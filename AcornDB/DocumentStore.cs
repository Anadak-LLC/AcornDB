using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    public class DocumentStore<T>
    {
        private readonly string _path;
        private readonly string _collectionName;
        private readonly ISerializer _serializer;
        private Dictionary<int, T> _data;

        public DocumentStore(string filePath, string collectionName, ISerializer serializer)
        {
            _path = filePath;
            _collectionName = collectionName;
            _serializer = serializer;
            _data = Load() ?? new Dictionary<int, T>();
        }

        public void Insert(T doc, TimeSpan? ttl = null)
        {
            var id = _data.Count + 1;
            _data[id] = doc;
            Save();
        }

        public void Update(int id, T doc)
        {
            _data[id] = doc;
            Save();
        }

        public T Get(int id)
        {
            if (_data.TryGetValue(id, out var doc))
                return doc;

            throw new KeyNotFoundException($"Document with id {id} not found.");
        }

        public IEnumerable<T> All() => _data.Values;

        public NutShell<T> GetShell(int id)
        {
            return new NutShell<T> { Payload = Get(id) };
        }

        public IEnumerable<NutShell<T>> AllShells()
        {
            foreach (var item in _data.Values)
            {
                yield return new NutShell<T> { Payload = item };
            }
        }

        public void Clear()
        {
            _data.Clear();
            Save();
        }

        public void CompactNow()
        {
            // Stub for compaction - just resave
            Save();
        }

        private void Save()
        {
            var fileName = $"{_path}.{_collectionName}.json";
            File.WriteAllText(fileName, _serializer.Serialize(_data));
        }

        private Dictionary<int, T>? Load()
        {
            var fileName = $"{_path}.{_collectionName}.json";
            if (!File.Exists(fileName)) return null;

            var json = File.ReadAllText(fileName);
            return _serializer.Deserialize<Dictionary<int, T>>(json);
        }
    }
}
