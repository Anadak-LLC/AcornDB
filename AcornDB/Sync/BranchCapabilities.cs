using System;

namespace AcornDB.Sync
{
    /// <summary>
    /// Capabilities that a branch can support
    /// </summary>
    [Flags]
    public enum BranchCapabilities
    {
        /// <summary>
        /// No capabilities
        /// </summary>
        None = 0,

        /// <summary>
        /// Can handle stash operations
        /// </summary>
        Stash = 1,

        /// <summary>
        /// Can handle toss (delete) operations
        /// </summary>
        Toss = 2,

        /// <summary>
        /// Can handle squabble (conflict resolution) operations
        /// </summary>
        Squabble = 4,

        /// <summary>
        /// Can handle shake (sync) operations
        /// </summary>
        Shake = 8,

        /// <summary>
        /// Supports batching leaves for efficient bulk operations
        /// </summary>
        Batching = 16,

        /// <summary>
        /// All capabilities
        /// </summary>
        All = Stash | Toss | Squabble | Shake
    }

    /// <summary>
    /// Extension methods for BranchCapabilities
    /// </summary>
    public static class BranchCapabilitiesExtensions
    {
        /// <summary>
        /// Check if a capability is supported
        /// </summary>
        public static bool Supports(this BranchCapabilities capabilities, BranchCapabilities capability)
        {
            return (capabilities & capability) == capability;
        }
    }
}
