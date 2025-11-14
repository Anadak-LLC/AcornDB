using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Policy;
using AcornDB.Storage.Serialization;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// High-performance BTree trunk using memory-mapped files, binary serialization,
    /// write batching, and lock-free reads. Designed to outperform LiteDB.
    /// Supports extensible IRoot processors for compression, encryption, policy enforcement, etc.
    ///
    /// Storage Pipeline:
    /// Write: Nut<T> → Serialize to binary → Root Chain (ascending) → byte[] → Write to MMF
    /// Read: Read MMF → byte[] → Root Chain (descending) → Deserialize from binary → Nut<T>
    /// </summary>
    public class BTreeTrunk<T> : TrunkBase<T>, IDisposable where T : class
    {
        private readonly string _filePath;
        private readonly ConcurrentDictionary<string, NutEntry> _index;
        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private long _filePosition;
        private readonly SemaphoreSlim _mmfWriteLock = new(1, 1);  // For MMF expansion, not batching
        private FileStream? _fileStream;
        private bool _indexLoaded = false;

        private const int INITIAL_FILE_SIZE = 64 * 1024 * 1024; // 64MB initial
        private const int BUFFER_THRESHOLD = 256; // Flush after 256 writes
        private const int FLUSH_INTERVAL_MS = 100; // Flush every 100ms
        private const int MAGIC_NUMBER = 0x41434F52; // 'ACOR' in hex

        private class NutEntry
        {
            public long Offset { get; set; }
            public int Length { get; set; }
            public DateTime Timestamp { get; set; }
            public int Version { get; set; }
        }

        // Binary format header: [Magic:4][Version:4][Timestamp:8][PayloadLen:4][Id][Payload]
        private const int HEADER_SIZE = 20;

        public BTreeTrunk(string? customPath = null, ISerializer? serializer = null)
            : base(
                serializer,
                enableBatching: true,           // Enable batching via TrunkBase
                batchThreshold: BUFFER_THRESHOLD,
                flushIntervalMs: FLUSH_INTERVAL_MS)
        {
            var typeName = typeof(T).Name;
            var folderPath = customPath ?? Path.Combine(Directory.GetCurrentDirectory(), "data", typeName);
            Directory.CreateDirectory(folderPath);

            _filePath = Path.Combine(folderPath, "btree_v2.db");
            _index = new ConcurrentDictionary<string, NutEntry>();

            InitializeMemoryMappedFile();
            // Note: Do NOT load index in constructor if roots might be needed
            // LoadIndex will be called automatically after adding roots or on first access
        }

        private void InitializeMemoryMappedFile()
        {
            bool isNew = !File.Exists(_filePath);

            if (isNew)
            {
                // Create new file
                _fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read,
                    8192, FileOptions.RandomAccess);
                _fileStream.SetLength(INITIAL_FILE_SIZE);
                _filePosition = 0;
            }
            else
            {
                // Open existing file
                _fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read,
                    8192, FileOptions.RandomAccess);
                _filePosition = _fileStream.Length;
            }

            // Create memory-mapped file for fast access
            var capacity = Math.Max(INITIAL_FILE_SIZE, _filePosition + INITIAL_FILE_SIZE);
            _mmf = MemoryMappedFile.CreateFromFile(
                _fileStream,
                null,
                capacity,
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: true);

            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
        }

        private void LoadIndex()
        {
            if (_filePosition == 0) return;

            // Fast index loading using memory-mapped file
            long position = 0;
            var buffer = ArrayPool<byte>.Shared.Rent(8192);

            try
            {
                while (position < _filePosition)
                {
                    // Read header
                    if (position + HEADER_SIZE > _filePosition) break;

                    int magic = _accessor!.ReadInt32(position);
                    if (magic != MAGIC_NUMBER) break; // Corrupted or end of valid data

                    int version = _accessor.ReadInt32(position + 4);
                    long timestampBinary = _accessor.ReadInt64(position + 8);
                    int payloadLen = _accessor.ReadInt32(position + 16);

                    position += HEADER_SIZE;

                    // Read ID (null-terminated string)
                    int idLen = 0;
                    while (position + idLen < _filePosition && _accessor.ReadByte(position + idLen) != 0)
                    {
                        idLen++;
                    }

                    if (idLen == 0 || position + idLen + 1 + payloadLen > _filePosition) break;

                    // Extract ID
                    var idBytes = new byte[idLen];
                    _accessor.ReadArray(position, idBytes, 0, idLen);
                    var id = Encoding.UTF8.GetString(idBytes);

                    position += idLen + 1; // +1 for null terminator

                    // Store entry (payload position)
                    _index[id] = new NutEntry
                    {
                        Offset = position,
                        Length = payloadLen,
                        Timestamp = DateTime.FromBinary(timestampBinary),
                        Version = version
                    };

                    position += payloadLen;
                }

                _filePosition = position;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Stash(string id, Nut<T> nut)
        {
            // Use TrunkBase batching infrastructure
            // This handles IRoot pipeline processing, batching, and auto-flush
            StashWithBatchingAsync(id, nut).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Write a single item to memory-mapped file (used by TrunkBase for immediate writes if needed)
        /// </summary>
        protected override async Task WriteToStorageAsync(string id, byte[] data, DateTime timestamp, int version)
        {
            // BTreeTrunk uses binary serialization, so we need to re-wrap the processed data
            var binaryData = WrapInBinaryFormat(id, data, timestamp, version);

            await Task.Run(() => WriteToMappedFile(id, binaryData, timestamp, version));

            // Flush immediately for non-batched writes
            _accessor!.Flush();
            _fileStream!.Flush(flushToDisk: true);
        }

        /// <summary>
        /// Write a batch of items to memory-mapped file (optimized bulk write)
        /// </summary>
        protected override async Task WriteBatchToStorageAsync(List<PendingWrite> batch)
        {
            await Task.Run(() =>
            {
                // Process batch items
                foreach (var write in batch)
                {
                    // Note: write.ProcessedData is already through IRoot pipeline,
                    // but we need to wrap it in BTree binary format
                    var binaryData = WrapInBinaryFormat(write.Id, write.ProcessedData, write.Timestamp, write.Version);

                    WriteToMappedFile(write.Id, binaryData, write.Timestamp, write.Version);
                }

                // Flush all writes to disk at once (batch optimization!)
                _accessor!.Flush();
                _fileStream!.Flush(flushToDisk: true);
            });
        }

        /// <summary>
        /// Wrap processed data in BTree binary format
        /// </summary>
        private byte[] WrapInBinaryFormat(string id, byte[] processedData, DateTime timestamp, int version)
        {
            var idBytes = Encoding.UTF8.GetBytes(id);
            var buffer = new byte[HEADER_SIZE + idBytes.Length + processedData.Length];

            int offset = 0;

            // Magic number
            BitConverter.GetBytes(MAGIC_NUMBER).CopyTo(buffer, offset);
            offset += 4;

            // Version
            BitConverter.GetBytes(version).CopyTo(buffer, offset);
            offset += 4;

            // Timestamp
            BitConverter.GetBytes(timestamp.ToBinary()).CopyTo(buffer, offset);
            offset += 8;

            // Payload length
            BitConverter.GetBytes(processedData.Length).CopyTo(buffer, offset);
            offset += 4;

            // ID bytes
            idBytes.CopyTo(buffer, offset);
            offset += idBytes.Length;

            // Processed payload
            processedData.CopyTo(buffer, offset);

            return buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteToMappedFile(string id, byte[] binaryData, DateTime timestamp, int version)
        {
            var offset = Interlocked.Add(ref _filePosition, binaryData.Length) - binaryData.Length;

            // Ensure capacity
            EnsureCapacity(offset + binaryData.Length);

            // Write data using memory-mapped file (fast!)
            _accessor!.WriteArray(offset, binaryData, 0, binaryData.Length);

            // Update index (lock-free with ConcurrentDictionary)
            // Store the entire processed data (including header, id, and transformed payload)
            _index[id] = new NutEntry
            {
                Offset = offset,
                Length = binaryData.Length,
                Timestamp = timestamp,
                Version = version
            };
        }

        private void EnsureCapacity(long required)
        {
            if (_accessor!.Capacity >= required) return;

            _mmfWriteLock.Wait();
            try
            {
                // Double check after acquiring lock
                if (_accessor.Capacity >= required) return;

                // Expand file (double the size)
                var newSize = Math.Max(required, _accessor.Capacity * 2);

                // Dispose old accessor and mmf
                _accessor?.Dispose();
                _mmf?.Dispose();

                // Expand underlying file
                _fileStream!.SetLength(newSize);

                // Recreate memory-mapped file
                _mmf = MemoryMappedFile.CreateFromFile(
                    _fileStream,
                    null,
                    newSize,
                    MemoryMappedFileAccess.ReadWrite,
                    HandleInheritability.None,
                    leaveOpen: true);

                _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            }
            finally
            {
                _mmfWriteLock.Release();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Nut<T>? Crack(string id)
        {
            // Ensure index is loaded (only matters if no roots were added)
            if (!_indexLoaded)
            {
                lock (_rootsLock)
                {
                    if (!_indexLoaded)
                    {
                        LoadIndex();
                        _indexLoaded = true;
                    }
                }
            }

            // Step 1: Lock-free read from index
            if (!_index.TryGetValue(id, out var entry))
                return null;

            // Step 2: Read entire stored byte array from memory-mapped file (includes header+id+payload)
            var buffer = ArrayPool<byte>.Shared.Rent(entry.Length);
            byte[] storedBytes;
            try
            {
                // Fast read from memory-mapped file
                _accessor!.ReadArray(entry.Offset, buffer, 0, entry.Length);
                storedBytes = buffer.AsSpan(0, entry.Length).ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            // Step 3: Process through root chain in descending sequence order (reverse)
            var processedBytes = ProcessThroughRootsDescending(storedBytes, id);

            // Step 4: Parse header and extract payload from processed bytes
            // Header format: [Magic:4][Version:4][Timestamp:8][PayloadLen:4][Id][Payload]
            int pos = HEADER_SIZE;

            // Skip ID (null-terminated)
            while (pos < processedBytes.Length && processedBytes[pos] != 0)
            {
                pos++;
            }
            pos++; // Skip null terminator

            // Extract payload
            var payloadBytes = processedBytes.AsSpan(pos);

            // Step 5: Deserialize from binary format
            return DeserializeBinary(payloadBytes, id, entry);
        }

        // Custom binary serialization - much faster than JSON for metadata
        private byte[] SerializeBinary(string id, Nut<T> nut)
        {
            // Serialize payload to JSON (still fast enough)
            var json = JsonConvert.SerializeObject(nut.Payload);
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            var idBytes = Encoding.UTF8.GetBytes(id);

            var totalSize = HEADER_SIZE + idBytes.Length + 1 + jsonBytes.Length;
            var buffer = new byte[totalSize];

            int pos = 0;

            // Write header
            BitConverter.TryWriteBytes(buffer.AsSpan(pos), MAGIC_NUMBER); pos += 4;
            BitConverter.TryWriteBytes(buffer.AsSpan(pos), nut.Version); pos += 4;
            BitConverter.TryWriteBytes(buffer.AsSpan(pos), nut.Timestamp.ToBinary()); pos += 8;
            BitConverter.TryWriteBytes(buffer.AsSpan(pos), jsonBytes.Length); pos += 4;

            // Write ID (null-terminated)
            Array.Copy(idBytes, 0, buffer, pos, idBytes.Length); pos += idBytes.Length;
            buffer[pos++] = 0; // Null terminator

            // Write payload
            Array.Copy(jsonBytes, 0, buffer, pos, jsonBytes.Length);

            return buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Nut<T>? DeserializeBinary(Span<byte> data, string id, NutEntry entry)
        {
            // Deserialize payload from JSON
            var json = Encoding.UTF8.GetString(data);
            var payload = JsonConvert.DeserializeObject<T>(json);

            return new Nut<T>
            {
                Id = id,
                Timestamp = entry.Timestamp,
                Version = entry.Version,
                Payload = payload
            };
        }

        public override void Toss(string id)
        {
            // Logical delete - just remove from index
            _index.TryRemove(id, out _);
        }

        public override IEnumerable<Nut<T>> CrackAll()
        {
            var results = new List<Nut<T>>(_index.Count);

            foreach (var kvp in _index)
            {
                var nut = Crack(kvp.Key);
                if (nut != null)
                    results.Add(nut);
            }

            return results;
        }

        public override IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("BTreeTrunk does not support history. Use DocumentStoreTrunk for versioning.");
        }

        public override IEnumerable<Nut<T>> ExportChanges()
        {
            return CrackAll();
        }

        public override void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            foreach (var nut in incoming)
            {
                Stash(nut.Id, nut);
            }

            // Force flush
            FlushBatchAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Compact the database file by removing deleted entries
        /// </summary>
        public void Compact()
        {
            // Flush any pending batched writes before compacting
            FlushBatchAsync().GetAwaiter().GetResult();

            _mmfWriteLock.Wait();
            try
            {
                var tempPath = _filePath + ".tmp";

                // Create new compacted file
                using (var tempFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    // Copy all active entries
                    foreach (var kvp in _index)
                    {
                        var nut = Crack(kvp.Key);
                        if (nut != null)
                        {
                            var data = SerializeBinary(kvp.Key, nut);
                            tempFs.Write(data, 0, data.Length);
                        }
                    }

                    tempFs.Flush(flushToDisk: true);
                }

                // Dispose current resources
                _accessor?.Dispose();
                _mmf?.Dispose();
                _fileStream?.Dispose();

                // Replace old file
                File.Delete(_filePath);
                File.Move(tempPath, _filePath);

                // Reinitialize
                _index.Clear();
                _filePosition = 0;
                InitializeMemoryMappedFile();
                LoadIndex();
            }
            finally
            {
                _mmfWriteLock.Release();
            }
        }

        public override void Dispose()
        {
            if (_disposed) return;

            // Base class handles timer disposal and flush
            // This ensures proper batching cleanup
            base.Dispose();

            // Dispose BTreeTrunk-specific resources
            _accessor?.Dispose();
            _mmf?.Dispose();
            _fileStream?.Dispose();
            _mmfWriteLock?.Dispose();
        }

        public override ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = false,
            TrunkType = "BTreeTrunk",
            
        };
    }
}
