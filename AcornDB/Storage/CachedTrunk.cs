using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Storage
{
    /// <summary>
    /// Simple cached trunk with write-through to backing store.
    /// Uses in-memory cache for fast reads with configurable TTL and capacity.
    ///
    /// Cache Strategy:
    /// - Reads: Check cache first, fallback to backing store, populate cache
    /// - Writes: Write to backing store first (write-through), then update cache
    /// - Deletes: Delete from backing store, invalidate cache
    ///
    /// Use Cases:
    /// - Read-heavy workloads
    /// - Reduce latency for frequently accessed data
    /// - Reduce load on slow backing stores (S3, databases)
    /// </summary>
    public class CachedTrunk<T> : ITrunk<T>, IDisposable
    {
        private readonly ITrunk<T> _backingStore;
        private readonly MemoryTrunk<T> _cache;
        private readonly CacheOptions _options;
        private bool _disposed;

        /// <summary>
        /// Create cached trunk with in-memory cache
        /// </summary>
        /// <param name="backingStore">Durable backing store</param>
        /// <param name="options">Cache options (TTL, capacity, etc.)</param>
        public CachedTrunk(ITrunk<T> backingStore, CacheOptions? options = null)
        {
            _backingStore = backingStore ?? throw new ArgumentNullException(nameof(backingStore));
            _cache = new MemoryTrunk<T>();
            _options = options ?? CacheOptions.Default;

            var backingCaps = _backingStore.Capabilities;
            Console.WriteLine($"💾 CachedTrunk initialized:");
            Console.WriteLine($"   Backing Store: {backingCaps.TrunkType}");
            Console.WriteLine($"   Cache TTL: {(_options.TimeToLive?.TotalSeconds.ToString("F0") + "s" ?? "Infinite")}");
            Console.WriteLine($"   Max Cache Size: {(_options.MaxCacheSize?.ToString() ?? "Unlimited")}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Save(string id, Nut<T> nut)
        {
            // Write-through: backing store first, then cache
            _backingStore.Save(id, nut);

            // Update cache
            if (ShouldCache(nut))
            {
                _cache.Save(id, nut);
                EvictIfNeeded();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Load(string id)
        {
            // Try cache first
            var nut = _cache.Load(id);

            if (nut != null)
            {
                // Check TTL
                if (IsExpired(nut))
                {
                    _cache.Delete(id);
                    nut = null;
                }
                else
                {
                    return nut; // Cache hit
                }
            }

            // Cache miss - load from backing store
            nut = _backingStore.Load(id);

            // Populate cache
            if (nut != null && ShouldCache(nut))
            {
                _cache.Save(id, nut);
                EvictIfNeeded();
            }

            return nut;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(string id)
        {
            // Delete from backing store
            _backingStore.Delete(id);

            // Invalidate cache
            _cache.Delete(id);
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            // Always load from backing store for consistency
            var nuts = _backingStore.LoadAll().ToList();

            // Optionally warm cache
            if (_options.WarmCacheOnLoadAll)
            {
                foreach (var nut in nuts)
                {
                    if (ShouldCache(nut))
                    {
                        _cache.Save(nut.Id, nut);
                    }
                }
                EvictIfNeeded();
            }

            return nuts;
        }

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            // History always from backing store (cache doesn't store history)
            return _backingStore.GetHistory(id);
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            // Export from backing store
            return _backingStore.ExportChanges();
        }

        public void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            // Import to backing store
            _backingStore.ImportChanges(incoming);

            // Invalidate cache (simplest strategy)
            if (_options.InvalidateCacheOnImport)
            {
                ClearCache();
            }
        }

        /// <summary>
        /// Clear the entire cache
        /// </summary>
        public void ClearCache()
        {
            var allIds = _cache.LoadAll().Select(n => n.Id).ToList();
            foreach (var id in allIds)
            {
                _cache.Delete(id);
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public CacheStats GetCacheStats()
        {
            var cached = _cache.LoadAll().ToList();
            var expired = cached.Count(IsExpired);

            return new CacheStats
            {
                CachedItemCount = cached.Count,
                ExpiredItemCount = expired,
                ActiveItemCount = cached.Count - expired
            };
        }

        private bool ShouldCache(Nut<T> nut)
        {
            // Check if nut has its own expiration that conflicts with cache
            if (nut.ExpiresAt.HasValue && nut.ExpiresAt.Value < DateTime.UtcNow)
            {
                return false; // Already expired
            }

            return true;
        }

        private bool IsExpired(Nut<T> nut)
        {
            if (!_options.TimeToLive.HasValue)
                return false; // No TTL

            var age = DateTime.UtcNow - nut.Timestamp;
            return age > _options.TimeToLive.Value;
        }

        private void EvictIfNeeded()
        {
            if (!_options.MaxCacheSize.HasValue)
                return;

            var cached = _cache.LoadAll().ToList();

            // Remove expired first
            var expired = cached.Where(IsExpired).ToList();
            foreach (var nut in expired)
            {
                _cache.Delete(nut.Id);
            }

            // Check size again
            cached = _cache.LoadAll().ToList();
            if (cached.Count <= _options.MaxCacheSize.Value)
                return;

            // Evict oldest items (LRU approximation using timestamp)
            var toEvict = cached
                .OrderBy(n => n.Timestamp)
                .Take(cached.Count - _options.MaxCacheSize.Value);

            foreach (var nut in toEvict)
            {
                _cache.Delete(nut.Id);
            }
        }

        private string GetTrunkType(ITrunk<T> trunk)
        {
            var caps = trunk.Capabilities;
            return caps.TrunkType;
        }

        // ITrunkCapabilities implementation - forward to backing store with custom TrunkType
        public ITrunkCapabilities Capabilities
        {
            get
            {
                var backingCaps = _backingStore.Capabilities;
                return new TrunkCapabilities
                {
                    SupportsHistory = backingCaps.SupportsHistory,
                    SupportsSync = true,
                    IsDurable = backingCaps.IsDurable,
                    SupportsAsync = backingCaps.SupportsAsync,
                    TrunkType = $"CachedTrunk({backingCaps.TrunkType})"
                };
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_cache is IDisposable cacheDisposable)
                cacheDisposable.Dispose();

            if (_backingStore is IDisposable backingDisposable)
                backingDisposable.Dispose();
        }
    }

    /// <summary>
    /// Cache configuration options
    /// </summary>
    public class CacheOptions
    {
        /// <summary>
        /// Time-to-live for cached items. Null = infinite.
        /// Default: 5 minutes
        /// </summary>
        public TimeSpan? TimeToLive { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Maximum number of items in cache. Null = unlimited.
        /// Default: 10,000 items
        /// </summary>
        public int? MaxCacheSize { get; set; } = 10_000;

        /// <summary>
        /// Warm cache on LoadAll() operations
        /// Default: false (LoadAll doesn't populate cache)
        /// </summary>
        public bool WarmCacheOnLoadAll { get; set; } = false;

        /// <summary>
        /// Invalidate entire cache on ImportChanges()
        /// Default: true (safest for consistency)
        /// </summary>
        public bool InvalidateCacheOnImport { get; set; } = true;

        /// <summary>
        /// Default cache options (5min TTL, 10K items)
        /// </summary>
        public static CacheOptions Default => new CacheOptions();

        /// <summary>
        /// Short-lived cache for very dynamic data (1min TTL, 1K items)
        /// </summary>
        public static CacheOptions ShortLived => new CacheOptions
        {
            TimeToLive = TimeSpan.FromMinutes(1),
            MaxCacheSize = 1_000
        };

        /// <summary>
        /// Long-lived cache for stable data (1 hour TTL, 100K items)
        /// </summary>
        public static CacheOptions LongLived => new CacheOptions
        {
            TimeToLive = TimeSpan.FromHours(1),
            MaxCacheSize = 100_000
        };

        /// <summary>
        /// Aggressive caching (infinite TTL, unlimited size)
        /// Use only for read-only or append-only data
        /// </summary>
        public static CacheOptions Aggressive => new CacheOptions
        {
            TimeToLive = null,
            MaxCacheSize = null,
            WarmCacheOnLoadAll = true
        };
    }

    /// <summary>
    /// Cache statistics
    /// </summary>
    public class CacheStats
    {
        public int CachedItemCount { get; set; }
        public int ExpiredItemCount { get; set; }
        public int ActiveItemCount { get; set; }
    }
}
