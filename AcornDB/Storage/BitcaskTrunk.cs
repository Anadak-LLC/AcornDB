using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Policy;
using AcornDB.Storage.Serialization;

namespace AcornDB.Storage
{
    /// <summary>
    /// Bitcask-style append-only log trunk with in-memory keydir index.
    /// Uses memory-mapped files, binary serialization, write batching, and lock-free reads.
    ///
    /// Record format v2 (deterministic, self-delimiting):
    ///   [Magic:4][FormatVer:2][Flags:2][KeyLen:4][PayloadLen:4][Timestamp:8][NutVersion:4][CRC32:4][KeyBytes][PayloadBytes]
    ///   Total header: 32 bytes. CRC32 covers KeyBytes + PayloadBytes.
    ///
    /// Legacy format v1 (read-only, for backward compatibility):
    ///   [Magic:4][Version:4][Timestamp:8][PayloadLen:4][IdBytes(null-term or not)][PayloadBytes]
    ///   Header: 20 bytes. No CRC. ID termination inconsistent between write paths.
    ///
    /// Storage Pipeline:
    ///   Write: Nut&lt;T&gt; → Serialize → Root Chain (ascending) → v2 binary record → MMF
    ///   Read:  MMF → parse header → payload bytes → Root Chain (descending) → Deserialize → Nut&lt;T&gt;
    /// </summary>
    public class BitcaskTrunk<T> : TrunkBase<T>, IDisposable where T : class
    {
        private readonly string _filePath;
        private readonly ConcurrentDictionary<string, NutEntry> _index;
        private readonly bool _validateCrcOnRead;
        private readonly CompactionOptions _compactionOptions;
        private long _filePosition;
        private readonly SemaphoreSlim _mmfWriteLock = new(1, 1);
        private FileStream? _fileStream;
        private volatile bool _indexLoaded = false;

        // Compaction tracking — updated during writes, deletes, and index load.
        private int _deadRecordCount;
        private int _totalRecordCount;
        private int _mutationsSinceCompaction;
        private Timer? _compactionTimer;
        private int _compactionInProgress; // 0 = idle, 1 = running (CAS guard)

        /// <summary>
        /// Immutable, reference-counted wrapper around a MemoryMappedFile + ViewAccessor pair.
        /// Readers acquire/release to keep the accessor alive during reads.
        /// Writers create a new holder and swap atomically; the old holder is disposed
        /// when its last reader releases.
        /// </summary>
        private sealed class AccessorHolder : IDisposable
        {
            private readonly MemoryMappedFile _mmf;
            public readonly MemoryMappedViewAccessor Accessor;
            private int _refCount = 1; // starts at 1 (owner reference)
            private int _disposed;

            public AccessorHolder(MemoryMappedFile mmf, MemoryMappedViewAccessor accessor)
            {
                _mmf = mmf;
                Accessor = accessor;
            }

            public long Capacity => Accessor.Capacity;

            /// <summary>
            /// Acquire a reader reference. Returns false if the holder has already been marked for disposal.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryAddRef()
            {
                while (true)
                {
                    int current = Volatile.Read(ref _refCount);
                    if (current <= 0) return false; // already draining
                    if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current)
                        return true;
                }
            }

            /// <summary>
            /// Release a reference. When the last reference is released, disposes the accessor and MMF.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Release()
            {
                if (Interlocked.Decrement(ref _refCount) == 0)
                {
                    DisposeCore();
                }
            }

            public void Dispose()
            {
                Release();
            }

