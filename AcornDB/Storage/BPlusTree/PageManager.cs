using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;

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
        private FileStream _fileStream;
        private long _nextPageId;
        private readonly object _allocLock = new();

        // Superblock layout (page 0):
        //   [Magic:4][FormatVersion:2][PageSize:2][Flags:4][Reserved:4]
        //   [RootPageId:8][RootGeneration:8]
        //   [FreeListHead:8]
        //   [SuperblockCRC:4]
        // Total: 42 bytes (rest of page 0 is reserved)
        internal const int MAGIC = 0x41504C53; // 'APLS' (AcornDB PlusTree)
        internal const ushort FORMAT_VERSION = 1;
        private const int SUPERBLOCK_HEADER_SIZE = 42;

        internal PageManager(string filePath, int pageSize)
        {
            _filePath = filePath;
            _pageSize = pageSize;

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
                WriteSuperblockToBuffer(superblock, rootPageId: 0, generation: 0);
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
            var header = (stackalloc byte[16]);
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

            _nextPageId = _fileStream.Length / _pageSize;
        }

        #region Page I/O

        /// <summary>
        /// Read a page into the provided buffer. Buffer must be at least PageSize bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReadPage(long pageId, Span<byte> dest)
        {
            if (dest.Length < _pageSize)
                throw new ArgumentException($"Buffer too small: {dest.Length} < {_pageSize}");

            long offset = pageId * _pageSize;
            RandomAccess.Read(_fileStream.SafeFileHandle, dest.Slice(0, _pageSize), offset);
        }

        /// <summary>
        /// Write a page from the provided buffer. Buffer must be at least PageSize bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WritePage(long pageId, ReadOnlySpan<byte> src)
        {
            if (src.Length < _pageSize)
                throw new ArgumentException($"Buffer too small: {src.Length} < {_pageSize}");

            long offset = pageId * _pageSize;
            RandomAccess.Write(_fileStream.SafeFileHandle, src.Slice(0, _pageSize), offset);
        }

        /// <summary>
        /// Allocate a new page, returning its ID. Thread-safe.
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
        /// Flush all written pages to disk.
        /// </summary>
        internal void Flush()
        {
            _fileStream.Flush(flushToDisk: true);
        }

        #endregion

        #region Superblock

        /// <summary>
        /// Read root page ID and generation from the superblock (page 0).
        /// </summary>
        internal (long RootPageId, long Generation) ReadSuperblock()
        {
            Span<byte> buf = stackalloc byte[SUPERBLOCK_HEADER_SIZE];
            RandomAccess.Read(_fileStream.SafeFileHandle, buf, 0);

            long rootPageId = BinaryPrimitives.ReadInt64LittleEndian(buf.Slice(16));
            long generation = BinaryPrimitives.ReadInt64LittleEndian(buf.Slice(24));

            return (rootPageId, generation);
        }

        /// <summary>
        /// Write root page ID and generation to the superblock (page 0) and fsync.
        /// This is the commit point for the data file.
        /// </summary>
        internal void WriteSuperblock(long rootPageId, long generation)
        {
            var buf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                // Read existing superblock to preserve other fields
                RandomAccess.Read(_fileStream.SafeFileHandle, buf.AsSpan(0, _pageSize), 0);
                WriteSuperblockToBuffer(buf, rootPageId, generation);
                RandomAccess.Write(_fileStream.SafeFileHandle, buf.AsSpan(0, _pageSize), 0);
                _fileStream.Flush(flushToDisk: true);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }

        private void WriteSuperblockToBuffer(Span<byte> buf, long rootPageId, long generation)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf, MAGIC);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(4), FORMAT_VERSION);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(6), (ushort)_pageSize);
            // Flags[4] + Reserved[4] at offset 8..15 — leave as-is
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(16), rootPageId);
            BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(24), generation);
            // FreeListHead[8] at offset 32..39 — leave as-is for now

            // CRC32 over first 38 bytes (everything before the CRC field)
            uint crc = Crc32.Compute(buf.Slice(0, 38));
            BinaryPrimitives.WriteUInt32LittleEndian(buf.Slice(38), crc);
        }

        internal int PageSize => _pageSize;

        #endregion

        public void Dispose()
        {
            _fileStream?.Dispose();
        }
    }
}
