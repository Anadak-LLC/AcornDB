using AcornDB.Storage;
using AcornDB.Sync;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AcornDB
{
    public partial class Tree<T>
    {
        private readonly Dictionary<string, NutShell<T>> _cache = new();
        private readonly Dictionary<string, List<NutShell<T>>> _history = new();
        private readonly List<Branch> _branches = new();
        internal readonly List<Tangle<T>> _tangles = new();
        private readonly ITrunk<T> _trunk;

        public Tree(ITrunk<T>? trunk = null)
        {
            _trunk = trunk ?? new FileTrunk<T>(); // default
            LoadFromTrunk();
        }

        public void Stash(string id, T item)
        {
            var shell = new NutShell<T>
            {
                Id = id,
                Payload = item,
                Timestamp = DateTime.UtcNow
            };

            _cache[id] = shell;
            _trunk.Save(id, shell);

            foreach (var branch in _branches)
            {
                branch.TryPush(id, shell);
            }
        }

        public T? Crack(string id)
        {
            if (_cache.TryGetValue(id, out var shell))
                return shell.Payload;

            var fromTrunk = _trunk.Load(id);
            if (fromTrunk != null)
            {
                _cache[id] = fromTrunk;
                return fromTrunk.Payload;
            }

            return default;
        }

        public void Toss(string id)
        {
            _cache.Remove(id);
            _trunk.Delete(id);
        }

        public void Shake()
        {
            Console.WriteLine("🌳 Shaking tree...");
            foreach (var branch in _branches)
            {
                foreach (var (id, shell) in _cache)
                {
                    branch.TryPush(id, shell);
                }
            }
        }

        public void Squabble(string id, NutShell<T> incoming)
        {
            if (_cache.TryGetValue(id, out var existing))
            {
                if (existing.Timestamp >= incoming.Timestamp)
                {
                    Console.WriteLine($"> ⚖️ Squabble: Local nut for '{id}' is fresher. Keeping it.");
                    return;
                }

                Console.WriteLine($"> 🥜 Squabble: Incoming nut for '{id}' is newer. Replacing it.");
                AddToHistory(id, existing);
            }
            else
            {
                Console.WriteLine($"> 🌰 First nut for '{id}' stashed.");
            }

            _cache[id] = incoming;
            _trunk.Save(id, incoming);
        }

        private void AddToHistory(string id, NutShell<T> previous)
        {
            if (!_history.ContainsKey(id))
                _history[id] = new();

            _history[id].Add(previous);
        }

        public IReadOnlyList<NutShell<T>> GetHistory(string id)
        {
            return _history.TryGetValue(id, out var versions) ? versions : new List<NutShell<T>>();
        }

        public IEnumerable<NutShell<T>> ExportChanges()
        {
            return _cache.Values;
        }

        public void Entangle(Branch branch)
        {
            if (!_branches.Contains(branch))
            {
                _branches.Add(branch);
                Console.WriteLine($"> 🌉 Tree<{typeof(T).Name}> entangled with {branch.RemoteUrl}");
            }
        }

        public bool UndoSquabble(string id)
        {
            if (!_history.TryGetValue(id, out var versions) || versions.Count == 0)
            {
                Console.WriteLine($"> 🕳️ No squabble history for '{id}' to undo.");
                return false;
            }

            var lastVersion = versions[^1];
            versions.RemoveAt(versions.Count - 1);

            _cache[id] = lastVersion;
            _trunk.Save(id, lastVersion);

            Console.WriteLine($"> ⏪ Squabble undone for '{id}'. Reverted to version from {lastVersion.Timestamp}.");
            return true;
        }

        internal void RegisterTangle(Tangle<T> tangle)
        {
            _tangles.Add(tangle);
        }

        internal IEnumerable<Tangle<T>> GetTangles()
        {
            return _tangles;
        }

        private void PushDeleteToAllTangles(string key)
        {
            foreach (var tangle in _tangles)
            {
                tangle.PushDelete(key);
            }
        }

        private void LoadFromTrunk()
        {
            foreach (var shell in _trunk.LoadAll())
            {
                if (!string.IsNullOrWhiteSpace(shell.Id))
                    _cache[shell.Id] = shell;
            }
        }
    }
}
