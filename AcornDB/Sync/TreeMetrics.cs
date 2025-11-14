using System;

namespace AcornDB.Sync
{
    /// <summary>
    /// Metrics for a specific tree
    /// </summary>
    public class TreeMetrics
    {
        public string TreeId { get; set; } = string.Empty;
        public long StashCount { get; set; }
        public long TossCount { get; set; }
        public long SquabbleCount { get; set; }
        public DateTime? LastSeen { get; set; }

        public long TotalOperations => StashCount + TossCount + SquabbleCount;
    }
}
