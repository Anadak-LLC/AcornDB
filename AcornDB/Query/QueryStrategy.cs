namespace AcornDB.Query
{
    /// <summary>
    /// Strategy for executing a query
    /// </summary>
    public enum QueryStrategy
    {
        /// <summary>
        /// Full table/cache scan (no index)
        /// </summary>
        FullScan,

        /// <summary>
        /// Index lookup (exact match)
        /// </summary>
        IndexSeek,

        /// <summary>
        /// Index range scan
        /// </summary>
        IndexRangeScan,

        /// <summary>
        /// Index scan (use index for ordering only)
        /// </summary>
        IndexScan,

        /// <summary>
        /// Multiple index merge
        /// </summary>
        IndexMerge
    }
}
