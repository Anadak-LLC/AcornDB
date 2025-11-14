using System;
using AcornDB.Logging;
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
    public class CachedTrunk<T> : ITrunk<T>, IDisposable where T : class
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
            AcornLog.Info($"💾 CachedTrunk initialized:");
            AcornLog.Info($"   Backing Store: {backingCaps.TrunkType}");
            AcornLog.Info($"   Cache TTL: {(_options.TimeToLive?.TotalSeconds.ToString("F0") + "s" ?? "Infinite")}");
            AcornLog.Info($"   Max Cache Size: {(_options.MaxCacheSize?.ToString() ?? "Unlimited")}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Stash(string id, Nut<T> nut)
        {
            // Write-through: backing store first, then cache
            _backingStore.Stash(id, nut);

            // Update cache
            if (ShouldCache(nut))
            {
                _cache.Stash(id, nut);
                EvictIfNeeded();
            }
        }

        [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
        public void Save(string id, Nut<T> nut) => Stash(id, nut);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Crack(string id)
        {
            // Try cache first
            var nut = _cache.Crack(id);

            if (nut != null)
            {
                // Check TTL
                if (IsExpired(nut))
                {
                    _cache.Toss(id);
                    nut = null;
                }
                else
                {
                    return nut; // Cache hit
                }
            }

            // Cache miss - load from backing store
            nut = _backingStore.Crack(id);

            // Populate cache
            if (nut != null && ShouldCache(nut))
            {
                _cache.Stash(id, nut);
                EvictIfNeeded();
            }

            return nut;
        }

        [Obsolete("Use Crack() instead. This method will be removed in a future version.")]
        public Nut<T>? Load(string id) => Crack(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Toss(string id)
        {
            // Delete from backing store
            _backingStore.Toss(id);

            // Invalidate cache
            _cache.Toss(id);
        }

        [Obsolete("Use Toss() instead. This method will be removed in a future version.")]
        public void Delete(string id) => Toss(id);

        public IEnumerable<Nut<T>> CrackAll()
        {
            // Always load from backing store for consistency
            var nuts = _backingStore.CrackAll().ToList();

            // Optionally warm cache
            if (_options.WarmCacheOnLoadAll)
            {
                foreach (var nut in nuts)
                {
                    if (ShouldCache(nut))
                    {
                        _cache.Stash(nut.Id, nut);
                    }
                }
                EvictIfNeeded();
            }

            return nuts;
        }

        [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
        public IEnumerable<Nut<T>> LoadAll() => CrackAll();

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
            var allIds = _cache.CrackAll().Select(n => n.Id).ToList();
            foreach (var id in allIds)
            {
                _cache.Toss(id);
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public CacheStats GetCacheStats()
        {
            var cached = _cache.CrackAll().ToList();
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

            var cached = _cache.CrackAll().ToList();

            // Remove expired first
            var expired = cached.Where(IsExpired).ToList();
            foreach (var nut in expired)
            {
                _cache.Toss(nut.Id);
            }

            // Check size again
            cached = _cache.CrackAll().ToList();
            if (cached.Count <= _options.MaxCacheSize.Value)
                return;

            // Evict oldest items (LRU approximation using timestamp)
            var toEvict = cached
                .OrderBy(n => n.Timestamp)
                .Take(cached.Count - _options.MaxCacheSize.Value);

            foreach (var nut in toEvict)
            {
                _cache.Toss(nut.Id);
            }
        }

        private string GetTrunkType(ITrunk<T> trunk)
        {
            var caps = trunk.Capabilities;
            return caps.TrunkType;
        }

        // Root processors - forward to backing store
        public IReadOnlyList<IRoot> Roots => _backingStore.Roots;

        public void AddRoot(IRoot root)
        {
            _backingStore.AddRoot(root);
        }

        public bool RemoveRoot(string name)
        {
            return _backingStore.RemoveRoot(name);
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
}
