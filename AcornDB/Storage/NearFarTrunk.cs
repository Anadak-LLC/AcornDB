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
    /// Near/Far caching trunk implementing distributed cache hierarchy.
    /// Near cache: Fast local in-memory cache (client-side)
    /// Far cache: Shared distributed cache (Redis, Memcached)
    /// Backing store: Durable persistence (database, files, cloud storage)
    ///
    /// Cache Strategy:
    /// - Reads: Near → Far → Backing store → Populate caches
    /// - Writes: Backing store first (write-through) → Invalidate caches
    /// - Deletes: Backing store → Invalidate caches
    ///
    /// Benefits:
    /// - Ultra-low latency for frequently accessed data (near cache)
    /// - Reduced load on backing store (far cache shared across instances)
    /// - Consistency across distributed application instances
    /// - Automatic cache invalidation on writes
    ///
    /// Use Cases:
    /// - Distributed web applications
    /// - Microservices with shared cache
    /// - Read-heavy workloads with multiple replicas
    /// - Reducing database load in high-traffic scenarios
    /// </summary>
    public class NearFarTrunk<T> : ITrunk<T>, IDisposable
    {
        private readonly ITrunk<T> _nearCache;
        private readonly ITrunk<T> _farCache;
        private readonly ITrunk<T> _backingStore;
        private readonly NearFarOptions _options;
        private bool _disposed;

        /// <summary>
        /// Create near/far trunk with distributed caching
        /// </summary>
        /// <param name="nearCache">Near cache (local, fast - typically MemoryTrunk)</param>
        /// <param name="farCache">Far cache (distributed, shared - typically RedisTrunk)</param>
        /// <param name="backingStore">Backing store (durable persistence)</param>
        /// <param name="options">Near/far options</param>
        public NearFarTrunk(
            ITrunk<T> nearCache,
            ITrunk<T> farCache,
            ITrunk<T> backingStore,
            NearFarOptions? options = null)
        {
            _nearCache = nearCache ?? throw new ArgumentNullException(nameof(nearCache));
            _farCache = farCache ?? throw new ArgumentNullException(nameof(farCache));
            _backingStore = backingStore ?? throw new ArgumentNullException(nameof(backingStore));
            _options = options ?? NearFarOptions.Default;

            AcornLog.Info($"🔄 NearFarTrunk initialized:");
            AcornLog.Info($"   Near Cache: {GetTrunkType(_nearCache)} (local)");
            AcornLog.Info($"   Far Cache: {GetTrunkType(_farCache)} (distributed)");
            AcornLog.Info($"   Backing Store: {GetTrunkType(_backingStore)}");
            AcornLog.Info($"   Write Strategy: {_options.WriteStrategy}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Stash(string id, Nut<T> nut)
        {
            // Write to backing store first (write-through)
            _backingStore.Stash(id, nut);

            if (_options.WriteStrategy == CacheWriteStrategy.WriteThrough)
            {
                // Update caches immediately
                _farCache.Stash(id, nut);
                _nearCache.Stash(id, nut);
            }
            else if (_options.WriteStrategy == CacheWriteStrategy.Invalidate)
            {
                // Invalidate caches (safest for consistency)
                try { _nearCache.Toss(id); } catch { /* Ignore */ }
                try { _farCache.Toss(id); } catch { /* Ignore */ }
            }
            // WriteAround: Don't touch caches
        }

        [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
        public void Save(string id, Nut<T> nut) => Stash(id, nut);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Crack(string id)
        {
            Nut<T>? nut = null;

            // 1. Try near cache (fastest)
            nut = _nearCache.Crack(id);
            if (nut != null)
            {
                return nut; // Near cache hit
            }

            // 2. Try far cache (shared, faster than backing store)
            nut = _farCache.Crack(id);
            if (nut != null)
            {
                // Populate near cache
                if (_options.PopulateNearOnFarHit)
                {
                    _nearCache.Stash(id, nut);
                }
                return nut; // Far cache hit
            }

            // 3. Load from backing store (slowest)
            nut = _backingStore.Crack(id);
            if (nut != null)
            {
                // Populate caches
                if (_options.PopulateFarOnBackingHit)
                {
                    _farCache.Stash(id, nut);
                }
                if (_options.PopulateNearOnBackingHit)
                {
                    _nearCache.Stash(id, nut);
                }
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

            // Invalidate caches
            try { _nearCache.Toss(id); } catch { /* Ignore */ }
            try { _farCache.Toss(id); } catch { /* Ignore */ }
        }

        [Obsolete("Use Toss() instead. This method will be removed in a future version.")]
        public void Delete(string id) => Toss(id);

        public IEnumerable<Nut<T>> CrackAll()
        {
            // Always load from backing store for consistency
            return _backingStore.CrackAll();
        }

        [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
        public IEnumerable<Nut<T>> LoadAll() => CrackAll();

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            // History always from backing store (caches don't store history)
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

            // Invalidate caches (safest for consistency)
            ClearAllCaches();
        }

        /// <summary>
        /// Clear all caches (near and far)
        /// </summary>
        public void ClearAllCaches()
        {
            ClearCache(_nearCache);
            ClearCache(_farCache);
        }

        /// <summary>
        /// Clear near cache only
        /// </summary>
        public void ClearNearCache()
        {
            ClearCache(_nearCache);
        }

        /// <summary>
        /// Clear far cache only
        /// </summary>
        public void ClearFarCache()
        {
            ClearCache(_farCache);
        }

        private void ClearCache(ITrunk<T> cache)
        {
            try
            {
                var allIds = cache.CrackAll().Select(n => n.Id).ToList();
                foreach (var id in allIds)
                {
                    cache.Toss(id);
                }
            }
            catch
            {
                // Ignore cache errors
            }
        }

        /// <summary>
        /// Get statistics for all cache levels
        /// </summary>
        public NearFarStats GetStats()
        {
            return new NearFarStats
            {
                NearCacheCount = SafeCount(_nearCache),
                FarCacheCount = SafeCount(_farCache),
                BackingStoreCount = SafeCount(_backingStore)
            };
        }

        private int SafeCount(ITrunk<T> trunk)
        {
            try
            {
                return trunk.CrackAll().Count();
            }
            catch
            {
                return -1; // Error
            }
        }

        private string GetTrunkType(ITrunk<T> trunk)
        {
            var caps = trunk.Capabilities;
            return caps.TrunkType;
        }

        // ITrunkCapabilities implementation - forward to near cache (primary read cache) with custom TrunkType
        public ITrunkCapabilities Capabilities
        {
            get
            {
                var nearCaps = _nearCache.Capabilities;
                return new TrunkCapabilities
                {
                    SupportsHistory = nearCaps.SupportsHistory,
                    SupportsSync = true,
                    IsDurable = nearCaps.IsDurable,
                    SupportsAsync = nearCaps.SupportsAsync,
                    TrunkType = $"NearFarTrunk({GetTrunkType(_nearCache)}+{GetTrunkType(_farCache)}+{GetTrunkType(_backingStore)})"
                };
            }
        }

        // IRoot interface members - forward to backing store
        public IReadOnlyList<IRoot> Roots => _backingStore.Roots;
        public void AddRoot(IRoot root) => _backingStore.AddRoot(root);
        public bool RemoveRoot(string name) => _backingStore.RemoveRoot(name);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_nearCache is IDisposable nearDisposable)
                nearDisposable.Dispose();

            if (_farCache is IDisposable farDisposable)
                farDisposable.Dispose();

            if (_backingStore is IDisposable backingDisposable)
                backingDisposable.Dispose();
        }
    }
}
