using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace AcornDB.Storage.BPlusTree
{
    /// <summary>
    /// Write-Ahead Log (WAL) manager for crash-safe page writes.
    ///
    /// WAL protocol:
    ///   1. Before modifying any data page, write the new page image to the WAL.
    ///   2. After all page images for a batch are written, write a commit record with the new root pointer.
    ///   3. fsync the WAL.
    ///   4. Apply page images to the data file (can be lazy / checkpointed).
    ///   5. Once all WAL entries are applied, truncate the WAL.
    ///
    /// WAL record format:
    ///   [RecordType:1][PageId:8][DataLength:4][PageData:N][RecordCRC:4]
    ///
    /// Commit record:
    ///   [RecordType:1 (=0x02)][RootPageId:8][Generation:8][EntryCount:8][CommitCRC:4]
    ///
    /// Recovery:
    ///   On startup, replay any committed but unapplied WAL entries to the data file.
    ///   Incomplete (uncommitted) entries at the end of the WAL are discarded.
    /// </summary>
    internal sealed class WalManager : IDisposable
    {
        private readonly string _walFilePath;
        private readonly PageManager _pageManager;
        private readonly int _pageSize;
        private FileStream? _walStream;
        private readonly object _walLock = new();
        private int _uncommittedCount;
        private long _committedSinceCheckpoint;

        internal const byte RECORD_TYPE_PAGE = 0x01;
        internal const byte RECORD_TYPE_COMMIT = 0x02;

        internal WalManager(string walFilePath, PageManager pageManager, int pageSize)
        {
            _walFilePath = walFilePath;
            _pageManager = pageManager;
            _pageSize = pageSize;

            _walStream = new FileStream(
                walFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 0,
                FileOptions.SequentialScan | FileOptions.WriteThrough);
        }

        /// <summary>
        /// Record a dirty page image in the WAL (not yet committed).
        /// Called during tree modification before updating the data file.
        /// </summary>
        internal void WritePageImage(long pageId, ReadOnlySpan<byte> pageData)
        {
            lock (_walLock)
            {
                // WAL record: [Type:1][PageId:8][DataLen:4][Data:N][CRC:4]
                int recordSize = 1 + 8 + 4 + _pageSize + 4;
                var buffer = ArrayPool<byte>.Shared.Rent(recordSize);
                try
                {
                    var span = buffer.AsSpan(0, recordSize);
                    span[0] = RECORD_TYPE_PAGE;
                    BinaryPrimitives.WriteInt64LittleEndian(span.Slice(1), pageId);
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(9), _pageSize);
                    pageData.Slice(0, _pageSize).CopyTo(span.Slice(13));

                    uint crc = Crc32.Compute(span.Slice(0, recordSize - 4));
                    BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(recordSize - 4), crc);

                    _walStream!.Write(span);
                    _uncommittedCount++;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        /// <summary>
        /// Write a commit record to the WAL and fsync.
        /// After this returns, the batch is durable.
        /// </summary>
        internal void CommitRootUpdate(long newRootPageId, long generation, long entryCount)
        {
            lock (_walLock)
            {
                // Commit record: [Type:1][RootPageId:8][Generation:8][EntryCount:8][CRC:4]
                Span<byte> record = stackalloc byte[29];
                record[0] = RECORD_TYPE_COMMIT;
                BinaryPrimitives.WriteInt64LittleEndian(record.Slice(1), newRootPageId);
                BinaryPrimitives.WriteInt64LittleEndian(record.Slice(9), generation);
                BinaryPrimitives.WriteInt64LittleEndian(record.Slice(17), entryCount);

                uint crc = Crc32.Compute(record.Slice(0, 25));
                BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(25), crc);

                _walStream!.Write(record);
                _walStream.Flush(flushToDisk: true);
                _committedSinceCheckpoint += _uncommittedCount;
                _uncommittedCount = 0;
            }
        }

        /// <summary>
        /// Number of page images committed to the WAL since the last checkpoint.
        /// Used by the trunk to decide when to trigger automatic checkpointing.
        /// </summary>
        internal long CommittedSinceCheckpoint
        {
            get { lock (_walLock) return _committedSinceCheckpoint; }
        }

        /// <summary>
        /// Recover from the WAL on startup.
        /// Replays committed page writes to the data file, discards uncommitted tail.
        /// </summary>
        internal void Recover()
        {
            if (_walStream == null || _walStream.Length == 0)
                return;

            _walStream.Position = 0;

            var pageEntries = new List<(long PageId, byte[] Data)>();

            // Pre-allocate buffers for recovery (cold path — no need for stackalloc)
            int pageRecordSize = 8 + 4 + _pageSize + 4;
            var fullRecordBuf = new byte[1 + pageRecordSize];
            var commitBuf = new byte[28];
            var fullCommitBuf = new byte[29];

            while (_walStream.Position < _walStream.Length)
            {
                int typeByte = _walStream.ReadByte();
                if (typeByte < 0) break; // EOF

                if (typeByte == RECORD_TYPE_PAGE)
                {
                    int read = _walStream.Read(fullRecordBuf, 1, pageRecordSize);
                    if (read < pageRecordSize) break; // Truncated record — discard

                    // Validate CRC
                    fullRecordBuf[0] = RECORD_TYPE_PAGE;
                    int fullLen = 1 + pageRecordSize;

                    uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(
                        fullRecordBuf.AsSpan(fullLen - 4, 4));
                    uint computedCrc = Crc32.Compute(
                        fullRecordBuf.AsSpan(0, fullLen - 4));
                    if (storedCrc != computedCrc) break; // Corrupted — discard from here

                    long pageId = BinaryPrimitives.ReadInt64LittleEndian(fullRecordBuf.AsSpan(1, 8));
                    var pageData = new byte[_pageSize];
                    Array.Copy(fullRecordBuf, 1 + 12, pageData, 0, _pageSize);
                    pageEntries.Add((pageId, pageData));
                }
                else if (typeByte == RECORD_TYPE_COMMIT)
                {
                    int read = _walStream.Read(commitBuf, 0, 28);
                    if (read < 28) break; // Truncated

                    // Validate commit CRC
                    fullCommitBuf[0] = RECORD_TYPE_COMMIT;
                    Array.Copy(commitBuf, 0, fullCommitBuf, 1, 28);

                    uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(
                        fullCommitBuf.AsSpan(25, 4));
                    uint computedCrc = Crc32.Compute(
                        fullCommitBuf.AsSpan(0, 25));
                    if (storedCrc != computedCrc) break; // Corrupted commit

                    // Committed: apply all accumulated page writes to data file
                    foreach (var (pageId, data) in pageEntries)
                    {
                        _pageManager.WritePage(pageId, data);
                    }
                    _pageManager.Flush();

                    long rootPageId = BinaryPrimitives.ReadInt64LittleEndian(commitBuf.AsSpan(0, 8));
                    long generation = BinaryPrimitives.ReadInt64LittleEndian(commitBuf.AsSpan(8, 8));
                    long entryCount = BinaryPrimitives.ReadInt64LittleEndian(commitBuf.AsSpan(16, 8));
                    _pageManager.WriteSuperblock(rootPageId, generation, entryCount);

                    pageEntries.Clear();
                }
                else
                {
                    break; // Unknown record type — stop
                }
            }

            // Truncate WAL after recovery (all committed entries applied)
            _walStream.SetLength(0);
            _walStream.Flush(flushToDisk: true);
        }

        /// <summary>
        /// Checkpoint: ensure all WAL entries are applied to the data file, then truncate WAL.
        /// In normal operation, page writes go to both WAL and data file, so checkpoint
        /// just truncates the WAL.
        /// </summary>
        internal void Checkpoint()
        {
            lock (_walLock)
            {
                _walStream?.SetLength(0);
                _walStream?.Flush(flushToDisk: true);
                _committedSinceCheckpoint = 0;
            }
        }

        public void Dispose()
        {
            _walStream?.Dispose();
            _walStream = null;
        }
    }
}
