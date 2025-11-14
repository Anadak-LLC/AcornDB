using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Computed index interface - indexes a computed/derived value from document properties.
    /// Examples:
    /// - FullName = FirstName + " " + LastName
    /// - FullNameLower = (FirstName + " " + LastName).ToLower()
    /// - YearMonth = new { CreatedDate.Year, CreatedDate.Month }
    ///
    /// Only supports a safe subset of expressions (validated by IndexExpressionValidator).
    /// </summary>
    public interface IComputedIndex<T, TResult> : IIndex where T : class
    {
        /// <summary>
        /// Expression that computes the indexed value from the document.
        /// Must contain only safe operations (property access, concat, ToLower, etc.)
        /// </summary>
        Expression<Func<T, TResult>> ComputeExpression { get; }

        /// <summary>
        /// Compiled version of ComputeExpression for fast evaluation
        /// </summary>
        Func<T, TResult> ComputeFunc { get; }

        /// <summary>
        /// Lookup documents by exact computed value
        /// </summary>
        /// <param name="value">Computed value to search for</param>
        /// <returns>Collection of document IDs where computed value matches</returns>
        IEnumerable<string> Lookup(TResult value);

        /// <summary>
        /// Range query on computed value (if TResult is orderable)
        /// </summary>
        /// <param name="min">Minimum value (inclusive)</param>
        /// <param name="max">Maximum value (inclusive)</param>
        /// <returns>Document IDs where computed value is in range</returns>
        IEnumerable<string> Range(TResult min, TResult max);

        /// <summary>
        /// Get all document IDs sorted by computed value
        /// </summary>
        /// <param name="ascending">True for ascending order</param>
        /// <returns>Document IDs sorted by computed value</returns>
        IEnumerable<string> GetAllSorted(bool ascending = true);
    }
}
