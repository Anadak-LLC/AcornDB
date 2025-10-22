using System.Runtime.CompilerServices;
using System.Text;
using AcornDB.Policy;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// Simple file-per-document trunk implementation.
    /// NOTE: This architecture is inherently slow (2000-3000x slower than BTreeTrunk).
    /// For performance-critical applications, use BTreeTrunk instead.
    /// Supports extensible IRoot processors for compression, encryption, policy enforcement, etc.
    ///
    /// Storage Pipeline:
    /// Write: Nut<T> → Serialize → Root Chain (ascending) → byte[] → Write to file
    /// Read: Read file → byte[] → Root Chain (descending) → Deserialize → Nut<T>
    /// </summary>
    public class FileTrunk<T> : ITrunk<T>
    {
        private readonly string _folderPath;
        private readonly JsonSerializerSettings _jsonSettings;
        private readonly List<IRoot> _roots = new();
        private readonly object _rootsLock = new();
        private readonly ISerializer _serializer;

        public ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = false,
            TrunkType = "FileTrunk"
        };

        public FileTrunk(string? customPath = null, ISerializer? serializer = null)
        {
            var typeName = typeof(T).Name;
            _folderPath = customPath ?? Path.Combine(Directory.GetCurrentDirectory(), "data", typeName);
            Directory.CreateDirectory(_folderPath);

            _serializer = serializer ?? new NewtonsoftJsonSerializer();

            // Optimize JSON serialization
            _jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.None, // Remove indentation to reduce file size and I/O
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore
            };
        }

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
        private string GetFilePath(string id)
        {
            // Cache common operation to reduce string allocations
            return Path.Combine(_folderPath, id + ".json");
        }

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

            // Step 3: Write final byte array to file
            var file = GetFilePath(id);
            using (var stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
            {
                stream.Write(processedBytes, 0, processedBytes.Length);
                stream.Flush(flushToDisk: true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Load(string id)
        {
            // Step 1: Read byte array from file
            var file = GetFilePath(id);
            if (!File.Exists(file)) return null;

            byte[] storedBytes;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
            {
                storedBytes = new byte[stream.Length];
                stream.Read(storedBytes, 0, storedBytes.Length);
            }

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
            var file = GetFilePath(id);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            // Load all nuts by passing each through the Load pipeline
            var files = Directory.GetFiles(_folderPath, "*.json");
            var list = new List<Nut<T>>(files.Length); // Pre-allocate capacity

            foreach (var file in files)
            {
                var id = Path.GetFileNameWithoutExtension(file);
                var nut = Load(id);
                if (nut != null)
                {
                    list.Add(nut);
                }
            }
            return list;
        }

        // Optional features - not supported by FileTrunk
        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("FileTrunk does not support history. Use DocumentStoreTrunk for versioning.");
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            // Simple implementation: export all current data
            return LoadAll();
        }

        public void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            foreach (var nut in incoming)
            {
                Save(nut.Id, nut);
            }
        }
    }
}
