namespace AcornDB.Sync
{
    /// <summary>
    /// Defines the synchronization mode for a Branch
    /// </summary>
    public enum SyncMode
    {
        /// <summary>
        /// Bidirectional sync - both push and pull changes
        /// </summary>
        Bidirectional,

        /// <summary>
        /// Push-only - only send local changes to remote, don't pull remote changes
        /// Useful for write-only replicas or backup scenarios
        /// </summary>
        PushOnly,

        /// <summary>
        /// Pull-only - only receive changes from remote, don't push local changes
        /// Useful for read-only replicas or consuming from authoritative sources
        /// </summary>
        PullOnly,

        /// <summary>
        /// No sync - branch is disabled
        /// </summary>
        Disabled
    }

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
