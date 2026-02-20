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
        /// (may change if the root splits) and whether a new key was inserted (vs update).
        /// </summary>
        internal (long NewRootPageId, bool IsNewKey) Insert(long rootPageId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, WalManager wal)
        {
            if (rootPageId == 0)
            {
                // Empty tree: create first leaf
                return (CreateInitialLeaf(key, value, wal), true);
            }

            var result = InsertRecursive(rootPageId, key, value, wal);

            if (result.Split)
            {
                // Root split: create new root
                return (CreateNewRoot(rootPageId, result.SplitKey!, result.NewPageId, result.Level + 1, wal), result.IsNewKey);
            }

            return (rootPageId, result.IsNewKey);
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
                        var internalResult = InsertIntoInternal(pageId, pageBuf, childResult.SplitKey!, childResult.NewPageId, level, wal);
                        internalResult.IsNewKey = childResult.IsNewKey;
                        return internalResult;
                    }

                    return new InsertResult { Split = false, Level = level, IsNewKey = childResult.IsNewKey };
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
                return new InsertResult { Split = false, Level = 0, IsNewKey = true };
            }
            else
            {
                // Split leaf (always a new key — keyExists was false)
                var splitResult = SplitLeafAndInsert(pageId, pageBuf, key, value, wal);
                splitResult.IsNewKey = true;
                return splitResult;
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
        ///
        /// Strategy:
        ///   - Delete from leaf with page compaction (no fragmentation).
        ///   - Merge underfull leaf with sibling when combined contents fit in one page.
        ///   - Redistribute (borrow from sibling) when merge would overflow.
        ///   - Update parent separator keys after merge/redistribution.
        ///   - Remove empty leaves from the leaf chain.
        ///   - Handle cascading internal node merges when a merge reduces parent below minimum.
        ///   - Root shrinking: if the root internal node has 0 separators, collapse to its sole child.
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

                if (pageType == PAGE_TYPE_LEAF)
                {
                    bool found = DeleteFromLeaf(rootPageId, pageBuf, key, wal);
                    int newCount = BinaryPrimitives.ReadUInt16LittleEndian(pageBuf.AsSpan(HDR_ITEM_COUNT));
                    long newRoot = newCount == 0 ? 0 : rootPageId;
                    return (newRoot, found);
                }
                else
                {
                    var result = DeleteRecursive(rootPageId, key, wal);
                    if (!result.Found)
                        return (rootPageId, false);

                    // Root shrinking: if root internal node now has 0 separators,
                    // collapse to its sole child (the leftmost child).
                    return (TryShrinkRoot(rootPageId), true);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
        }

        private DeleteResult DeleteRecursive(long pageId, ReadOnlySpan<byte> key, WalManager wal)
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
                    if (!found)
                        return new DeleteResult { Found = false };

                    // Re-read to get updated item count after compaction
                    ReadPageCached(pageId, pageBuf);
                    page = pageBuf.AsSpan(0, _pageSize);
                    int newItemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));
                    return new DeleteResult { Found = true, Underfull = IsLeafUnderfull(page, newItemCount) };
                }

                // Internal node: find child index and descend
                long leftmostChild = BinaryPrimitives.ReadInt64LittleEndian(page.Slice(HEADER_SIZE));
                int childIdx = FindChildIndex(page, itemCount, key);
                long childPageId = GetChildPageId(page, itemCount, childIdx, leftmostChild);

                var childResult = DeleteRecursive(childPageId, key, wal);
                if (!childResult.Found)
                    return childResult;

                if (!childResult.Underfull)
                    return new DeleteResult { Found = true, Underfull = false };

                // Child is underfull — try merge or redistribute with a sibling
                TryRebalanceChild(pageId, childPageId, childIdx, wal);

                // Re-read parent to check if it became underfull after rebalancing
                ReadPageCached(pageId, pageBuf);
                page = pageBuf.AsSpan(0, _pageSize);
                int parentItemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));
                return new DeleteResult { Found = true, Underfull = IsInternalUnderfull(page, parentItemCount) };
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
        }

        /// <summary>
        /// Find the child index (0-based) that the key routes to.
        /// Index 0 = leftmost child, index N = child after separator N-1.
        /// </summary>
        private int FindChildIndex(ReadOnlySpan<byte> page, int itemCount, ReadOnlySpan<byte> key)
        {
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

            return insertionPoint;
        }

        /// <summary>
        /// Get the child page ID for a given child index in an internal node.
        /// childIdx=0 returns leftmostChild; childIdx=N returns the pointer from separator N-1.
        /// </summary>
        private long GetChildPageId(ReadOnlySpan<byte> page, int itemCount, int childIdx, long leftmostChild)
        {
            if (childIdx == 0)
                return leftmostChild;

            var (slotOffset, slotLen) = ReadInternalSlot(page, childIdx - 1);
            var record = page.Slice(slotOffset, slotLen);
            int keyLen = BinaryPrimitives.ReadUInt16LittleEndian(record);
            return BinaryPrimitives.ReadInt64LittleEndian(record.Slice(2 + keyLen));
        }

        /// <summary>
        /// Check if a leaf page is underfull (used space &lt; 40% of usable space).
        /// A leaf with 0 entries is always underfull.
        /// </summary>
        private bool IsLeafUnderfull(ReadOnlySpan<byte> page, int itemCount)
        {
            if (itemCount == 0) return true;
            int usableSpace = _pageSize - HEADER_SIZE;
            int freeStart = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_START));
            int freeEnd = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_END));
            int usedSpace = (freeStart - HEADER_SIZE) + (_pageSize - freeEnd);
            // Underfull threshold: 40% of usable space
            return usedSpace * 5 < usableSpace * 2;
        }

        /// <summary>
        /// Check if an internal page is underfull (used space &lt; 40% of usable space).
        /// Internal nodes with 0 separators handled separately by root shrinking.
        /// </summary>
        private bool IsInternalUnderfull(ReadOnlySpan<byte> page, int itemCount)
        {
            if (itemCount == 0) return true;
            int usableSpace = _pageSize - HEADER_SIZE - 8; // subtract leftmost child pointer
            int slotArrayStart = HEADER_SIZE + 8;
            int freeStart = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_START));
            int freeEnd = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_FREE_SPACE_END));
            int usedSpace = (freeStart - slotArrayStart) + (_pageSize - freeEnd);
            return usedSpace * 5 < usableSpace * 2;
        }

        /// <summary>
        /// Try to rebalance an underfull child by merging with or redistributing from a sibling.
        /// Prefers the right sibling if available, otherwise uses the left sibling.
        /// Merge is preferred over redistribution when entries fit in one page.
        /// </summary>
        private void TryRebalanceChild(long parentPageId, long childPageId, int childIdx, WalManager wal)
        {
            var parentBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            var childBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            var siblingBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                ReadPageCached(parentPageId, parentBuf);
                var parentPage = parentBuf.AsSpan(0, _pageSize);
                int parentItemCount = BinaryPrimitives.ReadUInt16LittleEndian(parentPage.Slice(HDR_ITEM_COUNT));
                long leftmostChild = BinaryPrimitives.ReadInt64LittleEndian(parentPage.Slice(HEADER_SIZE));

                // Determine sibling: prefer right, fall back to left
                int separatorIdx; // Index of the separator between child and sibling
                long siblingPageId;
                bool siblingIsRight;

                if (childIdx < parentItemCount)
                {
                    // Right sibling exists (separator at childIdx)
                    separatorIdx = childIdx;
                    siblingPageId = GetChildPageId(parentPage, parentItemCount, childIdx + 1, leftmostChild);
                    siblingIsRight = true;
                }
                else if (childIdx > 0)
                {
                    // Left sibling exists (separator at childIdx - 1)
                    separatorIdx = childIdx - 1;
                    siblingPageId = GetChildPageId(parentPage, parentItemCount, childIdx - 1, leftmostChild);
                    siblingIsRight = false;
                }
                else
                {
                    // Only child — nothing to merge/redistribute with
                    return;
                }

                ReadPageCached(childPageId, childBuf);
                ReadPageCached(siblingPageId, siblingBuf);

                byte childType = childBuf[HDR_PAGE_TYPE];

                if (childType == PAGE_TYPE_LEAF)
                {
                    TryRebalanceLeaves(parentPageId, parentBuf, childPageId, childBuf,
                        siblingPageId, siblingBuf, separatorIdx, siblingIsRight, wal);
                }
                else
                {
                    TryRebalanceInternals(parentPageId, parentBuf, childPageId, childBuf,
                        siblingPageId, siblingBuf, separatorIdx, siblingIsRight, wal);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(siblingBuf);
                ArrayPool<byte>.Shared.Return(childBuf);
                ArrayPool<byte>.Shared.Return(parentBuf);
            }
        }

        /// <summary>
        /// Merge or redistribute two leaf siblings.
        /// If all entries fit in one page, merge (left absorbs right, remove right from chain).
        /// Otherwise, redistribute entries evenly and update parent separator.
        /// </summary>
        private void TryRebalanceLeaves(long parentPageId, byte[] parentBuf,
            long childPageId, byte[] childBuf, long siblingPageId, byte[] siblingBuf,
            int separatorIdx, bool siblingIsRight, WalManager wal)
        {
            // Determine left and right leaf
            long leftPageId, rightPageId;
            byte[] leftBuf, rightBuf;
            if (siblingIsRight)
            {
                leftPageId = childPageId; leftBuf = childBuf;
                rightPageId = siblingPageId; rightBuf = siblingBuf;
            }
            else
            {
                leftPageId = siblingPageId; leftBuf = siblingBuf;
                rightPageId = childPageId; rightBuf = childBuf;
            }

            // Collect all entries from both leaves (already sorted)
            var allEntries = CollectLeafEntries(leftBuf);
            allEntries.AddRange(CollectLeafEntries(rightBuf));

            // Right sibling of the right leaf (to maintain chain after merge)
            long rightOfRight = BinaryPrimitives.ReadInt64LittleEndian(
                rightBuf.AsSpan(HDR_RIGHT_SIBLING, 8));

            // Check if merge fits in one page
            if (LeafEntriesFitInPage(allEntries))
            {
                // === MERGE: left absorbs all entries, right is removed ===

                // Also need to update the predecessor of leftLeaf if left's rightSibling
                // currently points to right (it should, since they're adjacent).
                // Left leaf's right sibling becomes right leaf's right sibling.
                RewriteLeafPage(leftBuf, allEntries, rightOfRight);
                WritePageCrcAndFlush(leftPageId, leftBuf, wal);

                // Remove separator from parent, collapsing the right child
                RemoveSeparatorFromParent(parentPageId, parentBuf, separatorIdx,
                    leftPageId, siblingIsRight, wal);
            }
            else
            {
                // === REDISTRIBUTE: split entries evenly between left and right ===
                int splitPoint = allEntries.Count / 2;
                var leftEntries = allEntries.GetRange(0, splitPoint);
                var rightEntries = allEntries.GetRange(splitPoint, allEntries.Count - splitPoint);

                // Rewrite both leaves
                RewriteLeafPage(leftBuf, leftEntries, rightPageId);
                WritePageCrcAndFlush(leftPageId, leftBuf, wal);

                RewriteLeafPage(rightBuf, rightEntries, rightOfRight);
                WritePageCrcAndFlush(rightPageId, rightBuf, wal);

                // Update parent separator to the first key of the new right leaf
                UpdateParentSeparator(parentPageId, parentBuf, separatorIdx, rightEntries[0].Key, wal);
            }
        }

        /// <summary>
        /// Merge or redistribute two internal node siblings.
        /// Merge pulls the parent separator down into the combined node.
        /// Redistribute moves entries via the parent separator (rotate).
        /// </summary>
        private void TryRebalanceInternals(long parentPageId, byte[] parentBuf,
            long childPageId, byte[] childBuf, long siblingPageId, byte[] siblingBuf,
            int separatorIdx, bool siblingIsRight, WalManager wal)
        {
            long leftPageId, rightPageId;
            byte[] leftBuf, rightBuf;
            if (siblingIsRight)
            {
                leftPageId = childPageId; leftBuf = childBuf;
                rightPageId = siblingPageId; rightBuf = siblingBuf;
            }
            else
            {
                leftPageId = siblingPageId; leftBuf = siblingBuf;
                rightPageId = childPageId; rightBuf = childBuf;
            }

            // Read parent separator key
            var parentPage = parentBuf.AsSpan(0, _pageSize);
            var (sepOff, sepLen) = ReadInternalSlot(parentPage, separatorIdx);
            var sepRecord = parentPage.Slice(sepOff, sepLen);
            int sepKeyLen = BinaryPrimitives.ReadUInt16LittleEndian(sepRecord);
            byte[] parentSepKey = sepRecord.Slice(2, sepKeyLen).ToArray();

            // Collect entries from left internal node
            var leftPage = leftBuf.AsSpan(0, _pageSize);
            int leftItemCount = BinaryPrimitives.ReadUInt16LittleEndian(leftPage.Slice(HDR_ITEM_COUNT));
            long leftLeftmostChild = BinaryPrimitives.ReadInt64LittleEndian(leftPage.Slice(HEADER_SIZE));
            int leftLevel = leftPage[HDR_LEVEL];

            var allEntries = new List<(byte[] Key, long ChildPageId)>();

            // Left's separators
            for (int i = 0; i < leftItemCount; i++)
            {
                var (sOff, sLen) = ReadInternalSlot(leftPage, i);
                var rec = leftPage.Slice(sOff, sLen);
                int kLen = BinaryPrimitives.ReadUInt16LittleEndian(rec);
                allEntries.Add((rec.Slice(2, kLen).ToArray(),
                    BinaryPrimitives.ReadInt64LittleEndian(rec.Slice(2 + kLen))));
            }

            // Parent separator key + right's leftmost child becomes the bridge entry
            var rightPage = rightBuf.AsSpan(0, _pageSize);
            long rightLeftmostChild = BinaryPrimitives.ReadInt64LittleEndian(rightPage.Slice(HEADER_SIZE));
            allEntries.Add((parentSepKey, rightLeftmostChild));

            // Right's separators
            int rightItemCount = BinaryPrimitives.ReadUInt16LittleEndian(rightPage.Slice(HDR_ITEM_COUNT));
            for (int i = 0; i < rightItemCount; i++)
            {
                var (sOff, sLen) = ReadInternalSlot(rightPage, i);
                var rec = rightPage.Slice(sOff, sLen);
                int kLen = BinaryPrimitives.ReadUInt16LittleEndian(rec);
                allEntries.Add((rec.Slice(2, kLen).ToArray(),
                    BinaryPrimitives.ReadInt64LittleEndian(rec.Slice(2 + kLen))));
            }

            if (InternalEntriesFitInPage(allEntries))
            {
                // === MERGE: left absorbs all entries ===
                RewriteInternalPage(leftBuf, allEntries, leftLeftmostChild, leftLevel);
                WritePageCrcAndFlush(leftPageId, leftBuf, wal);

                RemoveSeparatorFromParent(parentPageId, parentBuf, separatorIdx,
                    leftPageId, siblingIsRight, wal);
            }
            else
            {
                // === REDISTRIBUTE: split at median, median goes up to parent ===
                int medianIdx = allEntries.Count / 2;
                var newSepKey = allEntries[medianIdx].Key;
                long newRightLeftmost = allEntries[medianIdx].ChildPageId;

                var leftEntries = allEntries.GetRange(0, medianIdx);
                var rightEntries = allEntries.GetRange(medianIdx + 1, allEntries.Count - medianIdx - 1);

                RewriteInternalPage(leftBuf, leftEntries, leftLeftmostChild, leftLevel);
                WritePageCrcAndFlush(leftPageId, leftBuf, wal);

                RewriteInternalPage(rightBuf, rightEntries, newRightLeftmost, leftLevel);
                WritePageCrcAndFlush(rightPageId, rightBuf, wal);

                UpdateParentSeparator(parentPageId, parentBuf, separatorIdx, newSepKey, wal);
            }
        }

        /// <summary>
        /// Collect all key-value entries from a leaf page buffer.
        /// </summary>
        private List<(byte[] Key, byte[] Value)> CollectLeafEntries(byte[] pageBuf)
        {
            var page = pageBuf.AsSpan(0, _pageSize);
            int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));
            var entries = new List<(byte[] Key, byte[] Value)>(itemCount);

            for (int i = 0; i < itemCount; i++)
            {
                var (slotOffset, slotLen) = ReadSlot(page, i);
                var record = page.Slice(slotOffset, slotLen);
                int kLen = BinaryPrimitives.ReadUInt16LittleEndian(record);
                var rKey = record.Slice(2, kLen).ToArray();
                int vLenOff = 2 + kLen;
                int vLen = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(vLenOff));
                var rVal = record.Slice(vLenOff + 4, vLen).ToArray();
                entries.Add((rKey, rVal));
            }

            return entries;
        }

        /// <summary>
        /// Check if a list of leaf entries fits in a single page.
        /// Accounts for header, slot array, and record sizes.
        /// </summary>
        private bool LeafEntriesFitInPage(List<(byte[] Key, byte[] Value)> entries)
        {
            int slotsSize = entries.Count * SLOT_SIZE;
            int recordsSize = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                recordsSize += 2 + entries[i].Key.Length + 4 + entries[i].Value.Length;
            }
            return HEADER_SIZE + slotsSize + recordsSize <= _pageSize;
        }

        /// <summary>
        /// Check if a list of internal entries fits in a single page.
        /// Accounts for header, leftmost child pointer, slot array, and record sizes.
        /// </summary>
        private bool InternalEntriesFitInPage(List<(byte[] Key, long ChildPageId)> entries)
        {
            int overhead = HEADER_SIZE + 8; // header + leftmost child ptr
            int slotsSize = entries.Count * SLOT_SIZE;
            int recordsSize = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                recordsSize += 2 + entries[i].Key.Length + 8;
            }
            return overhead + slotsSize + recordsSize <= _pageSize;
        }

        /// <summary>
        /// Remove a separator (and its associated child) from a parent internal node
        /// after a merge. The surviving child (left) replaces the merged pair.
        /// </summary>
        private void RemoveSeparatorFromParent(long parentPageId, byte[] parentBuf,
            int separatorIdx, long survivingChildPageId, bool siblingIsRight, WalManager wal)
        {
            var parentPage = parentBuf.AsSpan(0, _pageSize);
            int parentItemCount = BinaryPrimitives.ReadUInt16LittleEndian(parentPage.Slice(HDR_ITEM_COUNT));
            long leftmostChild = BinaryPrimitives.ReadInt64LittleEndian(parentPage.Slice(HEADER_SIZE));
            int level = parentPage[HDR_LEVEL];

            // Collect all separators except the one being removed
            var remaining = new List<(byte[] Key, long ChildPageId)>(parentItemCount - 1);

            for (int i = 0; i < parentItemCount; i++)
            {
                if (i == separatorIdx)
                    continue;

                var (sOff, sLen) = ReadInternalSlot(parentPage, i);
                var rec = parentPage.Slice(sOff, sLen);
                int kLen = BinaryPrimitives.ReadUInt16LittleEndian(rec);
                var key = rec.Slice(2, kLen).ToArray();
                long child = BinaryPrimitives.ReadInt64LittleEndian(rec.Slice(2 + kLen));
                remaining.Add((key, child));
            }

            // After merge, the surviving child (left) absorbs the right.
            // If the removed separator's *left* side was the leftmost child,
            // update leftmostChild to the surviving page.
            long newLeftmostChild;
            if (separatorIdx == 0 && !siblingIsRight)
            {
                // Left sibling was the leftmost child, child was at separator 0's pointer.
                // Surviving = left sibling = old leftmost child. Keep it.
                newLeftmostChild = survivingChildPageId;
            }
            else if (separatorIdx == 0 && siblingIsRight)
            {
                // Child was leftmost child (childIdx=0), sibling was at separator 0's pointer.
                // After merge, surviving = child = leftmost. Keep it.
                newLeftmostChild = survivingChildPageId;
            }
            else
            {
                // Separator is not at position 0. The leftmost child is unchanged.
                // But we need to ensure the surviving child's pointer replaces
                // the right side of the removed separator.
                newLeftmostChild = leftmostChild;

                // The removed separator's child pointer (right side of sep) pointed to the right child.
                // The left child (surviving) was referenced by separator[separatorIdx-1]'s child pointer
                // (or leftmost child if separatorIdx=1 and child was at index 0+1 =1).
                // After removing the separator, the remaining separator that was just before
                // the removed one should now point to the surviving child.
                if (siblingIsRight)
                {
                    // Child = left of separator, sibling = right of separator.
                    // Child survives. The pointer to child is at separator[separatorIdx-1].child
                    // or leftmostChild if separatorIdx was referencing childIdx>0.
                    // After removing separator, the entries shift and the pointer that
                    // previously went to the removed right child is gone. The pointer
                    // that was to the left of the separator still correctly points to child.
                    // No further adjustment needed.
                }
                else
                {
                    // Sibling = left, child = right of separator.
                    // Sibling (left) survives. We need to update the pointer that
                    // previously pointed to the right child to now point to the surviving left child.
                    // The separator at separatorIdx had child pointer pointing to right (= child).
                    // The separator before it (separatorIdx-1) has child pointer pointing to left (= sibling).
                    // After removing separator[separatorIdx], the pointer from separator[separatorIdx-1]
                    // (which is now at the same logical position) still points to sibling. Correct.
                    // BUT: the separator after the removed one (if any) had its LEFT defined by
                    // the removed separator's child pointer (the right child). That reference is gone.
                    // We need to repoint it to the surviving child.
                    // Actually, in the remaining list, the entry that was at separatorIdx+1 is now
                    // at position separatorIdx. Its child pointer is its RIGHT child. The LEFT child
                    // of that separator is defined by the previous separator's child pointer or leftmostChild.
                    // Since we removed separator[separatorIdx] and the entry at [separatorIdx-1] still
                    // points to the sibling (surviving), the [separatorIdx] entry (previously [separatorIdx+1])
                    // correctly has its left child as the sibling. This is correct.
                }
            }

            RewriteInternalPage(parentBuf, remaining, newLeftmostChild, level);
            WritePageCrcAndFlush(parentPageId, parentBuf, wal);
        }

        /// <summary>
        /// Update a separator key in a parent internal node (after redistribution).
        /// Rewrites the parent page with the new separator key at the given index.
        /// </summary>
        private void UpdateParentSeparator(long parentPageId, byte[] parentBuf,
            int separatorIdx, byte[] newSepKey, WalManager wal)
        {
            var parentPage = parentBuf.AsSpan(0, _pageSize);
            int parentItemCount = BinaryPrimitives.ReadUInt16LittleEndian(parentPage.Slice(HDR_ITEM_COUNT));
            long leftmostChild = BinaryPrimitives.ReadInt64LittleEndian(parentPage.Slice(HEADER_SIZE));
            int level = parentPage[HDR_LEVEL];

            var entries = new List<(byte[] Key, long ChildPageId)>(parentItemCount);
            for (int i = 0; i < parentItemCount; i++)
            {
                var (sOff, sLen) = ReadInternalSlot(parentPage, i);
                var rec = parentPage.Slice(sOff, sLen);
                int kLen = BinaryPrimitives.ReadUInt16LittleEndian(rec);
                long child = BinaryPrimitives.ReadInt64LittleEndian(rec.Slice(2 + kLen));

                if (i == separatorIdx)
                    entries.Add((newSepKey, child));
                else
                    entries.Add((rec.Slice(2, kLen).ToArray(), child));
            }

            RewriteInternalPage(parentBuf, entries, leftmostChild, level);
            WritePageCrcAndFlush(parentPageId, parentBuf, wal);
        }

        /// <summary>
        /// Delete a key from a leaf page by rewriting the page without the deleted entry.
        /// This reclaims all fragmented record space (full compaction).
        /// </summary>
        private bool DeleteFromLeaf(long pageId, byte[] pageBuf, ReadOnlySpan<byte> key, WalManager wal)
        {
            var page = pageBuf.AsSpan(0, _pageSize);
            int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));

            int deleteIdx = FindLeafInsertionPoint(page, itemCount, key, out bool keyExists);
            if (!keyExists)
                return false;

            // Collect all entries except the one being deleted
            long rightSibling = BinaryPrimitives.ReadInt64LittleEndian(page.Slice(HDR_RIGHT_SIBLING));
            var remaining = new List<(byte[] Key, byte[] Value)>(itemCount - 1);

            for (int i = 0; i < itemCount; i++)
            {
                if (i == deleteIdx)
                    continue;

                var (slotOffset, slotLen) = ReadSlot(page, i);
                var record = page.Slice(slotOffset, slotLen);
                int kLen = BinaryPrimitives.ReadUInt16LittleEndian(record);
                var rKey = record.Slice(2, kLen).ToArray();
                int vLenOff = 2 + kLen;
                int vLen = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(vLenOff));
                var rVal = record.Slice(vLenOff + 4, vLen).ToArray();
                remaining.Add((rKey, rVal));
            }

            // Rewrite the page: compacts all records, reclaims dead space
            RewriteLeafPage(pageBuf, remaining, rightSibling);
            WritePageCrcAndFlush(pageId, pageBuf, wal);
            return true;
        }

        /// <summary>
        /// If the root is an internal node with 0 separators (single child),
        /// collapse it to the leftmost child. Repeats until the root is a leaf
        /// or has at least one separator.
        /// </summary>
        private long TryShrinkRoot(long rootPageId)
        {
            var pageBuf = ArrayPool<byte>.Shared.Rent(_pageSize);
            try
            {
                long currentRoot = rootPageId;

                while (currentRoot != 0)
                {
                    ReadPageCached(currentRoot, pageBuf);
                    var page = pageBuf.AsSpan(0, _pageSize);

                    if (page[HDR_PAGE_TYPE] != PAGE_TYPE_INTERNAL)
                        break; // Leaf root — nothing to shrink

                    int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(HDR_ITEM_COUNT));
                    if (itemCount > 0)
                        break; // Root has separators — no shrinking needed

                    // Root internal with 0 separators: collapse to leftmost child
                    currentRoot = BinaryPrimitives.ReadInt64LittleEndian(page.Slice(HEADER_SIZE));
                }

                return currentRoot;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pageBuf);
            }
        }

        private struct DeleteResult
        {
            public bool Found;
            public bool Underfull;
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
            public bool IsNewKey;
        }
    }
}
