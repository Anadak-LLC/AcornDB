using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace AcornDB.Storage.BTree
{
    /// <summary>
    /// Manages fixed-size page I/O for the B+Tree data file.
    ///
    /// File layout:
    ///   Page 0: Superblock (root pointer, format version, page size, generation, free list head)
    ///   Page 1+: B+Tree pages (internal nodes, leaf nodes, or free pages)
    ///
    /// Uses RandomAccess.Read/Write for explicit offset I/O with Span{byte}.
    /// No memory-mapped files — the PageCache provides hot-path caching.
    /// </summary>
    internal sealed class PageManager : IDisposable
    {
        private readonly string _filePath;
        private readonly int _pageSize;
        private readonly bool _validateChecksumsOnRead;
        private readonly bool _fsyncOnCommit;
        private FileStream _fileStream;
        private long _nextPageId;
        private readonly object _allocLock = new();
        private bool _disposed;

        /// <summary>
        /// In-memory free page stack. Pages are pushed here when freed (merge/delete)
        /// and popped on allocation. The head of this list is persisted in the superblock
        /// as a linked list threaded through the free pages on disk.
        /// </summary>
        private long _freeListHead;

        // Superblock layout (page 0):
        //   [Magic:4][FormatVersion:2][PageSize:2][EntryCount:8]
        //   [RootPageId:8][RootGeneration:8]
        //   [FreeListHead:4][Reserved:2]
        //   [SuperblockCRC:4]
        // Total: 42 bytes (rest of page 0 is reserved)
        // Note: FreeListHead is 4 bytes (int32) at offset 32. Supports up to ~2B pages (~16TB at 8KB).
        //
        // EntryCount at offset 8 reuses the former Flags[4]+Reserved[4] fields
        // (always zero in v1 files). Migration: if EntryCount==0 && RootPageId!=0,
        // recount from leaf chain on open.
        internal const int MAGIC = 0x41504C53; // 'APLS' (AcornDB PlusTree)
        internal const ushort FORMAT_VERSION = 1;
        internal const int SUPERBLOCK_HEADER_SIZE = 42;
        internal const int SUPERBLOCK_CRC_OFFSET = 38;
        internal const int SUPERBLOCK_ENTRY_COUNT_OFFSET = 8;
        internal const int SUPERBLOCK_FREE_LIST_HEAD_OFFSET = 32;

        // Page header CRC field offset (matches BTreeNavigator.HDR_PAGE_CRC)
        private const int HDR_PAGE_CRC = 18;
        private const int HDR_PAGE_CRC_LEN = 4;

        internal PageManager(string filePath, int pageSize, bool validateChecksumsOnRead = true, bool fsyncOnCommit = true)
        {
            _filePath = filePath;
            _pageSize = pageSize;
            _validateChecksumsOnRead = validateChecksumsOnRead;
            _fsyncOnCommit = fsyncOnCommit;

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
                WriteSuperblockToBuffer(superblock, rootPageId: 0, generation: 0, entryCount: 0, freeListHead: 0);
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
            _freeListHead = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(SUPERBLOCK_FREE_LIST_HEAD_OFFSET));
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
        /// Allocate a page, returning its ID. Thread-safe.
        /// Reuses a free page if available; otherwise extends the file.
        /// </summary>
        internal long AllocatePage()
        {
            lock (_allocLock)
            {
                if (_freeListHead != 0)
                {
                    // Pop from free list: read the free page to get the next pointer
                    long pageId = _freeListHead;
                    Span<byte> header = stackalloc byte[16];
                    long offset = pageId * _pageSize;
                    RandomAccess.Read(_fileStream.SafeFileHandle, header, offset);

                    // Free page format: [PageType:1 (0x03)][NextFreePageId:8]
                    long nextFree = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(1));
                    _freeListHead = nextFree;
                    return pageId;
                }

                long newPageId = _nextPageId++;

                // Extend file to cover the new page
                long requiredLength = (newPageId + 1) * _pageSize;
                if (_fileStream.Length < requiredLength)
                {
                    _fileStream.SetLength(requiredLength);
                }

                return newPageId;
            }
        }

        /// <summary>
        /// Return a page to the free list for reuse. The page is overwritten with
        /// a free-page marker and linked into the free list chain.
        /// Must be called under the same write serialization as tree modifications.
        /// The free list head is persisted in the superblock on the next commit.
        /// </summary>
        internal void FreePage(long pageId, WalManager wal)
        {
            lock (_allocLock)
            {
                var buf = ArrayPool<byte>.Shared.Rent(_pageSize);
                try
                {
                    Array.Clear(buf, 0, _pageSize);
                    var page = buf.AsSpan(0, _pageSize);

                    // Free page format: [PageType:1 = 0x03][NextFreePageId:8]
                    page[0] = PAGE_TYPE_FREE;
                    BinaryPrimitives.WriteInt64LittleEndian(page.Slice(1), _freeListHead);

                    // CRC over the page (excluding CRC field at HDR_PAGE_CRC)
                    uint crc = Crc32.ComputeExcluding(page, HDR_PAGE_CRC, HDR_PAGE_CRC_LEN);
                    BinaryPrimitives.WriteUInt32LittleEndian(page.Slice(HDR_PAGE_CRC), crc);

                    // Write to WAL first, then data file
                    wal.WritePageImage(pageId, page);
                    WritePage(pageId, page);

                    _freeListHead = pageId;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }
            }
        }

        /// <summary>
        /// Page type marker for free (recycled) pages.
        /// </summary>
        internal const byte PAGE_TYPE_FREE = 0x03;

        /// <summary>
        /// Returns the current count of allocated pages (including the superblock).
        /// </summary>
        internal long PageCount => Volatile.Read(ref _nextPageId);

        /// <summary>
        /// Flush all written pages to disk.
        /// </summary>
        internal void Flush()
        {
            _fileStream.Flush(flushToDisk: _fsyncOnCommit);
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
        /// Read root page ID, generation, entry count, and free list head from the superblock (page 0).
        /// </summary>
        internal (long RootPageId, long Generation, long EntryCount, long FreeListHead) ReadSuperblock()
        {
            Span<byte> buf = stackalloc byte[SUPERBLOCK_HEADER_SIZE];
            RandomAccess.Read(_fileStream.SafeFileHandle, buf, 0);

            long entryCount = BinaryPrimitives.ReadInt64LittleEndian(buf.Slice(SUPERBLOCK_ENTRY_COUNT_OFFSET));
            long rootPageId = BinaryPrimitives.ReadInt64LittleEndian(buf.Slice(16));
            long generation = BinaryPrimitives.ReadInt64LittleEndian(buf.Slice(24));
            long freeListHead = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(SUPERBLOCK_FREE_LIST_HEAD_OFFSET));

            return (rootPageId, generation, entryCount, freeListHead);
        }

        /// <summary>
        /// Write root page ID, generation, entry count, and free list head to the superblock (page 0) and fsync.
        /// This is the commit point for the data file.
        /// </summary>
        internal void WriteSuperblock(long rootPageId, long generation, long entryCount)
        {
            var buf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                // Read existing superblock to preserve other fields
                RandomAccess.Read(_fileStream.SafeFileHandle, buf.AsSpan(0, _pageSize), 0);
                WriteSuperblockToBuffer(buf, rootPageId, generation, entryCount, _freeListHead);
                RandomAccess.Write(_fileStream.SafeFileHandle, buf.AsSpan(0, _pageSize), 0);
                _fileStream.Flush(flushToDisk: _fsyncOnCommit);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        private void WriteSuperblockToBuffer(Span<byte> buf, long rootPageId, long generation, long entryCount, long freeListHead)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf, MAGIC);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(4), FORMAT_VERSION);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(6), (ushort)_pageSize);
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(SUPERBLOCK_ENTRY_COUNT_OFFSET), entryCount);
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(16), rootPageId);
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(24), generation);
            BinaryPrimitives.WriteInt32LittleEndian(buf.Slice(SUPERBLOCK_FREE_LIST_HEAD_OFFSET), (int)freeListHead);

            // CRC32 over first 38 bytes (everything before the CRC field)
            uint crc = Crc32.Compute(buf.Slice(0, SUPERBLOCK_CRC_OFFSET));
            BinaryPrimitives.WriteUInt32LittleEndian(buf.Slice(SUPERBLOCK_CRC_OFFSET), crc);
        }

        internal int PageSize => _pageSize;
        internal bool ValidateChecksumsOnRead => _validateChecksumsOnRead;

        /// <summary>
        /// Current head of the free page list. 0 means the list is empty.
        /// </summary>
        internal long FreeListHead
        {
            get { lock (_allocLock) return _freeListHead; }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _fileStream?.Dispose();
        }
    }
}
