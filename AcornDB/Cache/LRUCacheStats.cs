using System;

namespace AcornDB.Cache
{
    /// <summary>
    /// Statistics for LRU cache strategy
    /// </summary>
    public class LRUCacheStats
    {
        public int TrackedItems { get; set; }
        public int MaxSize { get; set; }
        public DateTime? OldestAccessTime { get; set; }
        public DateTime? NewestAccessTime { get; set; }
        public double UtilizationPercentage { get; set; }
    }
}
