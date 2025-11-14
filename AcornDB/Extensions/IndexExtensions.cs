using System;
using System.Linq.Expressions;
using AcornDB.Indexing;
using AcornDB.Models;

namespace AcornDB
{
    /// <summary>
    /// Extension methods for adding indexes to Acorn builder and Trees.
    /// Note: Advanced index methods (composite, computed, text, time-series, TTL) are experimental
    /// and not yet implemented. They will throw NotImplementedException until v0.6.0+.
    /// Only WithIndex (scalar indexes) is production-ready.
    /// </summary>
    [Experimental("Advanced index types are planned for v0.6.0+. Only scalar indexes (WithIndex) are currently implemented.", "v0.6.0")]
    public static class IndexExtensions
    {
        /// <summary>
        /// Add a scalar index on a single property.
        /// Example: tree.WithIndex(u => u.Email, cfg => cfg.Unique().WithCaseInsensitiveComparison())
        /// </summary>
        /// <typeparam name="T">Document type</typeparam>
        /// <typeparam name="TProperty">Property type to index</typeparam>
        /// <param name="acorn">Acorn builder</param>
        /// <param name="propertySelector">Expression selecting the property to index</param>
        /// <param name="configure">Optional configuration for the index</param>
        /// <returns>Acorn builder for chaining</returns>
        public static Acorn<T> WithIndex<T, TProperty>(
            this Acorn<T> acorn,
            Expression<Func<T, TProperty>> propertySelector,
            Action<IndexConfiguration>? configure = null) where T : class
        {
            var config = new IndexConfiguration();
            configure?.Invoke(config);

            var index = new ManagedScalarIndex<T, TProperty>(propertySelector, config);
            acorn.AddIndex(index);

            return acorn;
        }

        /// <summary>
        /// Add a composite index on multiple properties.
        /// Example: tree.WithIndex(o => new { o.CustomerId, o.OrderDate })
        /// </summary>
        /// <typeparam name="T">Document type</typeparam>
        /// <param name="acorn">Acorn builder</param>
        /// <param name="keySelector">Expression creating composite key from multiple properties</param>
        /// <param name="configure">Optional configuration for the index</param>
        /// <returns>Acorn builder for chaining</returns>
        /// <exception cref="NotImplementedException">This feature is not yet implemented. Planned for v0.6.0 (Phase 4.1)</exception>
        public static Acorn<T> WithCompositeIndex<T>(
            this Acorn<T> acorn,
            Expression<Func<T, object>> keySelector,
            Action<IndexConfiguration>? configure = null) where T : class
        {
            var config = new IndexConfiguration();
            configure?.Invoke(config);

            // Composite index implementation will come in 0.6
            // For now, register the intent
            throw new NotImplementedException("Composite indexes will be implemented in 0.6");
        }

        /// <summary>
        /// Add a computed/expression index.
        /// Example: tree.WithComputedIndex(u => (u.FirstName + " " + u.LastName).ToLower())
        /// </summary>
        /// <typeparam name="T">Document type</typeparam>
        /// <typeparam name="TResult">Result type of computed expression</typeparam>
        /// <param name="acorn">Acorn builder</param>
        /// <param name="computeExpression">Expression computing the indexed value</param>
        /// <param name="configure">Optional configuration for the index</param>
        /// <returns>Acorn builder for chaining</returns>
        /// <exception cref="NotImplementedException">This feature is not yet implemented. Planned for v0.6.0 (Phase 4.2)</exception>
        public static Acorn<T> WithComputedIndex<T, TResult>(
            this Acorn<T> acorn,
            Expression<Func<T, TResult>> computeExpression,
            Action<IndexConfiguration>? configure = null) where T : class
        {
            var config = new IndexConfiguration();
            configure?.Invoke(config);

            // Computed index implementation will come in 0.6
            throw new NotImplementedException("Computed indexes will be implemented in 0.6");
        }

        /// <summary>
        /// Add a full-text search index on a text property.
        /// Example: tree.WithTextIndex(p => p.Description, cfg => cfg.WithLanguage("english"))
        /// </summary>
        /// <typeparam name="T">Document type</typeparam>
        /// <param name="acorn">Acorn builder</param>
        /// <param name="textSelector">Expression selecting the text property to index</param>
        /// <param name="configure">Optional configuration for the index</param>
        /// <returns>Acorn builder for chaining</returns>
        /// <exception cref="NotImplementedException">This feature is not yet implemented. Planned for v0.6.0 (Phase 4.3-4.4)</exception>
        public static Acorn<T> WithTextIndex<T>(
            this Acorn<T> acorn,
            Expression<Func<T, string>> textSelector,
            Action<IndexConfiguration>? configure = null) where T : class
        {
            var config = new IndexConfiguration();
            configure?.Invoke(config);

            // Text index implementation will come in 0.6
            throw new NotImplementedException("Text indexes will be implemented in 0.6");
        }

        /// <summary>
        /// Add a time-series index with bucketing.
        /// Example: tree.WithTimeSeries(e => e.Timestamp, cfg => cfg.BucketHours(1))
        /// </summary>
        /// <typeparam name="T">Document type</typeparam>
        /// <param name="acorn">Acorn builder</param>
        /// <param name="timestampSelector">Expression selecting the timestamp property</param>
        /// <param name="configure">Configuration for bucket size and aggregation</param>
        /// <returns>Acorn builder for chaining</returns>
        /// <exception cref="NotImplementedException">This feature is not yet implemented. Planned for v0.6.0 (Phase 4.5)</exception>
        public static Acorn<T> WithTimeSeries<T>(
            this Acorn<T> acorn,
            Expression<Func<T, DateTime>> timestampSelector,
            Action<IndexConfiguration> configure) where T : class
        {
            var config = new IndexConfiguration();
            configure(config);

            // Time-series index implementation will come in 0.6
            throw new NotImplementedException("Time-series indexes will be implemented in 0.6");
        }

        /// <summary>
        /// Configure TTL (time-to-live) with index optimization.
        /// Example: tree.WithTtl(u => u.ExpiresAt, cfg => cfg.CleanupInterval(TimeSpan.FromMinutes(5)))
        /// </summary>
        /// <typeparam name="T">Document type</typeparam>
        /// <param name="acorn">Acorn builder</param>
        /// <param name="expirationSelector">Expression selecting the expiration timestamp property</param>
        /// <param name="configure">Optional configuration for TTL behavior</param>
        /// <returns>Acorn builder for chaining</returns>
        /// <exception cref="NotImplementedException">This feature is not yet implemented. Planned for v0.6.0 (Phase 4.6)</exception>
        public static Acorn<T> WithTtl<T>(
            this Acorn<T> acorn,
            Expression<Func<T, DateTime>> expirationSelector,
            Action<IndexConfiguration>? configure = null) where T : class
        {
            var config = new IndexConfiguration();
            configure?.Invoke(config);

            // TTL index optimization will come in 0.6
            throw new NotImplementedException("TTL index optimization will be implemented in 0.6");
        }
    }
}
