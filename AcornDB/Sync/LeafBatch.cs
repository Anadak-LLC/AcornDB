using System;
using System.Collections.Generic;
using System.Linq;

namespace AcornDB.Sync
{
    /// <summary>
    /// A batch of leaves for efficient bulk propagation.
    /// Branches can buffer leaves and send them in batches to reduce network overhead.
    /// </summary>
    public class LeafBatch<T>
    {
        /// <summary>
        /// Unique identifier for this batch
        /// </summary>
        public string BatchId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Leaves in this batch
        /// </summary>
        public List<Leaf<T>> Leaves { get; set; } = new();

        /// <summary>
        /// When this batch was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of leaves in the batch
        /// </summary>
        public int Count => Leaves.Count;

        /// <summary>
        /// Check if the batch is empty
        /// </summary>
        public bool IsEmpty => Leaves.Count == 0;

        /// <summary>
        /// Add a leaf to the batch
        /// </summary>
        public void Add(Leaf<T> leaf)
        {
            Leaves.Add(leaf);
        }

        /// <summary>
        /// Add multiple leaves to the batch
        /// </summary>
        public void AddRange(IEnumerable<Leaf<T>> leaves)
        {
            Leaves.AddRange(leaves);
        }

        /// <summary>
        /// Clear all leaves from the batch
        /// </summary>
        public void Clear()
        {
            Leaves.Clear();
        }

        /// <summary>
        /// Get leaves grouped by type
        /// </summary>
        public Dictionary<LeafType, List<Leaf<T>>> GroupByType()
        {
            return Leaves.GroupBy(l => l.Type)
                        .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// Get size estimate in bytes (for network sizing)
        /// Rough estimate: 200 bytes per leaf base + data size
        /// </summary>
        public long EstimatedSizeBytes => Leaves.Count * 200;
    }
}
