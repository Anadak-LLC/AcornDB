using System.Runtime.CompilerServices;
using AcornDB.Logging;
using System.Text;
using AcornDB.Policy;
using AcornDB.Storage.Serialization;
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
    public class FileTrunk<T> : TrunkBase<T> where T : class
    {
        private readonly string _folderPath;
        private readonly JsonSerializerSettings _jsonSettings;

        public override ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = false,
            TrunkType = "FileTrunk"
        };

        public FileTrunk(string? customPath = null, ISerializer? serializer = null)
            : base(serializer)
        {
            var typeName = typeof(T).Name;
            _folderPath = customPath ?? Path.Combine(Directory.GetCurrentDirectory(), "data", typeName);
            Directory.CreateDirectory(_folderPath);

            // Optimize JSON serialization
            _jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.None, // Remove indentation to reduce file size and I/O
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetFilePath(string id)
        {
            // Cache common operation to reduce string allocations
            return Path.Combine(_folderPath, id + ".json");
        }

        public override void Stash(string id, Nut<T> nut)
        {
            // Step 1: Serialize Nut<T> to JSON then bytes
            var json = _serializer.Serialize(nut);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Step 2: Process through root chain in ascending sequence order
            var processedBytes = ProcessThroughRootsAscending(bytes, id);

            // Step 3: Write final byte array to file
            var file = GetFilePath(id);
            using (var stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
            {
                stream.Write(processedBytes, 0, processedBytes.Length);
                stream.Flush(flushToDisk: true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Nut<T>? Crack(string id)
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
            var file = GetFilePath(id);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        public override IEnumerable<Nut<T>> CrackAll()
        {
            // Load all nuts by passing each through the Load pipeline
            var files = Directory.GetFiles(_folderPath, "*.json");
            var list = new List<Nut<T>>(files.Length); // Pre-allocate capacity

            foreach (var file in files)
            {
                var id = Path.GetFileNameWithoutExtension(file);
                var nut = Crack(id);
                if (nut != null)
                {
                    list.Add(nut);
                }
            }
            return list;
        }

        // Optional features - not supported by FileTrunk
        public override IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("FileTrunk does not support history. Use DocumentStoreTrunk for versioning.");
        }

        public override IEnumerable<Nut<T>> ExportChanges()
        {
            // Simple implementation: export all current data
            return CrackAll();
        }

        public override void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            foreach (var nut in incoming)
            {
                Stash(nut.Id, nut);
            }
        }
    }
}
