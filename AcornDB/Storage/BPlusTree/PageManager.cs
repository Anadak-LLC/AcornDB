using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace AcornDB.Storage.BPlusTree
{
    /// <summary>
    /// Manages fixed-size page I/O for the B+Tree data file.
    ///
    /// File layout:
    ///   Page 0: Superblock (root pointer, format version, page size, generation)
    ///   Page 1+: B+Tree pages (internal nodes and leaf nodes)
    ///
    /// Uses RandomAccess.Read/Write for explicit offset I/O with Span{byte}.
    /// No memory-mapped files — the PageCache provides hot-path caching.
    /// </summary>
    internal sealed class PageManager : IDisposable
    {
        private readonly string _filePath;
        private readonly int _pageSize;
        private readonly bool _validateChecksumsOnRead;
        private FileStream _fileStream;
        private long _nextPageId;
        private readonly object _allocLock = new();
        private bool _disposed;

        // Superblock layout (page 0):
        //   [Magic:4][FormatVersion:2][PageSize:2][EntryCount:8]
        //   [RootPageId:8][RootGeneration:8]
        //   [FreeListHead:8]
        //   [SuperblockCRC:4]
        // Total: 42 bytes (rest of page 0 is reserved)
        //
        // EntryCount at offset 8 reuses the former Flags[4]+Reserved[4] fields
        // (always zero in v1 files). Migration: if EntryCount==0 && RootPageId!=0,
        // recount from leaf chain on open.
        internal const int MAGIC = 0x41504C53; // 'APLS' (AcornDB PlusTree)
        internal const ushort FORMAT_VERSION = 1;
        internal const int SUPERBLOCK_HEADER_SIZE = 42;
        internal const int SUPERBLOCK_CRC_OFFSET = 38;
        internal const int SUPERBLOCK_ENTRY_COUNT_OFFSET = 8;

        // Page header CRC field offset (matches BPlusTreeNavigator.HDR_PAGE_CRC)
        private const int HDR_PAGE_CRC = 18;
        private const int HDR_PAGE_CRC_LEN = 4;

        internal PageManager(string filePath, int pageSize, bool validateChecksumsOnRead = true)
        {
            _filePath = filePath;
            _pageSize = pageSize;
            _validateChecksumsOnRead = validateChecksumsOnRead;

            bool isNew = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;

            _fileStream = new FileStream(
                filePath,
                isNew ? FileMode.Create : FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 0, // Unbuffered — we manage our own caching
                FileOptions.RandomAccess);

            if (isNew)
            {
                InitializeNewFile();
            }
            else
            {
                ValidateAndLoad();
            }
        }

        private void InitializeNewFile()
        {
            // Write superblock (page 0) with empty root
            var superblock = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                Array.Clear(superblock, 0, _pageSize);
                WriteSuperblockToBuffer(superblock, rootPageId: 0, generation: 0, entryCount: 0);
                RandomAccess.Write(_fileStream.SafeFileHandle, superblock.AsSpan(0, _pageSize), 0);
                _fileStream.Flush(flushToDisk: true);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(superblock);
            }

            _nextPageId = 1; // Page 0 is superblock
        }

        private void ValidateAndLoad()
        {
            long fileLength = _fileStream.Length;
            if (fileLength < _pageSize)
                throw new InvalidDataException($"File too small for superblock: {fileLength} bytes (need at least {_pageSize}).");

            Span<byte> header = stackalloc byte[SUPERBLOCK_HEADER_SIZE];
            RandomAccess.Read(_fileStream.SafeFileHandle, header, 0);

            int magic = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (magic != MAGIC)
                throw new InvalidDataException($"Invalid B+Tree file: bad magic number 0x{magic:X8} (expected 0x{MAGIC:X8}).");

            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4));
            if (version > FORMAT_VERSION)
                throw new InvalidDataException($"Unsupported B+Tree format version {version} (max supported: {FORMAT_VERSION}).");

            ushort storedPageSize = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6));
            if (storedPageSize != _pageSize)
                throw new InvalidDataException($"Page size mismatch: file has {storedPageSize}, configured {_pageSize}.");

            // Validate superblock CRC (F-4)
            uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(SUPERBLOCK_CRC_OFFSET));
            uint computedCrc = Crc32.Compute(header.Slice(0, SUPERBLOCK_CRC_OFFSET));
            if (storedCrc != computedCrc)
                throw new InvalidDataException($"Superblock CRC mismatch: stored 0x{storedCrc:X8}, computed 0x{computedCrc:X8}.");

            _nextPageId = fileLength / _pageSize;
        }

        #region Page I/O

        /// <summary>
        /// Read a page into the provided buffer. Buffer must be at least PageSize bytes.
        /// Validates page CRC if checksums are enabled.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReadPage(long pageId, Span<byte> dest)
        {
            if (dest.Length < _pageSize)
                throw new ArgumentException($"Buffer too small: {dest.Length} < {_pageSize}");
            if (pageId < 1)
                throw new ArgumentOutOfRangeException(nameof(pageId), pageId, "Page ID must be >= 1 (page 0 is the superblock).");

            long currentNext = Volatile.Read(ref _nextPageId);
            if (pageId >= currentNext)
                throw new ArgumentOutOfRangeException(nameof(pageId), pageId, $"Page ID {pageId} is beyond allocated range [1..{currentNext - 1}].");

            long offset = pageId * _pageSize;
            RandomAccess.Read(_fileStream.SafeFileHandle, dest.Slice(0, _pageSize), offset);

            if (_validateChecksumsOnRead)
            {
                ValidatePageCrc(dest.Slice(0, _pageSize), pageId);
            }
        }

        /// <summary>
        /// Write a page from the provided buffer. Buffer must be at least PageSize bytes.
        /// Updates the internal page count if writing beyond the current allocation
        /// (e.g. during WAL recovery).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WritePage(long pageId, ReadOnlySpan<byte> src)
        {
            if (src.Length < _pageSize)
                throw new ArgumentException($"Buffer too small: {src.Length} < {_pageSize}");
            if (pageId < 1)
                throw new ArgumentOutOfRangeException(nameof(pageId), pageId, "Page ID must be >= 1 (page 0 is the superblock).");

            long offset = pageId * _pageSize;
            long requiredLength = offset + _pageSize;

            // Extend file if needed (WAL recovery may write pages beyond current file length)
            if (_fileStream.Length < requiredLength)
            {
                _fileStream.SetLength(requiredLength);
            }

            RandomAccess.Write(_fileStream.SafeFileHandle, src.Slice(0, _pageSize), offset);

            // Update _nextPageId if we wrote beyond the known allocation
            // Use a lock-free spin to avoid contention on the hot write path
            long next = pageId + 1;
            long current;
            do
            {
                current = Volatile.Read(ref _nextPageId);
                if (next <= current) break;
            } while (Interlocked.CompareExchange(ref _nextPageId, next, current) != current);
        }

        /// <summary>
        /// Allocate a new page, returning its ID. Thread-safe.
        /// The page is zero-initialized on disk.
        /// </summary>
        internal long AllocatePage()
        {
            lock (_allocLock)
            {
                long pageId = _nextPageId++;

                // Extend file to cover the new page
                long requiredLength = (pageId + 1) * _pageSize;
                if (_fileStream.Length < requiredLength)
                {
                    _fileStream.SetLength(requiredLength);
                }

                return pageId;
            }
        }

        /// <summary>
        /// Returns the current count of allocated pages (including the superblock).
        /// </summary>
        internal long PageCount => Volatile.Read(ref _nextPageId);

        /// <summary>
        /// Flush all written pages to disk.
        /// </summary>
        internal void Flush()
        {
            _fileStream.Flush(flushToDisk: true);
        }

        /// <summary>
        /// Validate CRC of a page buffer. Throws InvalidDataException on mismatch.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidatePageCrc(ReadOnlySpan<byte> page, long pageId)
        {
            uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(page.Slice(HDR_PAGE_CRC));
            uint computedCrc = Crc32.ComputeExcluding(page, HDR_PAGE_CRC, HDR_PAGE_CRC_LEN);
            if (storedCrc != computedCrc)
                throw new InvalidDataException($"Page {pageId} CRC mismatch: stored 0x{storedCrc:X8}, computed 0x{computedCrc:X8}.");
        }

        #endregion

        #region Superblock

        /// <summary>
        /// Read root page ID, generation, and entry count from the superblock (page 0).
        /// </summary>
        internal (long RootPageId, long Generation, long EntryCount) ReadSuperblock()
        {
            Span<byte> buf = stackalloc byte[SUPERBLOCK_HEADER_SIZE];
            RandomAccess.Read(_fileStream.SafeFileHandle, buf, 0);

            long entryCount = BinaryPrimitives.ReadInt64LittleEndian(buf.Slice(SUPERBLOCK_ENTRY_COUNT_OFFSET));
            long rootPageId = BinaryPrimitives.ReadInt64LittleEndian(buf.Slice(16));
            long generation = BinaryPrimitives.ReadInt64LittleEndian(buf.Slice(24));

            return (rootPageId, generation, entryCount);
        }

        /// <summary>
        /// Write root page ID, generation, and entry count to the superblock (page 0) and fsync.
        /// This is the commit point for the data file.
        /// </summary>
        internal void WriteSuperblock(long rootPageId, long generation, long entryCount)
        {
            var buf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                // Read existing superblock to preserve other fields
                RandomAccess.Read(_fileStream.SafeFileHandle, buf.AsSpan(0, _pageSize), 0);
                WriteSuperblockToBuffer(buf, rootPageId, generation, entryCount);
                RandomAccess.Write(_fileStream.SafeFileHandle, buf.AsSpan(0, _pageSize), 0);
                _fileStream.Flush(flushToDisk: true);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        private void WriteSuperblockToBuffer(Span<byte> buf, long rootPageId, long generation, long entryCount = 0)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf, MAGIC);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(4), FORMAT_VERSION);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(6), (ushort)_pageSize);
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(SUPERBLOCK_ENTRY_COUNT_OFFSET), entryCount);
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(16), rootPageId);
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(24), generation);
            // FreeListHead[8] at offset 32..39 — leave as-is for now

            // CRC32 over first 38 bytes (everything before the CRC field)
            uint crc = Crc32.Compute(buf.Slice(0, SUPERBLOCK_CRC_OFFSET));
            BinaryPrimitives.WriteUInt32LittleEndian(buf.Slice(SUPERBLOCK_CRC_OFFSET), crc);
        }

        internal int PageSize => _pageSize;
        internal bool ValidateChecksumsOnRead => _validateChecksumsOnRead;

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _fileStream?.Dispose();
        }
    }
}
