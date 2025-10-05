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

        public void Save(string id, NutShell<T> shell)
        {
            var file = Path.Combine(_folderPath, id + ".json");
            var json = JsonConvert.SerializeObject(shell, Formatting.Indented);
            File.WriteAllText(file, json, Encoding.UTF8);
        }

        public NutShell<T>? Load(string id)
        {
            var file = Path.Combine(_folderPath, id + ".json");
            if (!File.Exists(file)) return null;

            var content = File.ReadAllText(file);
            return JsonConvert.DeserializeObject<NutShell<T>>(content);
        }

        public void Delete(string id)
        {
            var file = Path.Combine(_folderPath, id + ".json");
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        public IEnumerable<NutShell<T>> LoadAll()
        {
            var list = new List<NutShell<T>>();
            foreach (var file in Directory.GetFiles(_folderPath, "*.json"))
            {
                var content = File.ReadAllText(file);
                var shell = JsonConvert.DeserializeObject<NutShell<T>>(content);
                if (shell != null) list.Add(shell);
            }
            return list;
        }
    }
}
