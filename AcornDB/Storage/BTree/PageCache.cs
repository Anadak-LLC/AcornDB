using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;

namespace AcornDB.Storage.BTree
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
    ///   - Statistics: lock-free hit/miss/eviction counters for measurement
    /// </summary>
    internal sealed class PageCache : IDisposable
    {
        private readonly int _maxPages;
        private readonly int _pageSize;
        private readonly CacheEntry[] _entries;
        private readonly ConcurrentDictionary<long, int> _index;
        private int _clockHand;
        private int _count;
        private readonly object _evictLock = new();

        // Statistics (lock-free counters)
        private long _hits;
        private long _misses;
        private long _evictions;

        internal PageCache(int maxPages, int pageSize)
        {
            if (maxPages <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPages), maxPages, "Must be > 0.");
            if (pageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Must be > 0.");

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
            {
                Interlocked.Increment(ref _misses);
                return false;
            }

            ref var entry = ref _entries[slot];
            if (entry.PageId != pageId || entry.Data == null)
            {
                // Stale index entry (slot was reassigned between lookup and access)
                _index.TryRemove(pageId, out _);
                Interlocked.Increment(ref _misses);
                return false;
            }

            entry.Data.AsSpan(0, _pageSize).CopyTo(dest);
            Volatile.Write(ref entry.Referenced, 1);
            Interlocked.Increment(ref _hits);
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
                if (existing.PageId == pageId && existing.Data != null)
                {
                    data.Slice(0, _pageSize).CopyTo(existing.Data.AsSpan());
                    Volatile.Write(ref existing.Referenced, 1);
                    return;
                }
            }

            int slot = AcquireSlot();
            ref var entry = ref _entries[slot];

            // Remove old mapping if this slot was occupied by a different page
            if (entry.Data != null && entry.PageId >= 0 && entry.PageId != pageId)
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
        /// The slot's buffer is retained for reuse by the next Put.
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

        /// <summary>
        /// Acquire a slot index: either an unused slot or an evicted one.
        /// </summary>
        private int AcquireSlot()
        {
            // Fast path: try to claim an unused slot
            int current = Volatile.Read(ref _count);
            while (current < _maxPages)
            {
                int claimed = Interlocked.CompareExchange(ref _count, current + 1, current);
                if (claimed == current)
                    return current; // Successfully claimed slot 'current'
                current = claimed;  // CAS failed, retry with new value
            }

            // All slots occupied: evict
            return Evict();
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

                    Interlocked.Increment(ref _evictions);
                    return slot;
                }

                // Fallback: evict clock hand position regardless
                int fallback = _clockHand;
                _clockHand = (_clockHand + 1) % _maxPages;
                ref var fb = ref _entries[fallback];
                if (fb.PageId >= 0)
                    _index.TryRemove(fb.PageId, out _);

                Interlocked.Increment(ref _evictions);
                return fallback;
            }
        }

        #region Statistics

        /// <summary>
        /// Number of cache hits (page found in cache).
        /// </summary>
        internal long Hits => Volatile.Read(ref _hits);

        /// <summary>
        /// Number of cache misses (page not in cache, disk read required).
        /// </summary>
        internal long Misses => Volatile.Read(ref _misses);

        /// <summary>
        /// Number of page evictions performed.
        /// </summary>
        internal long Evictions => Volatile.Read(ref _evictions);

        /// <summary>
        /// Cache hit ratio (0.0 – 1.0). Returns 0 if no accesses yet.
        /// </summary>
        internal double HitRatio
        {
            get
            {
                long h = Hits, m = Misses;
                long total = h + m;
                return total == 0 ? 0.0 : (double)h / total;
            }
        }

        /// <summary>
        /// Number of pages currently in the cache.
        /// </summary>
        internal int Count => _index.Count;

        /// <summary>
        /// Maximum number of pages the cache can hold.
        /// </summary>
        internal int Capacity => _maxPages;

        /// <summary>
        /// Reset all statistics counters.
        /// </summary>
        internal void ResetStatistics()
        {
            Interlocked.Exchange(ref _hits, 0);
            Interlocked.Exchange(ref _misses, 0);
            Interlocked.Exchange(ref _evictions, 0);
        }

        #endregion

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
