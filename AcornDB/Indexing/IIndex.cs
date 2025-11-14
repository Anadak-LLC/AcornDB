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
}
