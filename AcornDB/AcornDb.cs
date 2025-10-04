using System;
using System.Collections.Concurrent;
using System.IO;
using AcornDB.Storage;
using AcornDB.Events;
using AcornDB.Serialization;

namespace AcornDB
{
    public class AcornDb
    {
        private readonly string _filePath;
        private readonly ISerializer _serializer;
        private readonly ConcurrentDictionary<string, object> _collections;

        public AcornDb(string filePath, ISerializer? serializer = null)
        {
            _filePath = filePath;
            _serializer = serializer ?? new NewtonsoftJsonSerializer();
            _collections = new ConcurrentDictionary<string, object>();
        }

        public Collection<T> GetCollection<T>(string name)
        {
            if (_collections.TryGetValue(name, out var collection))
                return (Collection<T>)collection;

            var store = new DocumentStore<T>(_filePath, name, _serializer);
            var newCollection = new Collection<T>(store);
            _collections[name] = newCollection;
            return newCollection;
        }

        public void Extend(string endpoint, string key)
        {
            Console.WriteLine($">> Stub: Extending to {endpoint} with key {key}");
            // TODO: Real cloud sync logic here
        }

        public void ExtendFrom(string endpoint)
        {
            Console.WriteLine($">> Stub: Extending from {endpoint}");
            // TODO: Real cluster attach logic here
        }
    }
}
