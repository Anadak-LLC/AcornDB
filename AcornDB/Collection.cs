using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcornDB.Events;
using AcornDB.Models;
using AcornDB.Storage;
using AcornDB.Sync;
using Newtonsoft.Json;

namespace AcornDB
{
    public class Collection<T> : ISyncableCollection<T>
    {
        private readonly DocumentStore<T> _store;
        private readonly EventManager<T> _events;
        private readonly string _logPath;

        public Collection(DocumentStore<T> store, string logPath = "")
        {
            _store = store;
            _events = new EventManager<T>();
            _logPath = string.IsNullOrWhiteSpace(logPath) ? "acorn.collection.sync.log" : logPath;
        }

        // Basic nut-only operations

        public void Insert(T document, TimeSpan? ttl = null)
        {
            _store.Insert(document, ttl);
            _events.RaiseChanged(document);
        }

        public T Get(int id) => _store.Get(id);

        public void ReShell(int id, Action<T> updateFunc)
        {
            var doc = _store.Get(id);
            updateFunc(doc);
            _store.Update(id, doc);
            _events.RaiseChanged(doc);
        }

        public IEnumerable<T> All() => _store.All();

        public IQueryable<T> Query() => _store.All().AsQueryable();

        public void OnChanged(Action<T> callback) => _events.Subscribe(callback);

        // 🌰 Nut-themed aliases

        public void Stash(T nut, TimeSpan? ttl = null) => Insert(nut, ttl);

        public T Crack(int id) => Get(id);

        public void ReShell(int id, Action<T> update) => ReShell(id, update);

        public IEnumerable<T> Harvest() => All();

        // 🛡️ Shell access

        public NutShell<T> GetShell(int id) => _store.GetShell(id);

        public IEnumerable<NutShell<T>> AllShells() => _store.AllShells();

        public IQueryable<NutShell<T>> QueryShells() => _store.AllShells().AsQueryable();

        // 🧹 Maintenance tools

        public void Clear() => _store.Clear();

        public int Count() => _store.All().Count();

        public int ShellCount() => _store.AllShells().Count();

        public void SmushNow() => _store.CompactNow();

        // 🔁 Sync logic

        public ChangeSet<T> ExportChanges()
        {
            var changeSet = new ChangeSet<T>();

            if (!File.Exists(_logPath)) return changeSet;

            var lines = File.ReadLines(_logPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var entry = JsonConvert.DeserializeObject<ChangeEntry<T>>(line);
                    if (entry != null) changeSet.Changes.Add(entry);
                }
                catch
                {
                    // Ignore corrupt lines
                }
            }

            return changeSet;
        }

        public void ImportChanges(ChangeSet<T> changes)
        {
            foreach (var entry in changes.Changes)
            {
                try
                {
                    switch (entry.Operation.ToLowerInvariant())
                    {
                        case "insert":
                        case "update":
                            var shell = _store.GetShell(entry.Id);
                            if (shell.Version < entry.Payload?.Version)
                            {
                                _store.Update(entry.Id, entry.Payload.Nut);
                            }
                            break;
                        case "delete":
                            _store.Clear(); // TODO: refine this to support delete by ID
                            break;
                    }
                }
                catch
                {
                    // Entry doesn't exist — safe to insert
                    if (entry.Payload != null)
                    {
                        _store.Insert(entry.Payload.Nut);
                    }
                }
            }
        }
    }
}
