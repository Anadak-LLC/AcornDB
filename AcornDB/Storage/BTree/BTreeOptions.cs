using System;

namespace AcornDB.Storage.BTree
{
    /// <summary>
    /// Configuration options for BTreeTrunk.
    /// Tuned defaults based on design decisions (8KB pages, 256-page cache, WAL-based durability).
    /// </summary>
    public sealed class BTreeOptions
    {
        /// <summary>
        /// Page size in bytes. Must be a power of 2, minimum 4096, maximum 65536.
        /// Default: 8192 (8KB). Stored in superblock as ushort; cannot change after initial creation.
        /// </summary>
        public int PageSize { get; init; } = 8192;

        /// <summary>
        /// Maximum number of pages held in the page cache.
        /// Default: 256 (2MB at 8KB page size). Uses clock eviction. Must be greater than 0.
        /// </summary>
        public int MaxCachePages { get; init; } = 256;

        /// <summary>
        /// Whether to validate page CRCs on every read.
        /// Default: true. Disable only for benchmarking; never disable in production.
        /// </summary>
        public bool ValidateChecksumsOnRead { get; init; } = true;

        /// <summary>
        /// Whether to fsync the WAL after each batch commit.
        /// Default: true. Disabling trades durability for write throughput.
        /// </summary>
        public bool FsyncOnCommit { get; init; } = true;

        /// <summary>
        /// Number of WAL entries before triggering an automatic checkpoint.
        /// Default: 1000. Checkpoint applies WAL to data file and truncates WAL. Must be greater than 0.
        /// </summary>
        public int CheckpointThreshold { get; init; } = 1000;

        /// <summary>
        /// Default options: 8KB pages, 256-page cache, CRC validation on, fsync on commit.
        /// </summary>
        public static readonly BTreeOptions Default = new();

        /// <summary>
        /// Validates all options and throws <see cref="ArgumentException"/> if any are invalid.
        /// </summary>
        public void Validate()
        {
            if (PageSize < 4096 || PageSize > 65536)
                throw new ArgumentException($"PageSize must be between 4096 and 65536, got {PageSize}.", nameof(PageSize));

            if ((PageSize & (PageSize - 1)) != 0)
                throw new ArgumentException($"PageSize must be a power of 2, got {PageSize}.", nameof(PageSize));

            if (MaxCachePages <= 0)
                throw new ArgumentException($"MaxCachePages must be greater than 0, got {MaxCachePages}.", nameof(MaxCachePages));

            if (CheckpointThreshold <= 0)
                throw new ArgumentException($"CheckpointThreshold must be greater than 0, got {CheckpointThreshold}.", nameof(CheckpointThreshold));
        }
    }
}
