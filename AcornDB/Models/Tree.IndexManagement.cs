using System;
using System.Collections.Generic;
using System.Linq;
using AcornDB.Indexing;
using AcornDB.Logging;

namespace AcornDB
{
    public partial class Tree<T> where T : class
    {
        // Index collection - stores all registered indexes
        private readonly Dictionary<string, IIndex> _indexes = new Dictionary<string, IIndex>();
        private readonly object _indexLock = new object();
        private object? _identityIndex; // Stored as object to avoid generic constraint issues

        /// <summary>
        /// Initialize the identity index wrapper (called internally during tree construction)
        /// </summary>
        private void InitializeIdentityIndex()
        {
            // Only create identity index if T is a reference type
            if (!typeof(T).IsClass) return;

            lock (_indexLock)
            {
                // Use dynamic to bypass generic constraint at compile time
                dynamic identityIndex = Activator.CreateInstance(
                    typeof(IdentityIndex<>).MakeGenericType(typeof(T)),
                    new Func<IDictionary<string, Nut<T>>>(() => _cache),
                    "IX_Identity"
                )!;

                _identityIndex = identityIndex;
                _indexes["IX_Identity"] = (IIndex)identityIndex;
            }
        }

        /// <summary>
        /// Add an index to the tree. The index will be built from existing data.
        /// Called by Acorn.Sprout() during tree initialization.
        /// </summary>
        /// <param name="index">Index to add</param>
        public void AddIndex(IIndex index)
        {
            lock (_indexLock)
            {
                if (_indexes.ContainsKey(index.Name))
                {
                    throw new InvalidOperationException($"Index with name '{index.Name}' already exists");
                }

                _indexes[index.Name] = index;

                // Build the index from existing cached data
                var nuts = _cache.Values.Cast<object>();
                index.Build(nuts);
            }
        }

        /// <summary>
        /// Remove an index from the tree
        /// </summary>
        /// <param name="indexName">Name of the index to remove</param>
        /// <returns>True if index was found and removed</returns>
        public bool RemoveIndex(string indexName)
        {
            lock (_indexLock)
            {
                if (_indexes.TryGetValue(indexName, out var index))
                {
                    index.Clear();
                    return _indexes.Remove(indexName);
                }
                return false;
            }
        }

        /// <summary>
        /// Get an index by name
        /// </summary>
        /// <param name="indexName">Index name</param>
        /// <returns>Index if found, null otherwise</returns>
        public IIndex? GetIndex(string indexName)
        {
            lock (_indexLock)
            {
                return _indexes.TryGetValue(indexName, out var index) ? index : null;
            }
        }

        /// <summary>
        /// Get a typed scalar index by name
        /// </summary>
        public IScalarIndex<TDoc, TProperty>? GetScalarIndex<TDoc, TProperty>(string indexName) where TDoc : class
        {
            var index = GetIndex(indexName);
            return index as IScalarIndex<TDoc, TProperty>;
        }

        /// <summary>
        /// Get all indexes registered on this tree
        /// </summary>
        public IReadOnlyList<IIndex> GetAllIndexes()
        {
            lock (_indexLock)
            {
                return _indexes.Values.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Get statistics for all indexes
        /// </summary>
        public Dictionary<string, IndexStatistics> GetIndexStatistics()
        {
            lock (_indexLock)
            {
                return _indexes.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.GetStatistics());
            }
        }

        /// <summary>
        /// Update all indexes when a document is stashed.
        /// Called internally by Stash operations.
        /// </summary>
        private void UpdateIndexesOnStash(string id, T document)
        {
            lock (_indexLock)
            {
                foreach (var index in _indexes.Values)
                {
                    try
                    {
                        index.Add(id, document!);
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("Unique index violation"))
                    {
                        // Re-throw unique constraint violations - these are user errors
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Log other index update failures but don't break the stash operation
                        AcornLog.Error($"⚠️ Failed to update index '{index.Name}' during stash: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Update all indexes when a document is removed.
        /// Called internally by Toss operations.
        /// </summary>
        private void UpdateIndexesOnToss(string id)
        {
            lock (_indexLock)
            {
                foreach (var index in _indexes.Values)
                {
                    try
                    {
                        index.Remove(id);
                    }
                    catch (Exception ex)
                    {
                        // Log index update failure but don't break the toss operation
                        AcornLog.Error($"⚠️ Failed to update index '{index.Name}' during toss: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Rebuild all indexes from scratch.
        /// Useful after bulk operations or index corruption.
        /// </summary>
        public void RebuildAllIndexes()
        {
            lock (_indexLock)
            {
                var nuts = _cache.Values.Cast<object>();

                foreach (var index in _indexes.Values)
                {
                    try
                    {
                        index.Build(nuts);
                    }
                    catch (Exception ex)
                    {
                        AcornLog.Error($"⚠️ Failed to rebuild index '{index.Name}': {ex.Message}");
                    }
                }
            }
        }
    }
}
