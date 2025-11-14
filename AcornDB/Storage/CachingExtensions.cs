using System;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Storage
{
    /// <summary>
    /// Extension methods for adding caching to trunks
    /// </summary>
    public static class CachingExtensions
    {
        /// <summary>
        /// Wrap trunk with in-memory cache
        /// </summary>
        /// <param name="trunk">Backing trunk to cache</param>
        /// <param name="options">Cache options (TTL, capacity)</param>
        /// <returns>Cached trunk</returns>
        public static CachedTrunk<T> WithCache<T>(
            this ITrunk<T> trunk,
            CacheOptions? options = null) where T : class
        {
            return new CachedTrunk<T>(trunk, options);
        }

        /// <summary>
        /// Wrap trunk with near/far distributed caching
        /// </summary>
        /// <param name="trunk">Backing trunk (durable storage)</param>
        /// <param name="farCache">Far cache (distributed, e.g., Redis)</param>
        /// <param name="options">Near/far options</param>
        /// <returns>Near/far trunk</returns>
        public static NearFarTrunk<T> WithNearFarCache<T>(
            this ITrunk<T> trunk,
            ITrunk<T> farCache,
            NearFarOptions? options = null) where T : class
        {
            var nearCache = new MemoryTrunk<T>();
            return new NearFarTrunk<T>(nearCache, farCache, trunk, options);
        }

        /// <summary>
        /// Wrap trunk with near/far distributed caching (custom near cache)
        /// </summary>
        /// <param name="trunk">Backing trunk (durable storage)</param>
        /// <param name="nearCache">Near cache (local, e.g., MemoryTrunk)</param>
        /// <param name="farCache">Far cache (distributed, e.g., Redis)</param>
        /// <param name="options">Near/far options</param>
        /// <returns>Near/far trunk</returns>
        public static NearFarTrunk<T> WithNearFarCache<T>(
            this ITrunk<T> trunk,
            ITrunk<T> nearCache,
            ITrunk<T> farCache,
            NearFarOptions? options = null) where T : class
        {
            return new NearFarTrunk<T>(nearCache, farCache, trunk, options);
        }

    }
}
