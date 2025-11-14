namespace AcornDB.Indexing
{
    /// <summary>
    /// Current state of an index
    /// </summary>
    public enum IndexState
    {
        /// <summary>
        /// Index is being built (initial creation or rebuild)
        /// </summary>
        Building,

        /// <summary>
        /// Index is ready for queries
        /// </summary>
        Ready,

        /// <summary>
        /// Index is being verified in background
        /// </summary>
        Verifying,

        /// <summary>
        /// Index has errors and needs rebuild
        /// </summary>
        Error
    }
}
