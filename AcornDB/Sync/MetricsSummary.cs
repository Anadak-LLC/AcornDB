using System;

namespace AcornDB.Sync
{
    /// <summary>
    /// Summary of metrics collected by MetricsBranch
    /// </summary>
    public class MetricsSummary
    {
        public long TotalStash { get; set; }
        public long TotalToss { get; set; }
        public long TotalSquabble { get; set; }
        public long TotalShake { get; set; }
        public long TotalOperations { get; set; }
        public double OperationsPerSecond { get; set; }
        public TimeSpan Uptime { get; set; }
        public DateTime? LastEventTime { get; set; }
        public int UniqueTreesSeen { get; set; }
    }
}
