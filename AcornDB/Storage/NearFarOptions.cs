using System;

namespace AcornDB.Storage
{
    /// <summary>
    /// Near/far cache configuration options
    /// </summary>
    public class NearFarOptions
    {
        /// <summary>
        /// Cache write strategy
        /// Default: Invalidate (safest for consistency)
        /// </summary>
        public CacheWriteStrategy WriteStrategy { get; set; } = CacheWriteStrategy.Invalidate;

        /// <summary>
        /// Populate near cache on far cache hit
        /// Default: true
        /// </summary>
        public bool PopulateNearOnFarHit { get; set; } = true;

        /// <summary>
        /// Populate far cache on backing store hit
        /// Default: true
        /// </summary>
        public bool PopulateFarOnBackingHit { get; set; } = true;

        /// <summary>
        /// Populate near cache on backing store hit
        /// Default: true
        /// </summary>
        public bool PopulateNearOnBackingHit { get; set; } = true;

        /// <summary>
        /// Default options (invalidate on write, populate all caches on read)
        /// </summary>
        public static NearFarOptions Default => new NearFarOptions();

        /// <summary>
        /// Write-through strategy (update caches immediately)
        /// Best for: Read-heavy workloads, consistency requirements
        /// </summary>
        public static NearFarOptions WriteThrough => new NearFarOptions
        {
            WriteStrategy = CacheWriteStrategy.WriteThrough,
            PopulateNearOnFarHit = true,
            PopulateFarOnBackingHit = true,
            PopulateNearOnBackingHit = true
        };

        /// <summary>
        /// Write-around strategy (bypass cache on writes)
        /// Best for: Write-heavy workloads, data written once and read rarely
        /// </summary>
        public static NearFarOptions WriteAround => new NearFarOptions
        {
            WriteStrategy = CacheWriteStrategy.WriteAround,
            PopulateNearOnFarHit = true,
            PopulateFarOnBackingHit = true,
            PopulateNearOnBackingHit = false // Don't cache on first read
        };

        /// <summary>
        /// Aggressive caching (write-through, populate all levels)
        /// Best for: Read-only or append-only data
        /// </summary>
        public static NearFarOptions Aggressive => new NearFarOptions
        {
            WriteStrategy = CacheWriteStrategy.WriteThrough,
            PopulateNearOnFarHit = true,
            PopulateFarOnBackingHit = true,
            PopulateNearOnBackingHit = true
        };
    }
}
