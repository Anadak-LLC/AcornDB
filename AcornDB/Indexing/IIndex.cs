using System;
using System.Collections.Generic;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Base interface for all index types in AcornDB.
    /// Indexes provide efficient lookup, sorting, and filtering capabilities
    /// beyond the implicit identity index.
    /// </summary>
    public interface IIndex
    {
        /// <summary>
        /// Unique name for this index (e.g., "IX_User_Email", "IX_Order_CustomerDate")
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Type of index (Scalar, Composite, Computed, Text, TimeSeries, etc.)
        /// </summary>
        IndexType IndexType { get; }

        /// <summary>
        /// Whether this index enforces uniqueness constraint
        /// </summary>
        bool IsUnique { get; }

        /// <summary>
        /// Whether this index is currently built and ready for queries
        /// </summary>
        IndexState State { get; }

        /// <summary>
        /// Build or rebuild the index from scratch.
        /// Called during initial index creation or after corruption.
        /// </summary>
        /// <param name="documents">All documents to index (as Nut objects)</param>
        void Build(IEnumerable<object> documents);

        /// <summary>
        /// Add or update a document in the index.
        /// Called during Stash operations.
        /// </summary>
        /// <param name="id">Document ID</param>
        /// <param name="document">Document to index</param>
        void Add(string id, object document);

        /// <summary>
        /// Remove a document from the index.
        /// Called during Toss operations.
        /// </summary>
        void Remove(string id);

        /// <summary>
        /// Clear all entries from the index
        /// </summary>
        void Clear();

        /// <summary>
        /// Get index statistics for query planning
        /// </summary>
        IndexStatistics GetStatistics();
    }

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

    /// <summary>
    /// Statistics about an index for query planning
    /// </summary>
    public class IndexStatistics
    {
        /// <summary>
        /// Total number of entries in the index
        /// </summary>
        public long EntryCount { get; set; }

        /// <summary>
        /// Number of unique values (for cardinality estimation)
        /// </summary>
        public long UniqueValueCount { get; set; }

        /// <summary>
        /// Average selectivity (0.0 = all same value, 1.0 = all unique)
        /// </summary>
        public double Selectivity => EntryCount > 0 ? (double)UniqueValueCount / EntryCount : 0.0;

        /// <summary>
        /// Approximate memory usage in bytes
        /// </summary>
        public long MemoryUsageBytes { get; set; }

        /// <summary>
        /// Last time the index was updated
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