            private void DisposeCore()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    Accessor.Dispose();
                    _mmf.Dispose();
                }
            }
        }

        /// <summary>
        /// Current accessor holder. Readers snapshot via Volatile.Read; writers swap via Interlocked.Exchange.
        /// </summary>
        private AccessorHolder? _holder;

        private const int INITIAL_FILE_SIZE = 64 * 1024 * 1024; // 64MB initial
        private const int BUFFER_THRESHOLD = 256;
        private const int FLUSH_INTERVAL_MS = 100;

        // v1 format constants (legacy, read-only)
        private const int MAGIC_NUMBER = 0x41434F52;   // 'ACOR' — v1
        private const int V1_HEADER_SIZE = 20;

        // v2 format constants
        private const int MAGIC_NUMBER_V2 = 0x41435232; // 'ACR2' — v2
        private const short FORMAT_VERSION_2 = 2;
        private const int V2_HEADER_SIZE = 32;

        // Flags (v2) — reserved bits for future use
        [Flags]
        private enum RecordFlags : short
        {
            None       = 0,
            Tombstone  = 1 << 0,  // Logical delete marker — written by Toss(), removes key on index load
            Compressed = 1 << 1,  // Payload was compressed by a root (informational)
            Encrypted  = 1 << 2,  // Payload was encrypted by a root (informational)
        }

        /// <summary>
        /// In-memory index entry (keydir). Stores offsets for O(1) payload access.
        /// </summary>
        private sealed class NutEntry
        {
            /// <summary>Absolute file offset to the start of the full record (including header).</summary>
            public long RecordOffset { get; set; }
            /// <summary>Absolute file offset to the start of the payload bytes.</summary>
            public long PayloadOffset { get; set; }
            /// <summary>Length of the payload in bytes.</summary>
            public int PayloadLength { get; set; }
            public DateTime Timestamp { get; set; }
            public int Version { get; set; }
            /// <summary>True if this entry was read from v2 format; false for v1 legacy.</summary>
            public bool IsV2 { get; set; }
        }

        /// <summary>
        /// Validate CRC32 for a v2 record. Reads the stored CRC from the header and computes
        /// the expected CRC over key + payload bytes. Throws <see cref="CrcValidationException"/> on mismatch.
        /// </summary>
        private static void ValidateRecordCrc(MemoryMappedViewAccessor accessor, long recordOffset, long keyOffset, int keyLen, long payloadOffset, int payloadLen)
        {
            uint storedCrc = accessor.ReadUInt32(recordOffset + 28);

            var keyBuf = ArrayPool<byte>.Shared.Rent(keyLen);
            var payloadBuf = ArrayPool<byte>.Shared.Rent(payloadLen);
            try
            {
                accessor.ReadArray(keyOffset, keyBuf, 0, keyLen);
                accessor.ReadArray(payloadOffset, payloadBuf, 0, payloadLen);

                var crc = new Crc32();
                crc.Append(keyBuf.AsSpan(0, keyLen));
                crc.Append(payloadBuf.AsSpan(0, payloadLen));
                uint computedCrc = crc.GetCurrentHashAsUInt32();

                if (storedCrc != computedCrc)
                {
                    throw new CrcValidationException(recordOffset, storedCrc, computedCrc);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(keyBuf);
                ArrayPool<byte>.Shared.Return(payloadBuf);
            }
        }

        /// <param name="customPath">Custom directory for the data file. Defaults to <c>data/{TypeName}</c>.</param>
        /// <param name="serializer">Optional custom serializer.</param>
        /// <param name="validateCrcOnRead">
        /// When <c>true</c>, CRC32 is validated during <see cref="Crack"/> and <see cref="LoadIndex"/>.
        /// Detects silent data corruption at the cost of additional computation per read.
        /// </param>
        /// <param name="compactionOptions">
        /// Controls automatic compaction behavior. Defaults to <see cref="CompactionOptions.Default"/>
        /// which provides balanced thresholds suitable for most workloads.
        /// Use <see cref="CompactionOptions.Manual"/> to disable automatic compaction entirely.
        /// </param>
        public BitcaskTrunk(
            string? customPath = null,
            ISerializer? serializer = null,
            bool validateCrcOnRead = false,
            CompactionOptions? compactionOptions = null)
            : base(
                serializer,
                enableBatching: true,
                batchThreshold: BUFFER_THRESHOLD,
                flushIntervalMs: FLUSH_INTERVAL_MS)
        {
            _validateCrcOnRead = validateCrcOnRead;
            _compactionOptions = compactionOptions ?? CompactionOptions.Default;

            var typeName = typeof(T).Name;
            var folderPath = customPath ?? Path.Combine(Directory.GetCurrentDirectory(), "data", typeName);
            Directory.CreateDirectory(folderPath);

            _filePath = Path.Combine(folderPath, "btree_v2.db");
            _index = new ConcurrentDictionary<string, NutEntry>();

            InitializeMemoryMappedFile();
            StartCompactionTimerIfEnabled();
        }

        private void InitializeMemoryMappedFile()
        {
            bool isNew = !File.Exists(_filePath);

            if (isNew)
            {
                _fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read,
                    8192, FileOptions.RandomAccess);
                _fileStream.SetLength(INITIAL_FILE_SIZE);
                _filePosition = 0;
            }
            else
            {
                _fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read,
                    8192, FileOptions.RandomAccess);
                _filePosition = _fileStream.Length;
            }

            var capacity = Math.Max(INITIAL_FILE_SIZE, _filePosition + INITIAL_FILE_SIZE);
            var mmf = MemoryMappedFile.CreateFromFile(
                _fileStream,
                null,
                capacity,
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: true);

            var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            _holder = new AccessorHolder(mmf, accessor);
        }

        #region Index Loading (v1 + v2 format detection)

        private void LoadIndex()
        {
            if (_filePosition == 0) return;

            _totalRecordCount = 0;
            _deadRecordCount = 0;

            var accessor = _holder!.Accessor;
            long position = 0;

            while (position < _filePosition)
            {
                // Need at least 4 bytes to read magic
                if (position + 4 > _filePosition) break;

                int magic = accessor.ReadInt32(position);

                if (magic == MAGIC_NUMBER_V2)
                {
                    if (!TryLoadV2Record(accessor, ref position)) break;
                }
                else if (magic == MAGIC_NUMBER)
                {
                    if (!TryLoadV1Record(accessor, ref position)) break;
                }
                else
                {
                    // Not a valid record — end of valid data (zeroed region or corruption)
                    break;
                }
            }

            _filePosition = position;
        }

        /// <summary>
        /// Parse a v2 record header and add to index. Advances position past the record.
        /// </summary>
        private bool TryLoadV2Record(MemoryMappedViewAccessor accessor, ref long position)
        {
            if (position + V2_HEADER_SIZE > _filePosition) return false;

            // Read v2 header fields
            // [Magic:4][FormatVer:2][Flags:2][KeyLen:4][PayloadLen:4][Timestamp:8][NutVersion:4][CRC32:4]
            short formatVer = accessor.ReadInt16(position + 4);
            if (formatVer != FORMAT_VERSION_2) return false;

            var flags      = (RecordFlags)accessor.ReadInt16(position + 6);
            int keyLen     = accessor.ReadInt32(position + 8);
            int payloadLen = accessor.ReadInt32(position + 12);
            long tsBinary  = accessor.ReadInt64(position + 16);
            int version    = accessor.ReadInt32(position + 24);

            // Sanity checks
            if (keyLen <= 0 || keyLen > 1024 * 1024) return false;   // key > 1MB is suspect
            if (payloadLen < 0 || payloadLen > int.MaxValue / 2) return false;

            long recordStart = position;
            long dataStart = position + V2_HEADER_SIZE;
            long totalRecordSize = V2_HEADER_SIZE + keyLen + payloadLen;

            if (position + totalRecordSize > _filePosition) return false;

            // Read key bytes
            var keyBytes = new byte[keyLen];
            accessor.ReadArray(dataStart, keyBytes, 0, keyLen);
            var id = Encoding.UTF8.GetString(keyBytes);

            long payloadOffset = dataStart + keyLen;

            // Optional CRC validation during index load — reject record on mismatch
            if (_validateCrcOnRead)
            {
                uint storedCrc = accessor.ReadUInt32(position + 28);

                var crc = new Crc32();
                crc.Append(keyBytes);

                if (payloadLen > 0)
                {
                    var payloadBuf = ArrayPool<byte>.Shared.Rent(payloadLen);
                    try
                    {
                        accessor.ReadArray(payloadOffset, payloadBuf, 0, payloadLen);
                        crc.Append(payloadBuf.AsSpan(0, payloadLen));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(payloadBuf);
                    }
                }

                uint computedCrc = crc.GetCurrentHashAsUInt32();
                if (storedCrc != computedCrc)
                    return false; // treat as end of valid data
            }

            _totalRecordCount++;

            // Tombstone records mark a key as deleted — remove from index rather than adding.
            if ((flags & RecordFlags.Tombstone) != 0)
            {
                _deadRecordCount++; // tombstone itself is dead space
                if (_index.TryRemove(id, out _))
                    _deadRecordCount++; // the record it deletes is also dead
                position += totalRecordSize;
                return true;
            }

            // If key already exists, the previous record becomes dead space.
            if (_index.ContainsKey(id))
                _deadRecordCount++;

            _index[id] = new NutEntry
            {
                RecordOffset = recordStart,
                PayloadOffset = payloadOffset,
                PayloadLength = payloadLen,
                Timestamp = DateTime.FromBinary(tsBinary),
                Version = version,
                IsV2 = true
            };

            position += totalRecordSize;
            return true;
        }

        /// <summary>
        /// Parse a v1 record header (legacy format). Handles both null-terminated and
        /// non-null-terminated ID variants by using PayloadLen to compute boundaries.
        /// </summary>
        private bool TryLoadV1Record(MemoryMappedViewAccessor accessor, ref long position)
        {
            if (position + V1_HEADER_SIZE > _filePosition) return false;

            // v1 header: [Magic:4][Version:4][Timestamp:8][PayloadLen:4]
            int version    = accessor.ReadInt32(position + 4);
            long tsBinary  = accessor.ReadInt64(position + 8);
            int payloadLen = accessor.ReadInt32(position + 16);

            if (payloadLen < 0 || payloadLen > int.MaxValue / 2) return false;

            long afterHeader = position + V1_HEADER_SIZE;

            // v1 ID parsing: scan for null terminator byte-by-byte.
            int idLen = 0;
            bool foundNull = false;

            long scanLimit = Math.Min(_filePosition, afterHeader + 65536); // keys > 64KB unreasonable
            while (afterHeader + idLen < scanLimit)
            {
                byte b = accessor.ReadByte(afterHeader + idLen);
                if (b == 0)
                {
                    foundNull = true;
                    break;
                }
                idLen++;
            }

            if (idLen == 0) return false;

            var idBytes = new byte[idLen];
            accessor.ReadArray(afterHeader, idBytes, 0, idLen);
            var id = Encoding.UTF8.GetString(idBytes);

            long payloadOffset;
            if (foundNull)
            {
                payloadOffset = afterHeader + idLen + 1; // skip null
            }
            else
            {
                // Sub-format (a): no null terminator — inherently unreliable for v1.
                return false;
            }

            if (payloadOffset + payloadLen > _filePosition) return false;

            long recordStart = position;

            _totalRecordCount++;
            if (_index.ContainsKey(id))
                _deadRecordCount++;

            _index[id] = new NutEntry
            {
                RecordOffset = recordStart,
                PayloadOffset = payloadOffset,
                PayloadLength = payloadLen,
                Timestamp = DateTime.FromBinary(tsBinary),
                Version = version,
                IsV2 = false
            };

            position = payloadOffset + payloadLen;
            return true;
        }

        #endregion

        #region Write Path

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Stash(string id, Nut<T> nut)
        {
            StashWithBatchingAsync(id, nut).GetAwaiter().GetResult();
        }

        protected override Task WriteToStorageAsync(string id, byte[] data, DateTime timestamp, int version)
        {
            WriteRecordToMappedFile(id, data, timestamp, version);

            Volatile.Read(ref _holder)!.Accessor.Flush();
            _fileStream!.Flush(flushToDisk: true);

            TryAutoCompact();

            return Task.CompletedTask;
        }

        protected override Task WriteBatchToStorageAsync(List<PendingWrite> batch)
        {
            foreach (var write in batch)
            {
                WriteRecordToMappedFile(write.Id, write.ProcessedData, write.Timestamp, write.Version);
            }

            Volatile.Read(ref _holder)!.Accessor.Flush();
            _fileStream!.Flush(flushToDisk: true);

            TryAutoCompact();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Write a tombstone record (logical delete marker) for the given key.
        /// Tombstone records have <see cref="RecordFlags.Tombstone"/> set, PayloadLen=0,
        /// and CRC covers only the key bytes. On index reload, tombstones remove the key.
        /// </summary>
        private void WriteTombstoneRecord(string id)
        {
            EnsureIndexLoaded();

            int maxKeyLen = Encoding.UTF8.GetMaxByteCount(id.Length);
            var keyBuffer = ArrayPool<byte>.Shared.Rent(maxKeyLen);
            try
            {
                int keyLen = Encoding.UTF8.GetBytes(id, 0, id.Length, keyBuffer, 0);
                int totalSize = V2_HEADER_SIZE + keyLen; // no payload

                var crc = new Crc32();
                crc.Append(keyBuffer.AsSpan(0, keyLen));
                uint crcValue = crc.GetCurrentHashAsUInt32();

                var headerPool = ArrayPool<byte>.Shared.Rent(V2_HEADER_SIZE);
                try
                {
                    var hdr = headerPool.AsSpan(0, V2_HEADER_SIZE);
                    BinaryPrimitives.WriteInt32LittleEndian(hdr,       MAGIC_NUMBER_V2);
                    BinaryPrimitives.WriteInt16LittleEndian(hdr[4..],  FORMAT_VERSION_2);
                    BinaryPrimitives.WriteInt16LittleEndian(hdr[6..],  (short)RecordFlags.Tombstone);
                    BinaryPrimitives.WriteInt32LittleEndian(hdr[8..],  keyLen);
                    BinaryPrimitives.WriteInt32LittleEndian(hdr[12..], 0); // PayloadLen = 0
                    BinaryPrimitives.WriteInt64LittleEndian(hdr[16..], DateTime.UtcNow.ToBinary());
                    BinaryPrimitives.WriteInt32LittleEndian(hdr[24..], 0); // version irrelevant for tombstone
                    BinaryPrimitives.WriteUInt32LittleEndian(hdr[28..], crcValue);

                    long offset;
                    _mmfWriteLock.Wait();
                    try
                    {
                        offset = _filePosition;
                        _filePosition += totalSize;
                        EnsureCapacityLocked(offset + totalSize);
                    }
                    finally
                    {
                        _mmfWriteLock.Release();
                    }

                    var holder = Volatile.Read(ref _holder)!;
                    var accessor = holder.Accessor;

                    accessor.WriteArray(offset, headerPool, 0, V2_HEADER_SIZE);
                    accessor.WriteArray(offset + V2_HEADER_SIZE, keyBuffer, 0, keyLen);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(headerPool);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(keyBuffer);
            }
        }

        /// <summary>
        /// Write a v2 record directly to the memory-mapped file without building an intermediate
        /// full-record buffer. Encodes key bytes into a pooled buffer, computes CRC from source
        /// arrays, writes header/key/payload as separate segments to the MMF.
        /// </summary>
        private void WriteRecordToMappedFile(string id, byte[] processedData, DateTime timestamp, int version)
        {
            // Ensure index is loaded so _filePosition reflects actual data end, not padded file length.
            // Without this, writes after reopen (without a prior read) would append at the wrong offset.
            EnsureIndexLoaded();

            // Encode key into pooled buffer
            int maxKeyLen = Encoding.UTF8.GetMaxByteCount(id.Length);
            var keyBuffer = ArrayPool<byte>.Shared.Rent(maxKeyLen);
            try
            {
                int keyLen = Encoding.UTF8.GetBytes(id, 0, id.Length, keyBuffer, 0);
                int payloadLen = processedData.Length;
                int totalSize = V2_HEADER_SIZE + keyLen + payloadLen;

                // CRC32 over key + payload (computed from source arrays, no extra copy)
                var crc = new Crc32();
                crc.Append(keyBuffer.AsSpan(0, keyLen));
                crc.Append(processedData);
                uint crcValue = crc.GetCurrentHashAsUInt32();

                // Build header into a pooled buffer
                var headerPool = ArrayPool<byte>.Shared.Rent(V2_HEADER_SIZE);
                try
                {
                    var hdr = headerPool.AsSpan(0, V2_HEADER_SIZE);
                    BinaryPrimitives.WriteInt32LittleEndian(hdr,       MAGIC_NUMBER_V2);
                    BinaryPrimitives.WriteInt16LittleEndian(hdr[4..],  FORMAT_VERSION_2);
                    BinaryPrimitives.WriteInt16LittleEndian(hdr[6..],  (short)RecordFlags.None);
                    BinaryPrimitives.WriteInt32LittleEndian(hdr[8..],  keyLen);
                    BinaryPrimitives.WriteInt32LittleEndian(hdr[12..], payloadLen);
                    BinaryPrimitives.WriteInt64LittleEndian(hdr[16..], timestamp.ToBinary());
                    BinaryPrimitives.WriteInt32LittleEndian(hdr[24..], version);
                    BinaryPrimitives.WriteUInt32LittleEndian(hdr[28..], crcValue);

                    // Reserve space and ensure capacity atomically under lock.
                    // This fixes D-05: no window where _filePosition is past capacity.
                    long offset;
                    _mmfWriteLock.Wait();
                    try
                    {
                        offset = _filePosition;
                        _filePosition += totalSize;
                        EnsureCapacityLocked(offset + totalSize);
                    }
                    finally
                    {
                        _mmfWriteLock.Release();
                    }

                    // Snapshot the current holder for writing — safe even if remap happens
                    // after this point because the old accessor remains valid until released.
                    var holder = Volatile.Read(ref _holder)!;
                    var accessor = holder.Accessor;

                    // Write three segments directly — no full-record buffer allocation
                    accessor.WriteArray(offset, headerPool, 0, V2_HEADER_SIZE);
                    accessor.WriteArray(offset + V2_HEADER_SIZE, keyBuffer, 0, keyLen);
                    accessor.WriteArray(offset + V2_HEADER_SIZE + keyLen, processedData, 0, payloadLen);

                    // Track dead records: if key already exists, previous record is now dead space.
                    bool isUpdate = _index.ContainsKey(id);

                    // Update in-memory index
                    _index[id] = new NutEntry
                    {
                        RecordOffset = offset,
                        PayloadOffset = offset + V2_HEADER_SIZE + keyLen,
                        PayloadLength = payloadLen,
                        Timestamp = timestamp,
                        Version = version,
                        IsV2 = true
                    };

                    Interlocked.Increment(ref _totalRecordCount);
                    if (isUpdate)
                    {
                        Interlocked.Increment(ref _deadRecordCount);
                        Interlocked.Increment(ref _mutationsSinceCompaction);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(headerPool);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(keyBuffer);
            }
        }

        // CRC32 is computed inline at call sites using Crc32.Append(ReadOnlySpan<byte>)
        // to avoid forcing callers to allocate exact-sized byte[] arrays.

        #endregion

        #region Read Path

        /// <summary>
        /// Ensures the on-disk index has been loaded into memory.
        /// Uses a double-checked lock pattern with volatile _indexLoaded.
        /// Must be called at the top of every public read/enumerate/delete entry point.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureIndexLoaded()
        {
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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Nut<T>? Crack(string id)
        {
            EnsureIndexLoaded();

            if (!_index.TryGetValue(id, out var entry))
                return null;

            // Snapshot the holder and acquire a reader reference.
            // This keeps the underlying accessor alive even if a concurrent writer remaps.
            var holder = Volatile.Read(ref _holder)!;
            if (!holder.TryAddRef())
            {
                // Extremely rare: holder was released between our read and TryAddRef.
                // Re-read the (already-swapped) new holder.
                holder = Volatile.Read(ref _holder)!;
                holder.TryAddRef(); // new holder guaranteed to have refs
            }

            try
            {
                var accessor = holder.Accessor;

                if (_validateCrcOnRead && entry.IsV2)
                {
                    long keyOffset = entry.RecordOffset + V2_HEADER_SIZE;
                    int keyLen = (int)(entry.PayloadOffset - keyOffset);
                    ValidateRecordCrc(accessor, entry.RecordOffset, keyOffset, keyLen, entry.PayloadOffset, entry.PayloadLength);
                }

                string json;

                if (_roots.Count == 0)
                {
                    // Fast path: no roots — rent buffer, decode UTF8 directly
                    var buffer = ArrayPool<byte>.Shared.Rent(entry.PayloadLength);
                    try
                    {
                        accessor.ReadArray(entry.PayloadOffset, buffer, 0, entry.PayloadLength);
                        json = Encoding.UTF8.GetString(buffer, 0, entry.PayloadLength);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                else
                {
                    // Roots path: allocate exact-size array (roots take ownership and may resize)
                    var payloadBytes = new byte[entry.PayloadLength];
                    accessor.ReadArray(entry.PayloadOffset, payloadBytes, 0, entry.PayloadLength);

                    var processedBytes = ProcessThroughRootsDescending(payloadBytes, id);

                    json = Encoding.UTF8.GetString(processedBytes);
                }

                // TrunkBase serializes the full Nut<T>; deserialize accordingly and extract payload
                var deserializedNut = _serializer.Deserialize<Nut<T>>(json);
                return new Nut<T>
                {
                    Id = id,
                    Timestamp = entry.Timestamp,
                    Version = entry.Version,
                    Payload = deserializedNut.Payload
                };
            }
            finally
            {
                holder.Release();
            }
        }

        #endregion

        #region Capacity Management

        /// <summary>
        /// Ensure the memory-mapped file has at least <paramref name="required"/> bytes of capacity.
        /// Caller MUST hold <see cref="_mmfWriteLock"/>.
        /// If a remap is needed, a new AccessorHolder is created and swapped atomically.
        /// The old holder is released (and will be disposed when its last reader finishes).
        /// </summary>
        private void EnsureCapacityLocked(long required)
        {
            var current = Volatile.Read(ref _holder)!;
            if (current.Capacity >= required) return;

            var newSize = Math.Max(required, current.Capacity * 2);

            _fileStream!.SetLength(newSize);

            var mmf = MemoryMappedFile.CreateFromFile(
                _fileStream,
                null,
                newSize,
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: true);

            var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            var newHolder = new AccessorHolder(mmf, accessor);

            // Atomic swap — readers that already snapshot the old holder continue safely.
            var oldHolder = Interlocked.Exchange(ref _holder, newHolder);
            // Release owner reference on old holder. It will be disposed when last reader releases.
            oldHolder?.Release();
        }

        #endregion

        #region Delete / Enumeration

        public override void Toss(string id)
        {
            EnsureIndexLoaded();

            if (_index.TryRemove(id, out _))
            {
                // Write tombstone to disk so the delete survives restart.
                WriteTombstoneRecord(id);

                // Flush immediately so the tombstone is durable.
                Volatile.Read(ref _holder)!.Accessor.Flush();
                _fileStream!.Flush(flushToDisk: true);

                // Tombstone + deleted record = 2 dead records, 1 new total record
                Interlocked.Increment(ref _totalRecordCount);
                Interlocked.Add(ref _deadRecordCount, 2);
                Interlocked.Increment(ref _mutationsSinceCompaction);

                TryAutoCompact();
            }
        }

        public override IEnumerable<Nut<T>> CrackAll()
        {
            EnsureIndexLoaded();
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
            throw new NotSupportedException("BitcaskTrunk does not support history. Use DocumentStoreTrunk for versioning.");
        }

        public override IEnumerable<Nut<T>> ExportChanges()
        {
            return CrackAll();
        }

        public override void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            EnsureIndexLoaded();
            foreach (var nut in incoming)
            {
                Stash(nut.Id, nut);
            }

            FlushBatchAsync().GetAwaiter().GetResult();
        }

        #endregion

        #region Compaction

        /// <summary>
        /// Compact the database file by rewriting only live entries in v2 format.
        /// Eliminates dead (deleted/overwritten) records and migrates any v1 records to v2.
        ///
        /// Optimizations over naive approach:
        ///   - Reads raw payload bytes directly (no deserialize/serialize round-trip).
        ///   - Uses ArrayPool for key and payload buffers (no per-record allocations).
        ///   - Writes header/key/payload as separate segments (no full-record buffer).
        ///   - Rebuilds the index inline (avoids re-scanning the newly written file).
        ///   - Uses BufferedStream for efficient batched I/O.
        /// </summary>
        public void Compact()
        {
            FlushBatchAsync().GetAwaiter().GetResult();

            _mmfWriteLock.Wait();
            try
            {
                var tempPath = _filePath + ".tmp";
                var holder = Volatile.Read(ref _holder)!;
                var accessor = holder.Accessor;

                // Snapshot live entries — iterating ConcurrentDictionary while rebuilding index inline
                var liveEntries = _index.ToArray();

                // Build the new index inline as we write — avoids a full LoadIndex() scan afterwards
                var newIndex = new ConcurrentDictionary<string, NutEntry>(
                    Environment.ProcessorCount, liveEntries.Length);

                // Pooled header buffer (reused across all records)
                var headerBuf = ArrayPool<byte>.Shared.Rent(V2_HEADER_SIZE);

                // Track the largest payload/key buffers rented so we can return them once
                byte[]? payloadBuf = null;
                byte[]? keyBuf = null;

                try
                {
                    long writePos = 0;

                    using (var rawFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var tempFs = new BufferedStream(rawFs, 64 * 1024))
                    {
                        foreach (var kvp in liveEntries)
                        {
                            var id = kvp.Key;
                            var entry = kvp.Value;

                            // Encode key into pooled buffer
                            int maxKeyLen = Encoding.UTF8.GetMaxByteCount(id.Length);
                            if (keyBuf == null || keyBuf.Length < maxKeyLen)
                            {
                                if (keyBuf != null) ArrayPool<byte>.Shared.Return(keyBuf);
                                keyBuf = ArrayPool<byte>.Shared.Rent(maxKeyLen);
                            }
                            int keyLen = Encoding.UTF8.GetBytes(id, 0, id.Length, keyBuf, 0);

                            // Read raw payload into pooled buffer
                            int payloadLen = entry.PayloadLength;
                            if (payloadBuf == null || payloadBuf.Length < payloadLen)
                            {
                                if (payloadBuf != null) ArrayPool<byte>.Shared.Return(payloadBuf);
                                payloadBuf = ArrayPool<byte>.Shared.Rent(payloadLen);
                            }
                            accessor.ReadArray(entry.PayloadOffset, payloadBuf, 0, payloadLen);

                            // CRC32 over key + payload (from pooled buffers, no copy)
                            var crc = new Crc32();
                            crc.Append(keyBuf.AsSpan(0, keyLen));
                            crc.Append(payloadBuf.AsSpan(0, payloadLen));
                            uint crcValue = crc.GetCurrentHashAsUInt32();

                            // Build header into pooled buffer
                            var hdr = headerBuf.AsSpan(0, V2_HEADER_SIZE);
                            BinaryPrimitives.WriteInt32LittleEndian(hdr,       MAGIC_NUMBER_V2);
                            BinaryPrimitives.WriteInt16LittleEndian(hdr[4..],  FORMAT_VERSION_2);
                            BinaryPrimitives.WriteInt16LittleEndian(hdr[6..],  (short)RecordFlags.None);
                            BinaryPrimitives.WriteInt32LittleEndian(hdr[8..],  keyLen);
                            BinaryPrimitives.WriteInt32LittleEndian(hdr[12..], payloadLen);
                            BinaryPrimitives.WriteInt64LittleEndian(hdr[16..], entry.Timestamp.ToBinary());
                            BinaryPrimitives.WriteInt32LittleEndian(hdr[24..], entry.Version);
                            BinaryPrimitives.WriteUInt32LittleEndian(hdr[28..], crcValue);

                            // Write three segments — no full-record buffer allocation
                            tempFs.Write(headerBuf, 0, V2_HEADER_SIZE);
                            tempFs.Write(keyBuf, 0, keyLen);
                            tempFs.Write(payloadBuf, 0, payloadLen);

                            // Track offsets for inline index rebuild
                            long payloadOffset = writePos + V2_HEADER_SIZE + keyLen;
                            newIndex[id] = new NutEntry
                            {
                                RecordOffset = writePos,
                                PayloadOffset = payloadOffset,
                                PayloadLength = payloadLen,
                                Timestamp = entry.Timestamp,
                                Version = entry.Version,
                                IsV2 = true
                            };

                            writePos += V2_HEADER_SIZE + keyLen + payloadLen;
                        }

                        tempFs.Flush();
                        rawFs.Flush(flushToDisk: true);
                    }

                    // Dispose old holder (we hold the write lock, so no new writes are in progress).
                    // Concurrent readers may still hold refs to this holder via TryAddRef —
                    // AccessorHolder ref-counting ensures disposal only occurs when the last
                    // reader releases. After InitializeMemoryMappedFile() assigns _holder to
                    // the new holder, readers doing Volatile.Read(ref _holder) will get the
                    // new one. Any reader that got the old holder but whose TryAddRef returns
                    // false will retry and find the new holder (see Crack() retry logic).
                    holder.Dispose();
                    _fileStream?.Dispose();

                    File.Delete(_filePath);
                    File.Move(tempPath, _filePath);

                    // Swap the index — no LoadIndex() scan needed
                    _index.Clear();
                    foreach (var kvp in newIndex)
                        _index[kvp.Key] = kvp.Value;

                    // Reinitialize MMF on the compacted file.
                    // InitializeMemoryMappedFile() sets _filePosition = _fileStream.Length,
                    // which for the freshly-written compacted file equals writePos. However,
                    // CreateFromFile then extends the file to MMF capacity (padded). We
                    // explicitly set _filePosition = writePos afterwards to be unambiguous
                    // and resilient to any future reordering within InitializeMemoryMappedFile.
                    _filePosition = 0;
                    InitializeMemoryMappedFile();
                    _filePosition = writePos;

                    // Reset compaction tracking — file now contains only live entries.
                    _totalRecordCount = liveEntries.Length;
                    _deadRecordCount = 0;
                    _mutationsSinceCompaction = 0;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(headerBuf);
                    if (payloadBuf != null) ArrayPool<byte>.Shared.Return(payloadBuf);
                    if (keyBuf != null) ArrayPool<byte>.Shared.Return(keyBuf);
                }
            }
            finally
            {
                _mmfWriteLock.Release();
            }
        }

        #endregion

        #region Auto-Compaction

        private void StartCompactionTimerIfEnabled()
        {
            if (_compactionOptions.DisableAutoCompaction) return;
            if (_compactionOptions.BackgroundCheckInterval is not { } interval) return;

            _compactionTimer = new Timer(
                _ => TryAutoCompact(),
                null,
                interval,
                interval);
        }

        /// <summary>
        /// Evaluates compaction thresholds and runs compaction if any are exceeded.
        /// Thread-safe: only one compaction can run at a time (CAS guard).
        /// </summary>
        private void TryAutoCompact()
        {
            if (_compactionOptions.DisableAutoCompaction) return;
            if (_disposed) return;
            if (!ShouldCompact()) return;

            // CAS guard: only one compaction at a time
            if (Interlocked.CompareExchange(ref _compactionInProgress, 1, 0) != 0) return;

            try
            {
                // Re-check after acquiring guard (thresholds may have changed)
                if (ShouldCompact())
                    Compact();
            }
            finally
            {
                Volatile.Write(ref _compactionInProgress, 0);
            }
        }

        /// <summary>
        /// Returns true if any enabled compaction threshold has been exceeded.
        /// </summary>
        private bool ShouldCompact()
        {
            int deadCount = Volatile.Read(ref _deadRecordCount);
            int totalCount = Volatile.Read(ref _totalRecordCount);
            int mutations = Volatile.Read(ref _mutationsSinceCompaction);

            // Nothing to compact
            if (deadCount == 0) return false;

            // Minimum file size gate
            long fileSize = Volatile.Read(ref _filePosition);
            if (fileSize < _compactionOptions.MinimumFileSizeBytes) return false;

            // Dead space ratio
            if (_compactionOptions.DeadSpaceRatioThreshold is { } ratioThreshold && totalCount > 0)
            {
                double deadRatio = (double)deadCount / totalCount;
                if (deadRatio >= ratioThreshold) return true;
            }

            // Dead record count
            if (_compactionOptions.DeadRecordCountThreshold is { } countThreshold)
            {
                if (deadCount >= countThreshold) return true;
            }

            // Mutation count since last compaction
            if (_compactionOptions.MutationCountThreshold is { } mutationThreshold)
            {
                if (mutations >= mutationThreshold) return true;
            }

            return false;
        }

        #endregion

        #region Disposal

        public override void Dispose()
        {
            if (_disposed) return;

            _compactionTimer?.Dispose();
            _compactionTimer = null;

            base.Dispose();

            var holder = Interlocked.Exchange(ref _holder, null);
            holder?.Dispose();
            _fileStream?.Dispose();
            _mmfWriteLock?.Dispose();
        }

        #endregion

        public override ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = false,
            TrunkType = "BitcaskTrunk",
        };
    }

    /// <summary>
    /// Thrown when CRC validation is enabled and a stored record's CRC does not match the computed value.
    /// Indicates silent data corruption in the storage file.
    /// </summary>
    public class CrcValidationException : Exception
    {
        public long RecordOffset { get; }
        public uint StoredCrc { get; }
        public uint ComputedCrc { get; }

        public CrcValidationException(long recordOffset, uint storedCrc, uint computedCrc)
            : base($"CRC mismatch at record offset {recordOffset}: stored 0x{storedCrc:X8}, computed 0x{computedCrc:X8}. The record is corrupt.")
        {
            RecordOffset = recordOffset;
            StoredCrc = storedCrc;
            ComputedCrc = computedCrc;
        }
    }
}
