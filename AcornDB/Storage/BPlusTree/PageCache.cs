using System;
using System.Buffers;
using System.Threading;

namespace AcornDB.Storage.BPlusTree
{
    /// <summary>
    /// Bounded page cache using clock (second-chance) eviction.
    ///
    /// Thread-safe for concurrent reads. Writers invalidate entries after modifying pages.
    /// Cache entries are backed by pooled byte arrays returned to ArrayPool on eviction.
    ///
    /// Design:
    ///   - Fixed-size circular buffer of CacheEntry slots
    ///   - Each slot: pageId, data (byte[]), referenced bit, pinCount
    ///   - Lookup: O(1) via ConcurrentDictionary pageId -> slot index
    ///   - Eviction: clock hand sweeps; skips referenced/pinned entries
    /// </summary>
    internal sealed class PageCache : IDisposable
    {
        private readonly int _maxPages;
        private readonly int _pageSize;
        private readonly CacheEntry[] _entries;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _index;
        private int _clockHand;
        private int _count;
        private readonly object _evictLock = new();

        internal PageCache(int maxPages, int pageSize)
        {
            _maxPages = maxPages;
            _pageSize = pageSize;
            _entries = new CacheEntry[maxPages];
            _index = new(concurrencyLevel: Environment.ProcessorCount, capacity: maxPages);
        }

        /// <summary>
        /// Try to get a cached page. Returns false if not cached.
        /// If found, copies page data into dest and marks as referenced.
        /// </summary>
        internal bool TryGet(long pageId, Span<byte> dest)
        {
            if (!_index.TryGetValue(pageId, out int slot))
                return false;

            ref var entry = ref _entries[slot];
            if (entry.PageId != pageId || entry.Data == null)
            {
                // Stale index entry
                _index.TryRemove(pageId, out _);
                return false;
            }

            entry.Data.AsSpan(0, _pageSize).CopyTo(dest);
            Volatile.Write(ref entry.Referenced, 1);
            return true;
        }

        /// <summary>
        /// Insert or update a page in the cache. Evicts if full.
        /// </summary>
        internal void Put(long pageId, ReadOnlySpan<byte> data)
        {
            if (_index.TryGetValue(pageId, out int existingSlot))
            {
                // Update in-place
                ref var existing = ref _entries[existingSlot];
                if (existing.Data != null)
                {
                    data.Slice(0, _pageSize).CopyTo(existing.Data.AsSpan());
                    Volatile.Write(ref existing.Referenced, 1);
                    return;
                }
            }

            int slot;
            if (_count < _maxPages)
            {
                slot = Interlocked.Increment(ref _count) - 1;
                if (slot >= _maxPages)
                {
                    // Race: another thread took the last slot
                    slot = Evict();
                }
            }
            else
            {
                slot = Evict();
            }

            ref var entry = ref _entries[slot];

            // Remove old mapping if this slot was occupied
            if (entry.Data != null && entry.PageId != pageId)
            {
                _index.TryRemove(entry.PageId, out _);
            }

            if (entry.Data == null)
            {
                entry.Data = ArrayPool<byte>.Shared.Rent(_pageSize);
            }

            data.Slice(0, _pageSize).CopyTo(entry.Data.AsSpan());
            entry.PageId = pageId;
            Volatile.Write(ref entry.Referenced, 1);
            entry.PinCount = 0;

            _index[pageId] = slot;
        }

        /// <summary>
        /// Invalidate a cached page (called after page modification).
        /// </summary>
        internal void Invalidate(long pageId)
        {
            if (_index.TryRemove(pageId, out int slot))
            {
                ref var entry = ref _entries[slot];
                entry.PageId = -1;
                Volatile.Write(ref entry.Referenced, 0);
            }
        }

        private int Evict()
        {
            lock (_evictLock)
            {
                // Clock sweep: find an unreferenced, unpinned slot
                for (int i = 0; i < _maxPages * 2; i++)
                {
                    int slot = _clockHand;
                    _clockHand = (_clockHand + 1) % _maxPages;

                    ref var entry = ref _entries[slot];

                    if (entry.PinCount > 0)
                        continue;

                    if (Volatile.Read(ref entry.Referenced) == 1)
                    {
                        Volatile.Write(ref entry.Referenced, 0); // Second chance
                        continue;
                    }

                    // Evict this slot
                    if (entry.PageId >= 0)
                        _index.TryRemove(entry.PageId, out _);

                    return slot;
                }

                // Fallback: evict clock hand position regardless
                int fallback = _clockHand;
                _clockHand = (_clockHand + 1) % _maxPages;
                ref var fb = ref _entries[fallback];
                if (fb.PageId >= 0)
                    _index.TryRemove(fb.PageId, out _);
                return fallback;
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Data != null)
                {
                    ArrayPool<byte>.Shared.Return(_entries[i].Data!);
                    _entries[i].Data = null;
                }
            }
        }

        private struct CacheEntry
        {
            public long PageId;
            public byte[]? Data;
            public int Referenced; // 0 or 1 (used as clock bit)
            public int PinCount;
        }
    }
}
