using System;

namespace AcornDB.Storage
{
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
}
