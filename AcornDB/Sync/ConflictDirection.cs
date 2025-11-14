namespace AcornDB.Sync
{
    /// <summary>
    /// Conflict resolution direction for sync operations
    /// </summary>
    public enum ConflictDirection
    {
        /// <summary>
        /// Use the conflict judge to determine winner (default)
        /// </summary>
        UseJudge,

        /// <summary>
        /// Always prefer local version on conflicts
        /// </summary>
        PreferLocal,

        /// <summary>
        /// Always prefer remote version on conflicts
        /// </summary>
        PreferRemote
    }
}
