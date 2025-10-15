using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace AcornDB.Storage
{
    /// <summary>
    /// High-performance in-memory trunk with lock-free concurrent operations.
    /// Non-durable, no history. Optimized for maximum throughput.
    /// </summary>
    public class MemoryTrunk<T> : ITrunk<T>
    {
        // ConcurrentDictionary enables lock-free reads and thread-safe writes
        private readonly ConcurrentDictionary<string, Nut<T>> _storage = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Save(string id, Nut<T> nut)
        {
            // Lock-free write with ConcurrentDictionary
            _storage[id] = nut;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Load(string id)
        {
            // Lock-free read - perfect scalability under concurrent load
            return _storage.TryGetValue(id, out var nut) ? nut : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(string id)
        {
            // Lock-free removal
            _storage.TryRemove(id, out _);
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            // Return values directly - ConcurrentDictionary.Values is already thread-safe
            // No need for ToList() which creates unnecessary allocations
            return _storage.Values;
        }

        // Optional features - not supported by MemoryTrunk
        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("MemoryTrunk does not support history.");
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return LoadAll();
        }

        public void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            foreach (var nut in incoming)
            {
                Save(nut.Id, nut);
            }
        }

        /// <summary>
        /// Get count of stored items (lock-free)
        /// </summary>
        public int Count => _storage.Count;

        /// <summary>
        /// Clear all stored items (lock-free)
        /// </summary>
        public void Clear()
        {
            _storage.Clear();
        }
    }
}