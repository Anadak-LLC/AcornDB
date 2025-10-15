using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// Simple file-per-document trunk implementation.
    /// NOTE: This architecture is inherently slow (2000-3000x slower than BTreeTrunk).
    /// For performance-critical applications, use BTreeTrunk instead.
    /// </summary>
    public class FileTrunk<T> : ITrunk<T>
    {
        private readonly string _folderPath;
        private readonly JsonSerializerSettings _jsonSettings;

        public FileTrunk(string? customPath = null)
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

        public void Save(string id, Nut<T> nut)
        {
            var file = GetFilePath(id);
            var json = JsonConvert.SerializeObject(nut, _jsonSettings);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Use buffered FileStream for better I/O performance
            using (var stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Load(string id)
        {
            var file = GetFilePath(id);
            if (!File.Exists(file)) return null;

            // Use buffered FileStream with ArrayPool for better performance
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
            {
                var buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);
                try
                {
                    var bytesRead = stream.Read(buffer, 0, (int)stream.Length);
                    var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    return JsonConvert.DeserializeObject<Nut<T>>(json, _jsonSettings);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
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
            var files = Directory.GetFiles(_folderPath, "*.json");
            var list = new List<Nut<T>>(files.Length); // Pre-allocate capacity

            foreach (var file in files)
            {
                // Use optimized buffered I/O
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                {
                    var buffer = ArrayPool<byte>.Shared.Rent((int)stream.Length);
                    try
                    {
                        var bytesRead = stream.Read(buffer, 0, (int)stream.Length);
                        var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        var nut = JsonConvert.DeserializeObject<Nut<T>>(json, _jsonSettings);
                        if (nut != null) list.Add(nut);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
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
