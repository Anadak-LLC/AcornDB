using System.Text;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    
    public class FileTrunk<T> : ITrunk<T>
    {
        private readonly string _folderPath;

        public FileTrunk(string? customPath = null)
        {
            var typeName = typeof(T).Name;
            _folderPath = customPath ?? Path.Combine(Directory.GetCurrentDirectory(), "data", typeName);
            Directory.CreateDirectory(_folderPath);
        }

        public void Save(string id, Nut<T> nut)
        {
            var file = Path.Combine(_folderPath, id + ".json");
            var json = JsonConvert.SerializeObject(nut, Formatting.Indented);
            File.WriteAllText(file, json, Encoding.UTF8);
        }

        public Nut<T>? Load(string id)
        {
            var file = Path.Combine(_folderPath, id + ".json");
            if (!File.Exists(file)) return null;

            var content = File.ReadAllText(file);
            return JsonConvert.DeserializeObject<Nut<T>>(content);
        }

        public void Delete(string id)
        {
            var file = Path.Combine(_folderPath, id + ".json");
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            var list = new List<Nut<T>>();
            foreach (var file in Directory.GetFiles(_folderPath, "*.json"))
            {
                var content = File.ReadAllText(file);
                var nut = JsonConvert.DeserializeObject<Nut<T>>(content);
                if (nut != null) list.Add(nut);
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
