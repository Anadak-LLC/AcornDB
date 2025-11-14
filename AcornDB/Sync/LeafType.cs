namespace AcornDB.Sync
{
    /// <summary>
    /// Type of change event represented by a Leaf
    /// </summary>
    public enum LeafType
    {
        /// <summary>
        /// A new nut was stashed
        /// </summary>
        Stash,

        /// <summary>
        /// A nut was tossed (deleted)
        /// </summary>
        Toss,

        /// <summary>
        /// A conflict was resolved (squabble)
        /// </summary>
        Squabble,

        /// <summary>
        /// An existing nut was updated
        /// </summary>
        Update
    }
}
