using System;
using System.Collections.Generic;
using System.Linq;

namespace AcornDB.Cache
{
    /// <summary>
    /// LRU (Least Recently Used) cache eviction strategy
    /// Evicts items that haven't been accessed recently when cache size exceeds limit
    /// </summary>
    public class LRUCacheStrategy<T> : ICacheStrategy<T>
    {
        private readonly int _maxSize;
        private readonly Dictionary<string, DateTime> _accessTimes = new();
        private readonly object _lock = new();

        /// <summary>
        /// Gets the maximum cache size before eviction occurs
        /// </summary>
        public int MaxSize => _maxSize;

        /// <summary>
        /// Gets the number of tracked items
        /// </summary>
        public int TrackedItemCount
        {
            get
            {
                lock (_lock)
                {
                    return _accessTimes.Count;
                }
            }
        }

        /// <summary>
        /// Create a new LRU cache strategy
        /// </summary>
        /// <param name="maxSize">Maximum number of items to keep in cache (default: 10,000)</param>
        public LRUCacheStrategy(int maxSize = 10_000)
        {
            if (maxSize <= 0)
                throw new ArgumentException("Max size must be greater than 0", nameof(maxSize));

            _maxSize = maxSize;
        }

        public void OnStash(string id, Nut<T> nut)
        {
            lock (_lock)
            {
                _accessTimes[id] = DateTime.UtcNow;
            }
        }

        public void OnCrack(string id)
        {
            lock (_lock)
            {
                // Update access time on read
                if (_accessTimes.ContainsKey(id))
                {
                    _accessTimes[id] = DateTime.UtcNow;
                }
            }
        }

        public void OnToss(string id)
        {
            lock (_lock)
            {
                _accessTimes.Remove(id);
            }
        }

        public IEnumerable<string> GetEvictionCandidates(IDictionary<string, Nut<T>> currentCache)
        {
            lock (_lock)
            {
                // If we're under the limit, no eviction needed
                if (currentCache.Count <= _maxSize)
                    return Enumerable.Empty<string>();

                // Calculate how many items to evict (20% buffer to reduce eviction frequency)
                int targetSize = (int)(_maxSize * 0.8);
                int itemsToEvict = currentCache.Count - targetSize;

                // Performance optimization: Use a more efficient algorithm
                // Instead of sorting the entire dictionary, use a partial sort or heap
                // For now, we'll use OrderBy with Take which is reasonably efficient

                // Only process items that are actually in the cache
                var candidates = _accessTimes
                    .Where(kvp => currentCache.ContainsKey(kvp.Key))
                    .OrderBy(kvp => kvp.Value)
                    .Take(itemsToEvict)
                    .Select(kvp => kvp.Key)
                    .ToList();

                return candidates;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _accessTimes.Clear();
            }
        }

        /// <summary>
        /// Get the last access time for a specific item
        /// </summary>
        public DateTime? GetLastAccessTime(string id)
        {
            lock (_lock)
            {
                return _accessTimes.TryGetValue(id, out var time) ? time : null;
            }
        }

        /// <summary>
        /// Get statistics about the LRU cache
        /// </summary>
        public LRUCacheStats GetStats()
        {
            lock (_lock)
            {
                if (_accessTimes.Count == 0)
                {
                    return new LRUCacheStats
                    {
                        TrackedItems = 0,
                        MaxSize = _maxSize,
                        OldestAccessTime = null,
                        NewestAccessTime = null,
                        UtilizationPercentage = 0
                    };
                }

                var times = _accessTimes.Values.ToList();
                return new LRUCacheStats
                {
                    TrackedItems = _accessTimes.Count,
                    MaxSize = _maxSize,
                    OldestAccessTime = times.Min(),
                    NewestAccessTime = times.Max(),
                    UtilizationPercentage = (_accessTimes.Count * 100.0) / _maxSize
                };
            }
        }
    }
}
