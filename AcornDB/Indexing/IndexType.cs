namespace AcornDB.Indexing
{
    /// <summary>
    /// Type of index implementation
    /// </summary>
    public enum IndexType
    {
        /// <summary>
        /// Primary key / identity index (always present, unique)
        /// </summary>
        Identity,

        /// <summary>
        /// Single property index (e.g., Email, CustomerId)
        /// </summary>
        Scalar,

        /// <summary>
        /// Multiple property index (e.g., CustomerId + OrderDate)
        /// </summary>
        Composite,

        /// <summary>
        /// Computed/expression index (e.g., FirstName + LastName)
        /// </summary>
        Computed,

        /// <summary>
        /// Full-text search index
        /// </summary>
        Text,

        /// <summary>
        /// Time-series index with bucketing
        /// </summary>
        TimeSeries
    }
}
