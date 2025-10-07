using System.Collections.Generic;
using System.Linq;

namespace AcornDB.Cache
{
    /// <summary>
    /// Cache strategy that never evicts items (unlimited cache)
    /// </summary>
    public class NoEvictionStrategy<T> : ICacheStrategy<T>
    {
        public void OnStash(string id, Nut<T> nut) { }

        public void OnCrack(string id) { }

        public void OnToss(string id) { }

        public IEnumerable<string> GetEvictionCandidates(IDictionary<string, Nut<T>> currentCache)
        {
            return Enumerable.Empty<string>();
        }

        public void Reset() { }
    }
}
