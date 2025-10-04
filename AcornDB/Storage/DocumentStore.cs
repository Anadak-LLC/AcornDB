using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AcornDB.Serialization;
using AcornDB.Models;
using System.Reflection;

namespace AcornDB.Storage
{
    public class DocumentStore<T>
    {
        private class ChangeLogEntry
        {
            public int Id { get; set; }
            public string Operation { get; set; } = "insert";
            public NutShell<T>? Payload { get; set; }
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        }

        private readonly string _snapshotFile;
        private readonly string _logFile;
        private readonly ISerializer _serializer;
        private Dictionary<int, NutShell<T>> _data;
        private int _operationCount;
        private const int AutoCompactThreshold = 20;
        private readonly bool _supportsCustomId;
        private readonly PropertyInfo? _idProperty;

        public DocumentStore(string basePath, string collectionName, ISerializer serializer)
        {
            _snapshotFile = $"{basePath}.{collectionName}.snapshot.json";
            _logFile = $"{basePath}.{collectionName}.changes.log";
            _serializer = serializer;

            _data = LoadSnapshot() ?? new Dictionary<int, NutShell<T>>();
            ReplayLog();

            // Check if T implements INutment<int>
            _idProperty = typeof(T).GetProperty("Id");
            _supportsCustomId = typeof(INutment<int>).IsAssignableFrom(typeof(T)) && _idProperty != null;
        }

        public void Insert(T doc, TimeSpan? ttl = null)
        {
            int id;
            if (_supportsCustomId)
            {
                id = (int)(_idProperty!.GetValue(doc) ?? throw new InvalidOperationException("Id cannot be null on INutment instance."));
            }
            else
            {
                id = _data.Count + 1;
            }

            var shell = new NutShell<T>
            {
                Nut = doc,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = ttl.HasValue ? DateTimeOffset.UtcNow.Add(ttl.Value) : null,
                Version = 1
            };

            _data[id] = shell;
            AppendToLog(new ChangeLogEntry { Id = id, Operation = "insert", Payload = shell });
            MaybeCompact();
        }

        public void Update(int id, T doc)
        {
            if (!_data.TryGetValue(id, out var shell))
                throw new KeyNotFoundException($"Document with id {id} not found.");

            shell.Nut = doc;
            shell.UpdatedAt = DateTimeOffset.UtcNow;
            shell.Version += 1;

            _data[id] = shell;
            AppendToLog(new ChangeLogEntry { Id = id, Operation = "update", Payload = shell });
            MaybeCompact();
        }

        public T Get(int id)
        {
            var shell = GetShell(id);
            return shell.Nut;
        }

        public NutShell<T> GetShell(int id)
        {
            if (_data.TryGetValue(id, out var shell))
            {
                if (shell.ExpiresAt.HasValue && shell.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    _data.Remove(id);
                    AppendToLog(new ChangeLogEntry { Id = id, Operation = "delete" });
                    MaybeCompact();
                    throw new KeyNotFoundException($"Document with id {id} has expired.");
                }

                return shell;
            }

            throw new KeyNotFoundException($"Document with id {id} not found.");
        }

        public IEnumerable<T> All()
        {
            PurgeExpired();
            return _data.Values.Select(v => v.Nut);
        }

        public IEnumerable<NutShell<T>> AllShells()
        {
            PurgeExpired();
            return _data.Values;
        }

        public void Clear()
        {
            _data.Clear();
            File.Delete(_snapshotFile);
            File.Delete(_logFile);
        }

        public void CompactNow()
        {
            Compact();
        }

        private void AppendToLog(ChangeLogEntry entry)
        {
            var json = _serializer.Serialize(entry);
            File.AppendAllText(_logFile, json + Environment.NewLine);
            _operationCount++;
        }

        private Dictionary<int, NutShell<T>>? LoadSnapshot()
        {
            if (!File.Exists(_snapshotFile)) return null;
            var json = File.ReadAllText(_snapshotFile);
            return _serializer.Deserialize<Dictionary<int, NutShell<T>>>(json);
        }

        private void ReplayLog()
        {
            if (!File.Exists(_logFile)) return;

            foreach (var line in File.ReadLines(_logFile))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = _serializer.Deserialize<ChangeLogEntry>(line);
                if (entry == null) continue;

                switch (entry.Operation)
                {
                    case "insert":
                    case "update":
                        _data[entry.Id] = entry.Payload!;
                        break;
                    case "delete":
                        _data.Remove(entry.Id);
                        break;
                }
            }
        }

        private void PurgeExpired()
        {
            var expired = _data
                .Where(kvp => kvp.Value.ExpiresAt.HasValue && kvp.Value.ExpiresAt <= DateTimeOffset.UtcNow)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in expired)
            {
                _data.Remove(id);
                AppendToLog(new ChangeLogEntry { Id = id, Operation = "delete" });
            }
        }

        private void MaybeCompact()
        {
            if (_operationCount >= AutoCompactThreshold)
            {
                Compact();
                _operationCount = 0;
            }
        }

        private void Compact()
        {
            File.WriteAllText(_snapshotFile, _serializer.Serialize(_data));
            File.Delete(_logFile);
        }
    }
}
