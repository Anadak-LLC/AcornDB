using System;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Statistics about an index for query planning
    /// </summary>
    public class IndexStatistics
    {
        /// <summary>
        /// Total number of entries in the index
        /// </summary>
        public long EntryCount { get; set; }

        /// <summary>
        /// Number of unique values (for cardinality estimation)
        /// </summary>
        public long UniqueValueCount { get; set; }

        /// <summary>
        /// Average selectivity (0.0 = all same value, 1.0 = all unique)
        /// </summary>
        public double Selectivity => EntryCount > 0 ? (double)UniqueValueCount / EntryCount : 0.0;

        /// <summary>
        /// Approximate memory usage in bytes
        /// </summary>
        public long MemoryUsageBytes { get; set; }

        /// <summary>
        /// Last time the index was updated
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
