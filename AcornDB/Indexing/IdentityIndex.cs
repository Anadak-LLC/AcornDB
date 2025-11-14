using System;
using System.Collections.Generic;
using System.Linq;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Special index that wraps the implicit identity (primary key) index.
    /// Every Tree has an identity index based on document IDs - this exposes it through IIndex.
    /// Always unique, always present, read-only.
    /// </summary>
    /// <typeparam name="T">Document type</typeparam>
    public class IdentityIndex<T> : IIndex where T : class
    {
        private readonly Func<IDictionary<string, Nut<T>>> _cacheAccessor;
        private readonly string _name;

        public string Name => _name;
        public IndexType IndexType => IndexType.Identity;
        public bool IsUnique => true; // Identity indexes are always unique
        public IndexState State => IndexState.Ready; // Always ready since it's the cache itself

        /// <summary>
        /// Create an identity index wrapper around the tree's cache
        /// </summary>
        /// <param name="cacheAccessor">Function to access the tree's cache dictionary</param>
        /// <param name="name">Optional custom name (defaults to "IX_Identity")</param>
        public IdentityIndex(Func<IDictionary<string, Nut<T>>> cacheAccessor, string? name = null)
        {
            _cacheAccessor = cacheAccessor ?? throw new ArgumentNullException(nameof(cacheAccessor));
            _name = name ?? "IX_Identity";
        }

        public void Build(IEnumerable<object> documents)
        {
            // No-op: Identity index is always synchronized with cache
            // The cache IS the identity index
        }

        public void Add(string id, object document)
        {
            // No-op: Identity index is maintained by Tree.Stash()
            // No need to do anything here
        }

        public void Remove(string id)
        {
            // No-op: Identity index is maintained by Tree.Toss()
            // No need to do anything here
        }

        public void Clear()
        {
            // No-op: Cannot clear identity index
            // The cache IS the identity index
        }

        public IndexStatistics GetStatistics()
        {
            var cache = _cacheAccessor();
            return new IndexStatistics
            {
                EntryCount = cache.Count,
                UniqueValueCount = cache.Count, // Every ID is unique
                MemoryUsageBytes = cache.Count * 64, // Rough estimate
                LastUpdated = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Lookup a document by its ID
        /// </summary>
        /// <param name="id">Document ID</param>
        /// <returns>Single ID if found, empty if not found</returns>
        public IEnumerable<string> Lookup(string id)
        {
            var cache = _cacheAccessor();
            if (cache.ContainsKey(id))
            {
                return new[] { id };
            }
            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Check if a document exists by ID
        /// </summary>
        public bool Exists(string id)
        {
            var cache = _cacheAccessor();
            return cache.ContainsKey(id);
        }

        /// <summary>
        /// Get all document IDs
        /// </summary>
        public IEnumerable<string> GetAllIds()
        {
            var cache = _cacheAccessor();
            return cache.Keys.ToList();
        }

        /// <summary>
        /// Get count of documents
        /// </summary>
        public int Count()
        {
            var cache = _cacheAccessor();
            return cache.Count;
        }
    }

    /// <summary>
    /// Extension methods for working with identity indexes
    /// </summary>
    public static class IdentityIndexExtensions
    {
        /// <summary>
        /// Get the identity index for a tree (if exposed)
        /// </summary>
        public static IdentityIndex<T>? GetIdentityIndex<T>(this Tree<T> tree) where T : class
        {
            var index = tree.GetIndex("IX_Identity");
            return index as IdentityIndex<T>;
        }
    }
}
