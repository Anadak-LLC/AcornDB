using System;

namespace AcornDB.Storage
{
    /// <summary>
    /// Controls when and how automatic compaction occurs for <see cref="BitcaskTrunk{T}"/>.
    ///
    /// Bitcask is an append-only log; every update or delete appends a new record, leaving the
    /// previous version as dead space. Compaction rewrites the file with only live entries.
    ///
    /// Compaction is triggered when <b>any</b> of the enabled thresholds are exceeded, checked
    /// after each write or delete operation. Set a threshold to <c>null</c> to disable that trigger.
    /// </summary>
    public class CompactionOptions
    {
        /// <summary>
        /// Maximum ratio of dead bytes to total file size before compaction triggers.
        /// Expressed as a fraction (0.0–1.0). For example, 0.5 means compact when ≥50%
        /// of the file is dead space.
        ///
        /// Industry reference: Riak Bitcask uses 60% (0.6) as default fragmentation_threshold.
        /// Default: 0.5 (50%)
        /// </summary>
        public double? DeadSpaceRatioThreshold { get; set; } = 0.5;

        /// <summary>
        /// Absolute number of dead records (superseded updates + tombstones) before compaction triggers.
        /// Useful for workloads with small records where ratio-based detection is too coarse.
        /// Default: 10,000
        /// </summary>
        public int? DeadRecordCountThreshold { get; set; } = 10_000;

        /// <summary>
        /// Number of mutation operations (Stash to existing keys + Toss) since the last compaction
        /// before compaction triggers. Captures update-heavy workloads that may not yet hit the
        /// dead space ratio.
        /// Default: 50,000
        /// </summary>
        public int? MutationCountThreshold { get; set; } = 50_000;

        /// <summary>
        /// Minimum file size in bytes before any automatic compaction is considered.
        /// Avoids wasting I/O on small files where dead space is negligible.
        /// Default: 10 MB
        /// </summary>
        public long MinimumFileSizeBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// Interval for background compaction checks. When set, a timer periodically evaluates
        /// whether compaction thresholds have been exceeded and triggers compaction if so.
        /// Set to <c>null</c> to disable timer-based checks (thresholds are still checked inline
        /// after each mutation).
        ///
        /// Industry reference: Riak Bitcask uses a default merge interval of 3 hours.
        /// Default: 1 hour
        /// </summary>
        public TimeSpan? BackgroundCheckInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// When true, compaction is disabled entirely. <see cref="BitcaskTrunk{T}.Compact"/>
        /// can still be called manually.
        /// Default: false
        /// </summary>
        public bool DisableAutoCompaction { get; set; } = false;

        /// <summary>
        /// Default options: balanced thresholds suitable for most workloads.
        /// 50% dead space ratio, 10K dead records, 50K mutations, 10MB minimum, 1-hour background check.
        /// </summary>
        public static CompactionOptions Default => new CompactionOptions();

        /// <summary>
        /// Aggressive compaction: lower thresholds for latency-sensitive or space-constrained workloads.
        /// 30% dead space, 1K dead records, 5K mutations, 1MB minimum, 10-minute background check.
        /// </summary>
        public static CompactionOptions Aggressive => new CompactionOptions
        {
            DeadSpaceRatioThreshold = 0.30,
            DeadRecordCountThreshold = 1_000,
            MutationCountThreshold = 5_000,
            MinimumFileSizeBytes = 1 * 1024 * 1024,
            BackgroundCheckInterval = TimeSpan.FromMinutes(10)
        };

        /// <summary>
        /// Conservative compaction: higher thresholds for write-heavy append workloads where
        /// compaction I/O should be minimized.
        /// 70% dead space, 100K dead records, 500K mutations, 100MB minimum, 6-hour background check.
        /// </summary>
        public static CompactionOptions Conservative => new CompactionOptions
        {
            DeadSpaceRatioThreshold = 0.70,
            DeadRecordCountThreshold = 100_000,
            MutationCountThreshold = 500_000,
            MinimumFileSizeBytes = 100 * 1024 * 1024,
            BackgroundCheckInterval = TimeSpan.FromHours(6)
        };

        /// <summary>
        /// Manual-only compaction: no automatic triggers. The caller must invoke
        /// <see cref="BitcaskTrunk{T}.Compact"/> explicitly.
        /// </summary>
        public static CompactionOptions Manual => new CompactionOptions
        {
            DisableAutoCompaction = true
        };
    }
}
