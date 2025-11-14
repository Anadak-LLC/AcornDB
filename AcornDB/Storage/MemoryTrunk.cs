using System.Collections.Concurrent;
using AcornDB.Logging;
using System.Runtime.CompilerServices;
using System.Text;
using AcornDB.Policy;
using AcornDB.Storage.Serialization;
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
    public class MemoryTrunk<T> : TrunkBase<T> where T : class
    {
        // ConcurrentDictionary enables lock-free reads and thread-safe writes
        private readonly ConcurrentDictionary<string, byte[]> _storage = new();

        public MemoryTrunk(ISerializer? serializer = null)
            : base(serializer)
        {
        }

        public override ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = false,
            SupportsAsync = false,
            TrunkType = "MemoryTrunk"
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Stash(string id, Nut<T> nut)
        {
            // Step 1: Serialize Nut<T> to JSON then bytes
            var json = _serializer.Serialize(nut);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Step 2: Process through root chain in ascending sequence order
            var processedBytes = ProcessThroughRootsAscending(bytes, id);

            // Step 3: Store final byte array
            _storage[id] = processedBytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Nut<T>? Crack(string id)
        {
            // Step 1: Retrieve byte array from storage
            if (!_storage.TryGetValue(id, out var storedBytes))
                return null;

            // Step 2: Process through root chain in descending sequence order (reverse)
            var processedBytes = ProcessThroughRootsDescending(storedBytes, id);

            // Step 3: Deserialize bytes back to Nut<T>
            try
            {
                var json = Encoding.UTF8.GetString(processedBytes);
                var nut = _serializer.Deserialize<Nut<T>>(json);
                return nut;
            }
            catch (Exception ex)
            {
                AcornLog.Info($"⚠️ Failed to deserialize nut '{id}': {ex.Message}");
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Toss(string id)
        {
            // Lock-free removal
            _storage.TryRemove(id, out _);
        }

        public override IEnumerable<Nut<T>> CrackAll()
        {
            // Load all nuts by passing each through the Crack pipeline
            foreach (var id in _storage.Keys)
            {
                var nut = Crack(id);
                if (nut != null)
                    yield return nut;
            }
        }

        // Optional features - not supported by MemoryTrunk
        public override IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("MemoryTrunk does not support history.");
        }

        public override IEnumerable<Nut<T>> ExportChanges()
        {
            return CrackAll();
        }

        public override void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            foreach (var nut in incoming)
            {
                Stash(nut.Id, nut);
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
