using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Composite index interface - indexes multiple properties together.
    /// Examples: (CustomerId, OrderDate), (Category, Price), (Country, City, ZipCode)
    ///
    /// Supports prefix matching: an index on (A, B, C) can be used for:
    /// - WHERE A = x
    /// - WHERE A = x AND B = y
    /// - WHERE A = x AND B = y AND C = z
    /// But NOT for: WHERE B = y (missing A)
    /// </summary>
    public interface ICompositeIndex<T> : IIndex where T : class
    {
        /// <summary>
        /// Expression that extracts the composite key from the document
        /// (e.g., order => new { order.CustomerId, order.OrderDate })
        /// </summary>
        LambdaExpression KeySelector { get; }

        /// <summary>
        /// Ordered list of property names in the composite key
        /// </summary>
        IReadOnlyList<string> PropertyNames { get; }

        /// <summary>
        /// Lookup documents by exact composite key match
        /// </summary>
        /// <param name="keyValues">Values for each property in order</param>
        /// <returns>Collection of document IDs matching the composite key</returns>
        IEnumerable<string> Lookup(params object[] keyValues);

        /// <summary>
        /// Prefix lookup: match only the first N properties of the composite key.
        /// Example: For index (CustomerId, OrderDate), can search by CustomerId only.
        /// </summary>
        /// <param name="prefixValues">Values for the prefix properties</param>
        /// <returns>Collection of document IDs matching the prefix</returns>
        IEnumerable<string> PrefixLookup(params object[] prefixValues);

        /// <summary>
        /// Range query on composite key.
        /// Example: CustomerId = "C123" AND OrderDate BETWEEN start AND end
        /// </summary>
        /// <param name="exactPrefixValues">Exact values for prefix properties</param>
        /// <param name="rangePropertyIndex">Index of property to apply range on</param>
        /// <param name="min">Minimum value for range property</param>
        /// <param name="max">Maximum value for range property</param>
        /// <returns>Document IDs matching the composite range query</returns>
        IEnumerable<string> RangeOnLastProperty(object[] exactPrefixValues, object min, object max);

        /// <summary>
        /// Get all document IDs sorted by the composite key
        /// </summary>
        /// <param name="ascending">True for ascending order</param>
        /// <returns>Document IDs in composite key order</returns>
        IEnumerable<string> GetAllSorted(bool ascending = true);
    }
}
