using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Scalar index interface - indexes a single property for efficient lookups.
    /// Examples: Email, CustomerId, Price, CreatedDate
    /// </summary>
    public interface IScalarIndex<T, TProperty> : IIndex where T : class
    {
        /// <summary>
        /// Expression that extracts the indexed property from the document
        /// (e.g., user => user.Email)
        /// </summary>
        Expression<Func<T, TProperty>> PropertySelector { get; }

        /// <summary>
        /// Lookup documents by exact value (O(1) for hash index, O(log n) for B-tree)
        /// </summary>
        /// <param name="value">Value to search for</param>
        /// <returns>Collection of document IDs matching the value</returns>
        IEnumerable<string> Lookup(TProperty value);

        /// <summary>
        /// Range query: find all documents where property is between min and max (inclusive)
        /// Only supported for ordered types (numbers, dates, strings)
        /// </summary>
        /// <param name="min">Minimum value (inclusive)</param>
        /// <param name="max">Maximum value (inclusive)</param>
        /// <returns>Collection of document IDs in range, sorted by indexed property</returns>
        IEnumerable<string> Range(TProperty min, TProperty max);

        /// <summary>
        /// Get all document IDs in sorted order by the indexed property.
        /// Useful for ORDER BY queries.
        /// </summary>
        /// <param name="ascending">True for ascending order, false for descending</param>
        /// <returns>Document IDs in sorted order</returns>
        IEnumerable<string> GetAllSorted(bool ascending = true);

        /// <summary>
        /// Get the minimum value in the index
        /// </summary>
        TProperty? GetMin();

        /// <summary>
        /// Get the maximum value in the index
        /// </summary>
        TProperty? GetMax();
    }
}
