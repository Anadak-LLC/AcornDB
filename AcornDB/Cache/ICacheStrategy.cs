using System.Collections.Generic;

namespace AcornDB.Cache
{
    /// <summary>
    /// Interface for cache eviction strategies
    /// </summary>
    public interface ICacheStrategy<T>
    {
        /// <summary>
        /// Called when an item is added to the cache
        /// </summary>
        void OnStash(string id, Nut<T> nut);

        /// <summary>
        /// Called when an item is accessed from the cache
        /// </summary>
        void OnCrack(string id);

        /// <summary>
        /// Called when an item is removed from the cache
        /// </summary>
        void OnToss(string id);

        /// <summary>
        /// Determine which items should be evicted based on the strategy
        /// </summary>
        /// <param name="currentCache">Current cache contents</param>
        /// <returns>List of IDs to evict</returns>
        IEnumerable<string> GetEvictionCandidates(IDictionary<string, Nut<T>> currentCache);

        /// <summary>
        /// Reset the strategy state
        /// </summary>
        void Reset();
    }
}
