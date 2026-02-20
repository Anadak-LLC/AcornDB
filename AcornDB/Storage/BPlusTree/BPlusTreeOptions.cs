namespace AcornDB.Storage.BPlusTree
{
    /// <summary>
    /// Configuration options for BPlusTreeTrunk.
    /// Tuned defaults based on design decisions (8KB pages, 256-page cache, WAL-based durability).
    /// </summary>
    public sealed class BPlusTreeOptions
    {
        /// <summary>
        /// Page size in bytes. Must be a power of 2, minimum 4096.
        /// Default: 8192 (8KB). Stored in superblock; cannot change after initial creation.
        /// </summary>
        public int PageSize { get; init; } = 8192;

        /// <summary>
        /// Maximum number of pages held in the page cache.
        /// Default: 256 (2MB at 8KB page size). Uses clock eviction.
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
        /// Default: 1000. Checkpoint applies WAL to data file and truncates WAL.
        /// </summary>
        public int CheckpointThreshold { get; init; } = 1000;

        /// <summary>
        /// Default options: 8KB pages, 256-page cache, CRC validation on, fsync on commit.
        /// </summary>
        public static readonly BPlusTreeOptions Default = new();
    }
}
