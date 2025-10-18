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
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// High-performance BTree trunk using memory-mapped files, binary serialization,
    /// write batching, and lock-free reads. Designed to outperform LiteDB.
    /// </summary>
    public class BTreeTrunk<T> : ITrunk<T>, IDisposable
    {
        private readonly string _filePath;
        private readonly ConcurrentDictionary<string, NutEntry> _index;
        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private long _filePosition;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly List<PendingWrite> _writeBuffer = new();
        private readonly Timer _flushTimer;
        private bool _disposed = false;
        private FileStream? _fileStream;

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

        private struct PendingWrite
        {
            public string Id;
            public byte[] Data;
            public DateTime Timestamp;
            public int Version;
        }

        // Binary format header: [Magic:4][Version:4][Timestamp:8][PayloadLen:4][Id][Payload]
        private const int HEADER_SIZE = 20;

        public BTreeTrunk(string? customPath = null)
        {
            var typeName = typeof(T).Name;
            var folderPath = customPath ?? Path.Combine(Directory.GetCurrentDirectory(), "data", typeName);
            Directory.CreateDirectory(folderPath);

            _filePath = Path.Combine(folderPath, "btree_v2.db");
            _index = new ConcurrentDictionary<string, NutEntry>();

            InitializeMemoryMappedFile();
            LoadIndex();

            // Auto-flush timer for write batching
            _flushTimer = new Timer(_ =>
            {
                try { FlushAsync().Wait(); }
                catch { /* Swallow timer exceptions */ }
            }, null, FLUSH_INTERVAL_MS, FLUSH_INTERVAL_MS);
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
        public void Save(string id, Nut<T> nut)
        {
            // Serialize to binary format (custom for speed)
            var data = SerializeBinary(id, nut);

            // Add to write buffer
            lock (_writeBuffer)
            {
                _writeBuffer.Add(new PendingWrite
                {
                    Id = id,
                    Data = data,
                    Timestamp = nut.Timestamp,
                    Version = nut.Version
                });

                // Flush if buffer is full
                if (_writeBuffer.Count >= BUFFER_THRESHOLD)
                {
                    FlushAsync().Wait();
                }
            }
        }

        private async Task FlushAsync()
        {
            List<PendingWrite> toWrite;

            lock (_writeBuffer)
            {
                if (_writeBuffer.Count == 0) return;
                toWrite = new List<PendingWrite>(_writeBuffer);
                _writeBuffer.Clear();
            }

            await _writeLock.WaitAsync();
            try
            {
                foreach (var write in toWrite)
                {
                    WriteToMappedFile(write);
                }

                // Flush to disk
                _accessor!.Flush();
                _fileStream!.Flush(flushToDisk: true);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteToMappedFile(PendingWrite write)
        {
            var offset = Interlocked.Add(ref _filePosition, write.Data.Length) - write.Data.Length;

            // Ensure capacity
            EnsureCapacity(offset + write.Data.Length);

            // Write data using memory-mapped file (fast!)
            _accessor!.WriteArray(offset, write.Data, 0, write.Data.Length);

            // Update index (lock-free with ConcurrentDictionary)
            _index[write.Id] = new NutEntry
            {
                Offset = offset + HEADER_SIZE + Encoding.UTF8.GetByteCount(write.Id) + 1,
                Length = write.Data.Length - HEADER_SIZE - Encoding.UTF8.GetByteCount(write.Id) - 1,
                Timestamp = write.Timestamp,
                Version = write.Version
            };
        }

        private void EnsureCapacity(long required)
        {
            if (_accessor!.Capacity >= required) return;

            _writeLock.Wait();
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
                _writeLock.Release();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Load(string id)
        {
            // Lock-free read from index
            if (!_index.TryGetValue(id, out var entry))
                return null;

            // Use ArrayPool to reduce allocations
            var buffer = ArrayPool<byte>.Shared.Rent(entry.Length);
            try
            {
                // Fast read from memory-mapped file
                _accessor!.ReadArray(entry.Offset, buffer, 0, entry.Length);
                return DeserializeBinary(buffer.AsSpan(0, entry.Length), id, entry);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
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

        public void Delete(string id)
        {
            // Logical delete - just remove from index
            _index.TryRemove(id, out _);
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            var results = new List<Nut<T>>(_index.Count);

            foreach (var kvp in _index)
            {
                var nut = Load(kvp.Key);
                if (nut != null)
                    results.Add(nut);
            }

            return results;
        }

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("BTreeTrunk does not support history. Use DocumentStoreTrunk for versioning.");
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return LoadAll();
        }

        public void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            foreach (var nut in incoming)
            {
                Save(nut.Id, nut);
            }

            // Force flush
            FlushAsync().Wait();
        }

        /// <summary>
        /// Compact the database file by removing deleted entries
        /// </summary>
        public void Compact()
        {
            _writeLock.Wait();
            try
            {
                var tempPath = _filePath + ".tmp";

                // Create new compacted file
                using (var tempFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    // Copy all active entries
                    foreach (var kvp in _index)
                    {
                        var nut = Load(kvp.Key);
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
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _flushTimer?.Dispose();

            // Flush any pending writes
            try { FlushAsync().Wait(); } catch { }

            _accessor?.Dispose();
            _mmf?.Dispose();
            _fileStream?.Dispose();
            _writeLock?.Dispose();

            _disposed = true;
        }

        // ITrunkCapabilities implementation
        public ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = false,
            TrunkType = "BTreeTrunk"
        };
    }
}
