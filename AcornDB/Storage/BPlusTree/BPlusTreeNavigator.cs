using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AcornDB.Storage.BPlusTree
{
    /// <summary>
    /// B+Tree navigation, search, insert, delete, and scan logic.
    ///
    /// Operates on fixed-size pages via PageManager + PageCache.
    /// All page modifications go through WalManager for crash safety.
    ///
    /// Page layout (slotted page):
    ///   Header (fixed):
    ///     [PageType:1][Level:1][Flags:2][ItemCount:2][FreeSpaceStart:2]
    ///     [FreeSpaceEnd:2][RightSiblingPageId:8][PageCRC:4]
    ///   Total header: 22 bytes
    ///
    ///   Slot array (grows from header end, downward in logical terms):
    ///     Each slot: [Offset:2][Length:2] = 4 bytes per slot
    ///     Slot array starts at byte 22.
    ///
    ///   Records (grow from page end, upward):
    ///     Leaf record: [KeyLen:2][KeyBytes:N][ValLen:4][ValBytes:M]
    ///     Internal record: [KeyLen:2][KeyBytes:N][ChildPageId:8]
    ///
    ///   Free space is between end of slot array and start of records.
    ///
    /// PageType:
    ///   0x01 = Internal node
    ///   0x02 = Leaf node
    ///
    /// Level:
    ///   0 = leaf, 1+ = internal (root has highest level)
    ///
    /// Internal nodes additionally store a "leftmost child" pointer in the first 8 bytes
    /// of the record area (before the first key-child pair).
    /// </summary>
    internal sealed class BPlusTreeNavigator
    {
        private readonly PageManager _pageManager;
        private readonly PageCache _pageCache;
        private readonly int _pageSize;

        // Page header offsets
        internal const int HDR_PAGE_TYPE = 0;
        internal const int HDR_LEVEL = 1;
        internal const int HDR_FLAGS = 2;
        internal const int HDR_ITEM_COUNT = 4;
        internal const int HDR_FREE_SPACE_START = 6;
        internal const int HDR_FREE_SPACE_END = 8;
        internal const int HDR_RIGHT_SIBLING = 10;
        internal const int HDR_PAGE_CRC = 18;
        internal const int HEADER_SIZE = 22;

        // Slot size: offset (2) + length (2)
        internal const int SLOT_SIZE = 4;

        // Page types
        internal const byte PAGE_TYPE_INTERNAL = 0x01;
        internal const byte PAGE_TYPE_LEAF = 0x02;

        internal BPlusTreeNavigator(PageManager pageManager, PageCache pageCache, int pageSize)
        {
            _pageManager = pageManager;
            _pageCache = pageCache;
            _pageSize = pageSize;
        }

        #region Search

        /// <summary>
        /// Search for a key starting from the given root page.
        /// Returns the value bytes if found, null otherwise.
        /// O(log_B N) page reads.
        /// </summary>
        internal byte[]? Search(long rootPageId, ReadOnlySpan<byte> key)
        {
            var pageBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                long currentPageId = rootPageId;

                while (true)
                {
                    ReadPageCached(currentPageId, pageBuf);
                    var page = pageBuf.AsSpan(0, _pageSize);

                    byte pageType = page[HDR_PAGE_TYPE];
                    int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));

                    if (pageType == PAGE_TYPE_LEAF)
                    {
                        return SearchLeaf(page, itemCount, key);
                    }
                    else
                    {
                        currentPageId = SearchInternal(page, itemCount, key);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
        }

        private byte[]? SearchLeaf(ReadOnlySpan<byte> page, int itemCount, ReadOnlySpan<byte> key)
        {
            // Binary search over slot array
            int lo = 0, hi = itemCount - 1;

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                var (slotOffset, slotLen) = ReadSlot(page, mid);
                var record = page.Slice(slotOffset, slotLen);

                int keyLen = BinaryPrimitives.ReadUInt16LittleEndian(record);
                var recordKey = record.Slice(2, keyLen);

                int cmp = recordKey.SequenceCompareTo(key);
                if (cmp == 0)
                {
                    // Found: extract value
                    int valLenOffset = 2 + keyLen;
                    int valLen = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(valLenOffset));
                    return record.Slice(valLenOffset + 4, valLen).ToArray();
                }
                else if (cmp < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return null;
        }

        private long SearchInternal(ReadOnlySpan<byte> page, int itemCount, ReadOnlySpan<byte> key)
        {
            // Internal node: leftmost child pointer is stored at HEADER_SIZE position
            // (before slot array, but logically the "left" child of the first separator).
            // Actually, we store leftmost child as a special field right after the header.
            // Slot array starts at HEADER_SIZE + 8 for internal nodes.
            long leftmostChild = BinaryPrimitives.ReadInt64LittleEndian(page.Slice(HEADER_SIZE));

            if (itemCount == 0)
                return leftmostChild;

            // Binary search: find the rightmost separator <= key
            int lo = 0, hi = itemCount - 1;
            int insertionPoint = 0;

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                var (slotOffset, slotLen) = ReadInternalSlot(page, mid);
                var record = page.Slice(slotOffset, slotLen);

                int keyLen = BinaryPrimitives.ReadUInt16LittleEndian(record);
                var recordKey = record.Slice(2, keyLen);

                int cmp = recordKey.SequenceCompareTo(key);
                if (cmp <= 0)
                {
                    insertionPoint = mid + 1;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            if (insertionPoint == 0)
                return leftmostChild;

            // Follow the child pointer of the last separator <= key
            var (sOff, sLen) = ReadInternalSlot(page, insertionPoint - 1);
            var sep = page.Slice(sOff, sLen);
            int sepKeyLen = BinaryPrimitives.ReadUInt16LittleEndian(sep);
            long childPageId = BinaryPrimitives.ReadInt64LittleEndian(sep.Slice(2 + sepKeyLen));
            return childPageId;
        }

        #endregion

        #region Insert

        /// <summary>
        /// Insert a key-value pair into the B+Tree. Returns the new root page ID
        /// (may change if the root splits).
        /// </summary>
        internal long Insert(long rootPageId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WalManager wal)
        {
            if (rootPageId == 0)
            {
                // Empty tree: create first leaf
                return CreateInitialLeaf(key, value, wal);
            }

            var result = InsertRecursive(rootPageId, key, value, wal);

            if (result.Split)
            {
                // Root split: create new root
                return CreateNewRoot(rootPageId, result.SplitKey!, result.NewPageId, result.Level + 1, wal);
            }

            return rootPageId;
        }

        private InsertResult InsertRecursive(long pageId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WalManager wal)
        {
            var pageBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                ReadPageCached(pageId, pageBuf);
                var page = pageBuf.AsSpan(0, _pageSize);

                byte pageType = page[HDR_PAGE_TYPE];
                int level = page[HDR_LEVEL];

                if (pageType == PAGE_TYPE_LEAF)
                {
                    return InsertIntoLeaf(pageId, pageBuf, key, value, wal);
                }
                else
                {
                    // Find child to descend into
                    int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));
                    long childPageId = SearchInternal(page, itemCount, key);

                    var childResult = InsertRecursive(childPageId, key, value, wal);

                    if (childResult.Split)
                    {
                        // Insert the separator from the child split into this internal node
                        return InsertIntoInternal(pageId, pageBuf, childResult.SplitKey!, childResult.NewPageId, level, wal);
                    }

                    return new InsertResult { Split = false, Level = level };
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
        }

        private InsertResult InsertIntoLeaf(long pageId, byte[] pageBuf, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WalManager wal)
        {
            var page = pageBuf.AsSpan(0, _pageSize);
            int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));
            int freeStart = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_START));
            int freeEnd = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_END));

            // Record size: [KeyLen:2][Key:N][ValLen:4][Val:M]
            int recordSize = 2 + key.Length + 4 + value.Length;
            int slotSpaceNeeded = SLOT_SIZE;
            int totalNeeded = recordSize + slotSpaceNeeded;

            // Find insertion point (maintain sorted order)
            int insertIdx = FindLeafInsertionPoint(page, itemCount, key, out bool keyExists);

            if (keyExists)
            {
                // Update existing: replace value in-place or rewrite page
                // For simplicity, rewrite the record at the existing slot
                return UpdateLeafRecord(pageId, pageBuf, insertIdx, key, value, wal);
            }

            int freeSpace = freeEnd - freeStart;
            if (freeSpace >= totalNeeded)
            {
                // Fits: insert directly
                InsertLeafRecord(page, itemCount, insertIdx, freeStart, freeEnd, key, value, recordSize);

                // Update header
                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT), (ushort)(itemCount + 1));
                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_START), (ushort)(freeStart + SLOT_SIZE));
                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_END), (ushort)(freeEnd - recordSize));

                WritePageCrcAndFlush(pageId, pageBuf, wal);
                return new InsertResult { Split = false, Level = 0 };
            }
            else
            {
                // Split leaf
                return SplitLeafAndInsert(pageId, pageBuf, key, value, wal);
            }
        }

        private InsertResult InsertIntoInternal(long pageId, byte[] pageBuf, byte[] separatorKey, long newChildPageId, int level, WalManager wal)
        {
            var page = pageBuf.AsSpan(0, _pageSize);
            int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));

            // Internal record: [KeyLen:2][Key:N][ChildPageId:8]
            int recordSize = 2 + separatorKey.Length + 8;
            int totalNeeded = recordSize + SLOT_SIZE;

            // Check free space (internal nodes have leftmost child ptr at HEADER_SIZE, slots start at HEADER_SIZE + 8)
            int freeStart = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_START));
            int freeEnd = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_END));
            int freeSpace = freeEnd - freeStart;

            if (freeSpace >= totalNeeded)
            {
                int insertIdx = FindInternalInsertionPoint(page, itemCount, separatorKey);
                InsertInternalRecord(page, itemCount, insertIdx, freeStart, freeEnd, separatorKey, newChildPageId, recordSize);

                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT), (ushort)(itemCount + 1));
                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_START), (ushort)(freeStart + SLOT_SIZE));
                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_END), (ushort)(freeEnd - recordSize));

                WritePageCrcAndFlush(pageId, pageBuf, wal);
                return new InsertResult { Split = false, Level = level };
            }
            else
            {
                // Split internal node
                return SplitInternalAndInsert(pageId, pageBuf, separatorKey, newChildPageId, level, wal);
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// Delete a key from the B+Tree. Returns (newRootPageId, found).
        /// Does NOT merge underfull pages (MVP: simple delete-from-leaf).
        /// </summary>
        internal (long NewRootPageId, bool Found) Delete(long rootPageId, ReadOnlySpan<byte> key, WalManager wal)
        {
            if (rootPageId == 0)
                return (0, false);

            var pageBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                ReadPageCached(rootPageId, pageBuf);
                var page = pageBuf.AsSpan(0, _pageSize);
                byte pageType = page[HDR_PAGE_TYPE];
                int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));

                if (pageType == PAGE_TYPE_LEAF)
                {
                    bool found = DeleteFromLeaf(rootPageId, pageBuf, key, wal);
                    int newCount = BinaryPrimitives.ReadUInt16LittleEndian(pageBuf.AsSpan(HDR_ITEM_COUNT));
                    long newRoot = newCount == 0 ? 0 : rootPageId;
                    return (newRoot, found);
                }
                else
                {
                    // Navigate to leaf and delete
                    return DeleteRecursive(rootPageId, key, wal);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
        }

        private (long NewRootPageId, bool Found) DeleteRecursive(long pageId, ReadOnlySpan<byte> key, WalManager wal)
        {
            var pageBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                ReadPageCached(pageId, pageBuf);
                var page = pageBuf.AsSpan(0, _pageSize);
                byte pageType = page[HDR_PAGE_TYPE];
                int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));

                if (pageType == PAGE_TYPE_LEAF)
                {
                    bool found = DeleteFromLeaf(pageId, pageBuf, key, wal);
                    return (pageId, found);
                }

                long childPageId = SearchInternal(page, itemCount, key);
                var result = DeleteRecursive(childPageId, key, wal);
                return (pageId, result.Found);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
        }

        private bool DeleteFromLeaf(long pageId, byte[] pageBuf, ReadOnlySpan<byte> key, WalManager wal)
        {
            var page = pageBuf.AsSpan(0, _pageSize);
            int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));

            int deleteIdx = FindLeafInsertionPoint(page, itemCount, key, out bool keyExists);
            if (!keyExists)
                return false;

            // Remove the slot entry by shifting subsequent slots left
            int slotArrayStart = HEADER_SIZE;
            int slotToRemove = deleteIdx;
            for (int i = slotToRemove; i < itemCount - 1; i++)
            {
                int srcOff = slotArrayStart + (i + 1) * SLOT_SIZE;
                int dstOff = slotArrayStart + i * SLOT_SIZE;
                page.Slice(srcOff, SLOT_SIZE).CopyTo(page.Slice(dstOff));
            }

            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT), (ushort)(itemCount - 1));
            int freeStart = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_START));
            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_START), (ushort)(freeStart - SLOT_SIZE));

            // Note: record space is not reclaimed (fragmentation). Page compaction is deferred.
            WritePageCrcAndFlush(pageId, pageBuf, wal);
            return true;
        }

        #endregion

        #region Scan

        /// <summary>
        /// Scan all key-value pairs in sorted order by following leaf chain.
        /// </summary>
        internal IEnumerable<(byte[] Key, byte[] Value)> ScanAll(long rootPageId)
        {
            // Navigate to leftmost leaf
            long leafId = FindLeftmostLeaf(rootPageId);
            return ScanFromLeaf(leafId, null, null);
        }

        /// <summary>
        /// Range scan: return all entries with keys in [startKey, endKey] (inclusive).
        /// </summary>
        internal IEnumerable<(byte[] Key, byte[] Value)> RangeScan(long rootPageId, ReadOnlySpan<byte> startKey, ReadOnlySpan<byte> endKey)
        {
            // Navigate to the leaf containing startKey
            long leafId = FindLeafForKey(rootPageId, startKey);
            return ScanFromLeaf(leafId, startKey.ToArray(), endKey.ToArray());
        }

        private IEnumerable<(byte[] Key, byte[] Value)> ScanFromLeaf(long startLeafId, byte[]? startKey, byte[]? endKey)
        {
            // Note: Cannot use Span<byte> across yield boundaries.
            // Use byte[] and BinaryPrimitives with array slices instead.
            var pageBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                long currentLeafId = startLeafId;
                bool started = startKey == null;

                while (currentLeafId != 0)
                {
                    ReadPageCached(currentLeafId, pageBuf);
                    int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(pageBuf.AsSpan(HDR_ITEM_COUNT, 2));

                    for (int i = 0; i < itemCount; i++)
                    {
                        int slotPos = HEADER_SIZE + i * SLOT_SIZE;
                        ushort slotOffset = BinaryPrimitives.ReadUInt16LittleEndian(pageBuf.AsSpan(slotPos, 2));
                        ushort slotLen = BinaryPrimitives.ReadUInt16LittleEndian(pageBuf.AsSpan(slotPos + 2, 2));

                        int keyLen = BinaryPrimitives.ReadUInt16LittleEndian(pageBuf.AsSpan(slotOffset, 2));
                        var recordKey = new byte[keyLen];
                        Array.Copy(pageBuf, slotOffset + 2, recordKey, 0, keyLen);

                        if (!started)
                        {
                            if (recordKey.AsSpan().SequenceCompareTo(startKey) >= 0)
                                started = true;
                            else
                                continue;
                        }

                        if (endKey != null && recordKey.AsSpan().SequenceCompareTo(endKey) > 0)
                            yield break;

                        int valLenOffset = slotOffset + 2 + keyLen;
                        int valLen = BinaryPrimitives.ReadInt32LittleEndian(pageBuf.AsSpan(valLenOffset, 4));
                        var valueBytes = new byte[valLen];
                        Array.Copy(pageBuf, valLenOffset + 4, valueBytes, 0, valLen);

                        yield return (recordKey, valueBytes);
                    }

                    // Follow right sibling chain
                    long rightSibling = BinaryPrimitives.ReadInt64LittleEndian(pageBuf.AsSpan(HDR_RIGHT_SIBLING, 8));
                    currentLeafId = rightSibling;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
        }

        /// <summary>
        /// Count all entries by traversing leaf chain.
        /// </summary>
        internal long CountEntries(long rootPageId)
        {
            long leafId = FindLeftmostLeaf(rootPageId);
            long count = 0;

            var pageBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                while (leafId != 0)
                {
                    ReadPageCached(leafId, pageBuf);
                    var page = pageBuf.AsSpan(0, _pageSize);
                    count += BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));

                    leafId = BinaryPrimitives.ReadInt64LittleEndian(page.Slice(HDR_RIGHT_SIBLING));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }

            return count;
        }

        #endregion

        #region Page Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReadPageCached(long pageId, byte[] dest)
        {
            var span = dest.AsSpan(0, _pageSize);
            if (!_pageCache.TryGet(pageId, span))
            {
                _pageManager.ReadPage(pageId, span);
                _pageCache.Put(pageId, span);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (int Offset, int Length) ReadSlot(ReadOnlySpan<byte> page, int slotIndex)
        {
            int slotPos = HEADER_SIZE + slotIndex * SLOT_SIZE;
            ushort offset = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(slotPos));
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(slotPos + 2));
            return (offset, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (int Offset, int Length) ReadInternalSlot(ReadOnlySpan<byte> page, int slotIndex)
        {
            // Internal nodes: slot array starts at HEADER_SIZE + 8 (after leftmost child ptr)
            int slotPos = HEADER_SIZE + 8 + slotIndex * SLOT_SIZE;
            ushort offset = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(slotPos));
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(slotPos + 2));
            return (offset, length);
        }

        private int FindLeafInsertionPoint(ReadOnlySpan<byte> page, int itemCount, ReadOnlySpan<byte> key, out bool keyExists)
        {
            keyExists = false;
            int lo = 0, hi = itemCount - 1;
            int insertPoint = itemCount; // Default: append

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                var (slotOffset, slotLen) = ReadSlot(page, mid);
                var record = page.Slice(slotOffset, slotLen);
                int keyLen = BinaryPrimitives.ReadUInt16LittleEndian(record);
                var recordKey = record.Slice(2, keyLen);

                int cmp = recordKey.SequenceCompareTo(key);
                if (cmp == 0)
                {
                    keyExists = true;
                    return mid;
                }
                else if (cmp < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    insertPoint = mid;
                    hi = mid - 1;
                }
            }

            return insertPoint;
        }

        private int FindInternalInsertionPoint(ReadOnlySpan<byte> page, int itemCount, ReadOnlySpan<byte> key)
        {
            int lo = 0, hi = itemCount - 1;
            int insertPoint = itemCount;

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                var (slotOffset, slotLen) = ReadInternalSlot(page, mid);
                var record = page.Slice(slotOffset, slotLen);
                int keyLen = BinaryPrimitives.ReadUInt16LittleEndian(record);
                var recordKey = record.Slice(2, keyLen);

                int cmp = recordKey.SequenceCompareTo(key);
                if (cmp <= 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    insertPoint = mid;
                    hi = mid - 1;
                }
            }

            return insertPoint;
        }

        private void InsertLeafRecord(Span<byte> page, int itemCount, int insertIdx,
            int freeStart, int freeEnd, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, int recordSize)
        {
            // Write record at end of free space
            int recordOffset = freeEnd - recordSize;
            var record = page.Slice(recordOffset, recordSize);
            BinaryPrimitives.WriteUInt16LittleEndian(record, (ushort)key.Length);
            key.CopyTo(record.Slice(2));
            BinaryPrimitives.WriteInt32LittleEndian(record.Slice(2 + key.Length), value.Length);
            value.CopyTo(record.Slice(2 + key.Length + 4));

            // Shift slot array to make room at insertIdx
            int slotArrayStart = HEADER_SIZE;
            for (int i = itemCount; i > insertIdx; i--)
            {
                int srcOff = slotArrayStart + (i - 1) * SLOT_SIZE;
                int dstOff = slotArrayStart + i * SLOT_SIZE;
                page.Slice(srcOff, SLOT_SIZE).CopyTo(page.Slice(dstOff));
            }

            // Write new slot
            int newSlotPos = slotArrayStart + insertIdx * SLOT_SIZE;
            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(newSlotPos), (ushort)recordOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(newSlotPos + 2), (ushort)recordSize);
        }

        private void InsertInternalRecord(Span<byte> page, int itemCount, int insertIdx,
            int freeStart, int freeEnd, ReadOnlySpan<byte> key, long childPageId, int recordSize)
        {
            // Write record at end of free space
            int recordOffset = freeEnd - recordSize;
            var record = page.Slice(recordOffset, recordSize);
            BinaryPrimitives.WriteUInt16LittleEndian(record, (ushort)key.Length);
            key.CopyTo(record.Slice(2));
            BinaryPrimitives.WriteInt64LittleEndian(record.Slice(2 + key.Length), childPageId);

            // Shift slot array (internal slots start at HEADER_SIZE + 8)
            int slotArrayStart = HEADER_SIZE + 8;
            for (int i = itemCount; i > insertIdx; i--)
            {
                int srcOff = slotArrayStart + (i - 1) * SLOT_SIZE;
                int dstOff = slotArrayStart + i * SLOT_SIZE;
                page.Slice(srcOff, SLOT_SIZE).CopyTo(page.Slice(dstOff));
            }

            int newSlotPos = slotArrayStart + insertIdx * SLOT_SIZE;
            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(newSlotPos), (ushort)recordOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(newSlotPos + 2), (ushort)recordSize);
        }

        private InsertResult UpdateLeafRecord(long pageId, byte[] pageBuf, int slotIdx,
            ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WalManager wal)
        {
            // Simple approach: rewrite the page with the updated record.
            // A production implementation would do in-place update if size matches.
            var page = pageBuf.AsSpan(0, _pageSize);
            int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));

            // Collect all entries, replacing the one at slotIdx
            var entries = new List<(byte[] Key, byte[] Value)>(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                var (slotOffset, slotLen) = ReadSlot(page, i);
                var record = page.Slice(slotOffset, slotLen);
                int kLen = BinaryPrimitives.ReadUInt16LittleEndian(record);

                if (i == slotIdx)
                {
                    entries.Add((key.ToArray(), value.ToArray()));
                }
                else
                {
                    var rKey = record.Slice(2, kLen).ToArray();
                    int vLenOff = 2 + kLen;
                    int vLen = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(vLenOff));
                    var rVal = record.Slice(vLenOff + 4, vLen).ToArray();
                    entries.Add((rKey, rVal));
                }
            }

            // Rewrite the page
            long rightSibling = BinaryPrimitives.ReadInt64LittleEndian(page.Slice(HDR_RIGHT_SIBLING));
            RewriteLeafPage(pageBuf, entries, rightSibling);
            WritePageCrcAndFlush(pageId, pageBuf, wal);

            return new InsertResult { Split = false, Level = 0 };
        }

        private InsertResult SplitLeafAndInsert(long pageId, byte[] pageBuf, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WalManager wal)
        {
            var page = pageBuf.AsSpan(0, _pageSize);
            int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));
            long oldRightSibling = BinaryPrimitives.ReadInt64LittleEndian(page.Slice(HDR_RIGHT_SIBLING));

            // Collect all entries + new entry, sorted
            var entries = new List<(byte[] Key, byte[] Value)>(itemCount + 1);
            bool inserted = false;
            for (int i = 0; i < itemCount; i++)
            {
                var (slotOffset, slotLen) = ReadSlot(page, i);
                var record = page.Slice(slotOffset, slotLen);
                int kLen = BinaryPrimitives.ReadUInt16LittleEndian(record);
                var rKey = record.Slice(2, kLen);

                if (!inserted && key.SequenceCompareTo(rKey) <= 0)
                {
                    entries.Add((key.ToArray(), value.ToArray()));
                    inserted = true;
                }

                int vLenOff = 2 + kLen;
                int vLen = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(vLenOff));
                entries.Add((rKey.ToArray(), record.Slice(vLenOff + 4, vLen).ToArray()));
            }
            if (!inserted)
                entries.Add((key.ToArray(), value.ToArray()));

            // Split: left half stays in pageId, right half goes to new page
            int splitPoint = entries.Count / 2;
            var leftEntries = entries.GetRange(0, splitPoint);
            var rightEntries = entries.GetRange(splitPoint, entries.Count - splitPoint);

            // Allocate new right page
            long newPageId = _pageManager.AllocatePage();

            // Rewrite left page (right sibling -> newPageId)
            RewriteLeafPage(pageBuf, leftEntries, newPageId);
            WritePageCrcAndFlush(pageId, pageBuf, wal);

            // Write right page (right sibling -> old right sibling)
            var rightBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                Array.Clear(rightBuf, 0, _pageSize);
                RewriteLeafPage(rightBuf, rightEntries, oldRightSibling);
                WritePageCrcAndFlush(newPageId, rightBuf, wal);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rightBuf);
            }

            // Split key = first key of right page
            return new InsertResult
            {
                Split = true,
                SplitKey = rightEntries[0].Key,
                NewPageId = newPageId,
                Level = 0
            };
        }

        private InsertResult SplitInternalAndInsert(long pageId, byte[] pageBuf, byte[] separatorKey, long newChildPageId, int level, WalManager wal)
        {
            var page = pageBuf.AsSpan(0, _pageSize);
            int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));
            long leftmostChild = BinaryPrimitives.ReadInt64LittleEndian(page.Slice(HEADER_SIZE));

            // Collect all separators + children, then insert the new one
            var entries = new List<(byte[] Key, long ChildPageId)>(itemCount + 1);
            bool inserted = false;

            for (int i = 0; i < itemCount; i++)
            {
                var (slotOffset, slotLen) = ReadInternalSlot(page, i);
                var record = page.Slice(slotOffset, slotLen);
                int kLen = BinaryPrimitives.ReadUInt16LittleEndian(record);
                var rKey = record.Slice(2, kLen);
                long childId = BinaryPrimitives.ReadInt64LittleEndian(record.Slice(2 + kLen));

                if (!inserted && separatorKey.AsSpan().SequenceCompareTo(rKey) <= 0)
                {
                    entries.Add((separatorKey, newChildPageId));
                    inserted = true;
                }

                entries.Add((rKey.ToArray(), childId));
            }
            if (!inserted)
                entries.Add((separatorKey, newChildPageId));

            // Split: median goes up, left and right halves become separate internal nodes
            int medianIdx = entries.Count / 2;
            var medianKey = entries[medianIdx].Key;
            var leftEntries = entries.GetRange(0, medianIdx);
            var rightEntries = entries.GetRange(medianIdx + 1, entries.Count - medianIdx - 1);

            // Left internal node: leftmostChild stays the same
            long rightLeftmostChild = entries[medianIdx].ChildPageId;

            // Allocate new right page
            long newPageId = _pageManager.AllocatePage();

            // Rewrite left page
            RewriteInternalPage(pageBuf, leftEntries, leftmostChild, level);
            WritePageCrcAndFlush(pageId, pageBuf, wal);

            // Write right page
            var rightBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                Array.Clear(rightBuf, 0, _pageSize);
                RewriteInternalPage(rightBuf, rightEntries, rightLeftmostChild, level);
                WritePageCrcAndFlush(newPageId, rightBuf, wal);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rightBuf);
            }

            return new InsertResult
            {
                Split = true,
                SplitKey = medianKey,
                NewPageId = newPageId,
                Level = level
            };
        }

        #endregion

        #region Page Construction Helpers

        private long CreateInitialLeaf(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WalManager wal)
        {
            long pageId = _pageManager.AllocatePage();
            var pageBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                Array.Clear(pageBuf, 0, _pageSize);
                var entries = new List<(byte[] Key, byte[] Value)> { (key.ToArray(), value.ToArray()) };
                RewriteLeafPage(pageBuf, entries, rightSibling: 0);
                WritePageCrcAndFlush(pageId, pageBuf, wal);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
            return pageId;
        }

        private long CreateNewRoot(long leftChildId, byte[] separatorKey, long rightChildId, int level, WalManager wal)
        {
            long rootPageId = _pageManager.AllocatePage();
            var pageBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                Array.Clear(pageBuf, 0, _pageSize);
                var entries = new List<(byte[] Key, long ChildPageId)> { (separatorKey, rightChildId) };
                RewriteInternalPage(pageBuf, entries, leftChildId, level);
                WritePageCrcAndFlush(rootPageId, pageBuf, wal);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
            return rootPageId;
        }

        private void RewriteLeafPage(Span<byte> pageBuf, List<(byte[] Key, byte[] Value)> entries, long rightSibling)
        {
            var page = pageBuf.Slice(0, _pageSize);
            page.Clear();

            // Header
            page[HDR_PAGE_TYPE] = PAGE_TYPE_LEAF;
            page[HDR_LEVEL] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT), (ushort)entries.Count);
            BinaryPrimitives.WriteInt64LittleEndian(page.Slice(HDR_RIGHT_SIBLING), rightSibling);

            // Build records from the end, slots from HEADER_SIZE
            int freeStart = HEADER_SIZE + entries.Count * SLOT_SIZE;
            int freeEnd = _pageSize;

            for (int i = 0; i < entries.Count; i++)
            {
                var (k, v) = entries[i];
                int recordSize = 2 + k.Length + 4 + v.Length;
                freeEnd -= recordSize;

                // Write record
                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(freeEnd), (ushort)k.Length);
                k.AsSpan().CopyTo(page.Slice(freeEnd + 2));
                BinaryPrimitives.WriteInt32LittleEndian(page.Slice(freeEnd + 2 + k.Length), v.Length);
                v.AsSpan().CopyTo(page.Slice(freeEnd + 2 + k.Length + 4));

                // Write slot
                int slotPos = HEADER_SIZE + i * SLOT_SIZE;
                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(slotPos), (ushort)freeEnd);
                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(slotPos + 2), (ushort)recordSize);
            }

            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_START), (ushort)freeStart);
            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_END), (ushort)freeEnd);
        }

        private void RewriteInternalPage(Span<byte> pageBuf, List<(byte[] Key, long ChildPageId)> entries, long leftmostChild, int level)
        {
            var page = pageBuf.Slice(0, _pageSize);
            page.Clear();

            // Header
            page[HDR_PAGE_TYPE] = PAGE_TYPE_INTERNAL;
            page[HDR_LEVEL] = (byte)level;
            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT), (ushort)entries.Count);

            // Leftmost child pointer
            BinaryPrimitives.WriteInt64LittleEndian(page.Slice(HEADER_SIZE), leftmostChild);

            // Slots start at HEADER_SIZE + 8
            int slotArrayStart = HEADER_SIZE + 8;
            int freeStart = slotArrayStart + entries.Count * SLOT_SIZE;
            int freeEnd = _pageSize;

            for (int i = 0; i < entries.Count; i++)
            {
                var (k, childId) = entries[i];
                int recordSize = 2 + k.Length + 8;
                freeEnd -= recordSize;

                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(freeEnd), (ushort)k.Length);
                k.AsSpan().CopyTo(page.Slice(freeEnd + 2));
                BinaryPrimitives.WriteInt64LittleEndian(page.Slice(freeEnd + 2 + k.Length), childId);

                int slotPos = slotArrayStart + i * SLOT_SIZE;
                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(slotPos), (ushort)freeEnd);
                BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(slotPos + 2), (ushort)recordSize);
            }

            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_START), (ushort)freeStart);
            BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_END), (ushort)freeEnd);
        }

        private void WritePageCrcAndFlush(long pageId, byte[] pageBuf, WalManager wal)
        {
            var page = pageBuf.AsSpan(0, _pageSize);

            // Compute CRC over the page excluding the 4-byte CRC field at HDR_PAGE_CRC
            uint crc = Crc32.ComputeExcluding(page, HDR_PAGE_CRC, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(page.Slice(HDR_PAGE_CRC), crc);

            // Write to WAL first, then to data file
            wal.WritePageImage(pageId, page);
            _pageManager.WritePage(pageId, page);
            _pageCache.Invalidate(pageId);
        }

        private long FindLeftmostLeaf(long pageId)
        {
            var pageBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                while (true)
                {
                    ReadPageCached(pageId, pageBuf);
                    var page = pageBuf.AsSpan(0, _pageSize);

                    if (page[HDR_PAGE_TYPE] == PAGE_TYPE_LEAF)
                        return pageId;

                    // Follow leftmost child
                    pageId = BinaryPrimitives.ReadInt64LittleEndian(page.Slice(HEADER_SIZE));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
        }

        private long FindLeafForKey(long pageId, ReadOnlySpan<byte> key)
        {
            var pageBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                while (true)
                {
                    ReadPageCached(pageId, pageBuf);
                    var page = pageBuf.AsSpan(0, _pageSize);

                    if (page[HDR_PAGE_TYPE] == PAGE_TYPE_LEAF)
                        return pageId;

                    int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));
                    pageId = SearchInternal(page, itemCount, key);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
        }

        #endregion

        private struct InsertResult
        {
            public bool Split;
            public byte[]? SplitKey;
            public long NewPageId;
            public int Level;
        }
    }
}
