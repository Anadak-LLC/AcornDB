namespace AcornDB.Storage
{
    /// <summary>
    /// Cache write strategies
    /// </summary>
    public enum CacheWriteStrategy
    {
        /// <summary>
        /// Write to backing store, then update caches
        /// Best for: Read-heavy workloads
        /// </summary>
        WriteThrough,

        /// <summary>
        /// Write to backing store, invalidate caches
        /// Best for: Strong consistency requirements
        /// </summary>
        Invalidate,

        /// <summary>
        /// Write to backing store only, don't touch caches
        /// Best for: Write-heavy workloads, data written once and read rarely
        /// </summary>
        WriteAround
    }
}
