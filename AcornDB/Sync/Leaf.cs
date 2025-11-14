using System;
using System.Collections.Generic;

namespace AcornDB.Sync
{
    /// <summary>
    /// A Leaf represents a change event in the tree that propagates through branches.
    /// Contains anti-loop tracking (OriginTreeId, VisitedTrees, HopCount) to prevent
    /// duplicate processing in mesh topologies.
    /// </summary>
    public class Leaf<T>
    {
        /// <summary>
        /// Unique identifier for this leaf: {originTreeId}-{timestamp.Ticks}-{sequence}
        /// </summary>
        public string LeafId { get; set; } = string.Empty;

        /// <summary>
        /// ID of the tree that originally created this change
        /// </summary>
        public string OriginTreeId { get; set; } = string.Empty;

        /// <summary>
        /// Set of tree IDs that have already processed this leaf (loop prevention)
        /// </summary>
        public HashSet<string> VisitedTrees { get; set; } = new();

        /// <summary>
        /// Number of hops this leaf has taken (safety limit to prevent infinite propagation)
        /// </summary>
        public int HopCount { get; set; } = 0;

        /// <summary>
        /// Type of change event
        /// </summary>
        public LeafType Type { get; set; }

        /// <summary>
        /// Key of the nut being changed
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// The nut data (null for delete operations)
        /// </summary>
        public Nut<T>? Data { get; set; }

        /// <summary>
        /// Timestamp when this change occurred
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Check if a tree has already visited/processed this leaf
        /// </summary>
        public bool HasVisited(string treeId)
        {
            return VisitedTrees.Contains(treeId);
        }

        /// <summary>
        /// Mark a tree as having visited this leaf
        /// </summary>
        public void MarkVisited(string treeId)
        {
            VisitedTrees.Add(treeId);
        }

        /// <summary>
        /// Check if this leaf should be dropped (exceeded hop limit)
        /// </summary>
        public bool IsExpired(int maxHops = 10)
        {
            return HopCount >= maxHops;
        }
    }
}
