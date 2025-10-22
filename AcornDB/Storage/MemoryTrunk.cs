using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using AcornDB.Policy;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// High-performance in-memory trunk with lock-free concurrent operations.
    /// Non-durable, no history. Optimized for maximum throughput.
    /// Supports extensible IRoot processors for compression, encryption, policy enforcement, etc.
    ///
    /// Storage Pipeline:
    /// Write: Nut<T> → Serialize → Root Chain (ascending) → byte[] → Store
    /// Read: Retrieve byte[] → Root Chain (descending) → Deserialize → Nut<T>
    /// </summary>
    public class MemoryTrunk<T> : ITrunk<T>
    {
        // ConcurrentDictionary enables lock-free reads and thread-safe writes
        private readonly ConcurrentDictionary<string, byte[]> _storage = new();
        private readonly List<IRoot> _roots = new();
        private readonly object _rootsLock = new();
        private readonly ISerializer _serializer;

        public MemoryTrunk(ISerializer? serializer = null)
        {
            _serializer = serializer ?? new NewtonsoftJsonSerializer();
        }

        public ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = false,
            SupportsAsync = false,
            TrunkType = "MemoryTrunk"
        };

        /// <summary>
        /// Get all registered root processors
        /// </summary>
        public IReadOnlyList<IRoot> Roots
        {
            get
            {
                lock (_rootsLock)
                {
                    return _roots.ToList();
                }
            }
        }

        /// <summary>
        /// Add a root processor to the processing chain
        /// </summary>
        public void AddRoot(IRoot root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            lock (_rootsLock)
            {
                _roots.Add(root);
                // Sort by sequence to ensure correct execution order
                _roots.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            }
        }

        /// <summary>
        /// Remove a root processor from the processing chain
        /// </summary>
        public bool RemoveRoot(string name)
        {
            lock (_rootsLock)
            {
                var root = _roots.FirstOrDefault(r => r.Name == name);
                if (root != null)
                {
                    _roots.Remove(root);
                    return true;
                }
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Save(string id, Nut<T> nut)
        {
            // Step 1: Serialize Nut<T> to JSON then bytes
            var json = _serializer.Serialize(nut);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Step 2: Process through root chain in ascending sequence order
            var context = new RootProcessingContext
            {
                PolicyContext = new PolicyContext { Operation = "Write" },
                DocumentId = id
            };

            var processedBytes = bytes;
            lock (_rootsLock)
            {
                foreach (var root in _roots)
                {
                    processedBytes = root.OnStash(processedBytes, context);
                }
            }

            // Step 3: Store final byte array
            _storage[id] = processedBytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Load(string id)
        {
            // Step 1: Retrieve byte array from storage
            if (!_storage.TryGetValue(id, out var storedBytes))
                return null;

            // Step 2: Process through root chain in descending sequence order (reverse)
            var context = new RootProcessingContext
            {
                PolicyContext = new PolicyContext { Operation = "Read" },
                DocumentId = id
            };

            var processedBytes = storedBytes;
            lock (_rootsLock)
            {
                // Reverse iteration for read path
                for (int i = _roots.Count - 1; i >= 0; i--)
                {
                    processedBytes = _roots[i].OnCrack(processedBytes, context);
                }
            }

            // Step 3: Deserialize bytes back to Nut<T>
            try
            {
                var json = Encoding.UTF8.GetString(processedBytes);
                var nut = _serializer.Deserialize<Nut<T>>(json);
                return nut;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to deserialize nut '{id}': {ex.Message}");
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(string id)
        {
            // Lock-free removal
            _storage.TryRemove(id, out _);
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            // Load all nuts by passing each through the Load pipeline
            foreach (var id in _storage.Keys)
            {
                var nut = Load(id);
                if (nut != null)
                    yield return nut;
            }
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
