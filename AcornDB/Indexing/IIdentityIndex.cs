using System;
using System.Collections.Generic;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Identity index interface - always present, provides primary key lookup.
    /// Wraps the existing Dictionary-based ID lookup with index semantics.
    /// Cannot be removed or disabled.
    /// </summary>
    public interface IIdentityIndex<T> : IIndex where T : class
    {
        /// <summary>
        /// Lookup a document by its ID (O(1) operation)
        /// </summary>
        /// <param name="id">Document ID</param>
        /// <returns>Nut wrapper containing the document, or null if not found</returns>
        Nut<T>? Lookup(string id);

        /// <summary>
        /// Check if a document with this ID exists
        /// </summary>
        /// <param name="id">Document ID</param>
        /// <returns>True if document exists</returns>
        bool Contains(string id);

        /// <summary>
        /// Get all document IDs in the index
        /// </summary>
        IEnumerable<string> GetAllIds();

        /// <summary>
        /// Get all documents in the index
        /// </summary>
        IEnumerable<Nut<T>> GetAll();
    }
}
