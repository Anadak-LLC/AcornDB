using System;
using System.Linq;
using System.Threading;
using AcornDB.Cache;

namespace AcornDB
{
    /// <summary>
    /// Cache management functionality for Tree&lt;T&gt;
    /// Handles TTL enforcement, cache eviction strategies, and automatic cleanup
    /// </summary>
    public partial class Tree<T> where T : class
    {
        private Timer? _expirationTimer;
        private TimeSpan _cleanupInterval = TimeSpan.FromMinutes(1);
        private bool _ttlEnforcementEnabled = true;
        private ICacheStrategy<T> _cacheStrategy;
        private bool _cacheEvictionEnabled = true;

        /// <summary>
        /// Gets or sets the cache eviction strategy
        /// </summary>
        public ICacheStrategy<T> CacheStrategy
        {
            get => _cacheStrategy;
            set
            {
                _cacheStrategy = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>
        /// Gets or sets whether cache eviction is enabled
        /// </summary>
        public bool CacheEvictionEnabled
        {
            get => _cacheEvictionEnabled;
            set => _cacheEvictionEnabled = value;
        }

        /// <summary>
        /// Gets or sets whether TTL enforcement is enabled
        /// </summary>
        public bool TtlEnforcementEnabled
        {
            get => _ttlEnforcementEnabled;
            set
            {
                _ttlEnforcementEnabled = value;
                if (value && _expirationTimer == null)
                {
                    StartExpirationTimer();
                }
                else if (!value && _expirationTimer != null)
                {
                    StopExpirationTimer();
                }
            }
        }

        /// <summary>
        /// Gets or sets the interval for automatic cleanup of expired nuts
        /// </summary>
        public TimeSpan CleanupInterval
        {
            get => _cleanupInterval;
            set
            {
                _cleanupInterval = value;
                if (_ttlEnforcementEnabled && _expirationTimer != null)
                {
                    // Restart timer with new interval
                    StopExpirationTimer();
                    StartExpirationTimer();
                }
            }
        }

        /// <summary>
        /// Start the automatic expiration timer
        /// </summary>
        private void StartExpirationTimer()
        {
            if (!_ttlEnforcementEnabled) return;

            _expirationTimer = new Timer(
                callback: _ => CleanupExpiredNuts(),
                state: null,
                dueTime: _cleanupInterval,
                period: _cleanupInterval
            );
        }

        /// <summary>
        /// Stop the automatic expiration timer
        /// </summary>
        private void StopExpirationTimer()
        {
            _expirationTimer?.Dispose();
            _expirationTimer = null;
        }

        /// <summary>
        /// Manually trigger cleanup of expired nuts
        /// </summary>
        /// <returns>Number of expired nuts removed</returns>
        public int CleanupExpiredNuts()
        {
            if (!_ttlEnforcementEnabled) return 0;

            var now = DateTime.UtcNow;
            List<string> expired;

            // Lock only the enumeration to get expired IDs
            lock (_cacheLock)
            {
                expired = _cache
                    .Where(x => x.Value.ExpiresAt.HasValue && x.Value.ExpiresAt.Value <= now)
                    .Select(x => x.Key)
                    .ToList();
            }

            // Delete outside the lock to avoid holding it too long
            foreach (var id in expired)
            {
                Toss(id);
                // Removed console logging - use events/telemetry instead
            }

            return expired.Count;
        }

        /// <summary>
        /// Get count of nuts that will expire within a given timespan
        /// </summary>
        public int GetExpiringNutsCount(TimeSpan within)
        {
            var threshold = DateTime.UtcNow.Add(within);
            lock (_cacheLock)
            {
                return _cache.Count(x =>
                    x.Value.ExpiresAt.HasValue &&
                    x.Value.ExpiresAt.Value <= threshold);
            }
        }

        /// <summary>
        /// Get IDs of nuts that will expire within a given timespan
        /// </summary>
        public string[] GetExpiringNuts(TimeSpan within)
        {
            var threshold = DateTime.UtcNow.Add(within);
            lock (_cacheLock)
            {
                return _cache
                    .Where(x => x.Value.ExpiresAt.HasValue && x.Value.ExpiresAt.Value <= threshold)
                    .Select(x => x.Key)
                    .ToArray();
            }
        }

        /// <summary>
        /// Trigger cache eviction based on the current strategy
        /// </summary>
        /// <returns>Number of items evicted</returns>
        public int EvictCacheItems()
        {
            if (!_cacheEvictionEnabled || _cacheStrategy == null)
                return 0;

            List<string> candidates;

            lock (_cacheLock)
            {
                candidates = _cacheStrategy.GetEvictionCandidates(_cache).ToList();
            }

            foreach (var id in candidates)
            {
                lock (_cacheLock)
                {
                    // Remove from cache but NOT from trunk (eviction != deletion)
                    _cache.Remove(id);
                }
                _cacheStrategy.OnToss(id);
                // Removed console logging - use events/telemetry instead
            }

            return candidates.Count;
        }

        /// <summary>
        /// Check if cache eviction is needed and trigger if necessary
        /// Called automatically after Stash operations
        /// </summary>
        private void CheckAndEvictCache()
        {
            if (!_cacheEvictionEnabled || _cacheStrategy == null)
                return;

            // Quick check: if cache is under limit, no need to call GetEvictionCandidates
            lock (_cacheLock)
            {
                if (_cacheStrategy is LRUCacheStrategy<T> lru && _cache.Count <= lru.MaxSize)
                    return;
            }

            IEnumerable<string> candidates;
            lock (_cacheLock)
            {
                candidates = _cacheStrategy.GetEvictionCandidates(_cache);
            }

            if (candidates.Any())
            {
                EvictCacheItems();
            }
        }
    }
}
