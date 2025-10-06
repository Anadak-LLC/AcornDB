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
        private readonly Dictionary<string, Nut<T>> _cache = new();
        private readonly List<Branch> _branches = new();
        internal readonly List<Tangle<T>> _tangles = new();
        private readonly ITrunk<T> _trunk;

        // Auto-ID detection caching
        private Func<T, string>? _idExtractor = null;
        private bool _idExtractorInitialized = false;

        // Stats tracking
        private int _totalStashed = 0;
        private int _totalTossed = 0;
        private int _squabblesResolved = 0;
        private int _smushesPerformed = 0;

        // Public properties
        public int NutCount => _cache.Count;

        public Tree(ITrunk<T>? trunk = null)
        {
            _trunk = trunk ?? new FileTrunk<T>(); // defaults to FileTrunk
            InitializeIdExtractor();
            LoadFromTrunk();
        }

        /// <summary>
        /// Stash a nut with auto-ID detection
        /// </summary>
        public void Stash(T item)
        {
            var id = ExtractId(item);
            Stash(id, item);
        }

        /// <summary>
        /// Stash a nut with explicit ID
        /// </summary>
        public void Stash(string id, T item)
        {
            var nut = new Nut<T>
            {
                Id = id,
                Payload = item,
                Timestamp = DateTime.UtcNow
            };

            _cache[id] = nut;
            _trunk.Save(id, nut);
            _totalStashed++;

            foreach (var branch in _branches)
            {
                branch.TryPush(id, nut);
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
            _totalTossed++;
        }

        public void Shake()
        {
            Console.WriteLine("🌳 Shaking tree...");

            // Export changes from trunk for sync
            var changes = _trunk.ExportChanges();

            foreach (var branch in _branches)
            {
                foreach (var shell in changes)
                {
                    branch.TryPush(shell.Id, shell);
                }
            }
        }

        public void Squabble(string id, Nut<T> incoming)
        {
            if (_cache.TryGetValue(id, out var existing))
            {
                if (existing.Timestamp >= incoming.Timestamp)
                {
                    Console.WriteLine($"> ⚖️ Squabble: Local nut for '{id}' is fresher. Keeping it.");
                    _squabblesResolved++;
                    return;
                }

                Console.WriteLine($"> 🥜 Squabble: Incoming nut for '{id}' is newer. Replacing it.");
                _squabblesResolved++;
            }
            else
            {
                Console.WriteLine($"> 🌰 First nut for '{id}' stashed.");
            }

            _cache[id] = incoming;
            _trunk.Save(id, incoming); // Trunk handles versioning
        }

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            // Delegate to trunk - may throw NotSupportedException if trunk doesn't support history
            return _trunk.GetHistory(id);
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return _trunk.ExportChanges();
        }

        /// <summary>
        /// Entangle with a remote branch via HTTP
        /// </summary>
        public void Entangle(Branch branch)
        {
            if (!_branches.Contains(branch))
            {
                _branches.Add(branch);
                Console.WriteLine($"> 🌉 Tree<{typeof(T).Name}> entangled with {branch.RemoteUrl}");
            }
        }

        /// <summary>
        /// Entangle with another tree in-process (no HTTP required)
        /// </summary>
        public void Entangle(Tree<T> otherTree)
        {
            var inProcessBranch = new InProcessBranch<T>(otherTree);
            Entangle(inProcessBranch);
            Console.WriteLine($"> 🪢 Tree<{typeof(T).Name}> entangled in-process");
        }

        public bool UndoSquabble(string id)
        {
            try
            {
                var versions = _trunk.GetHistory(id);
                if (versions.Count == 0)
                {
                    Console.WriteLine($"> 🕳️ No squabble history for '{id}' to undo.");
                    return false;
                }

                var lastVersion = versions[^1];
                _cache[id] = lastVersion;
                _trunk.Save(id, lastVersion);

                Console.WriteLine($"> ⏪ Squabble undone for '{id}'. Reverted to version from {lastVersion.Timestamp}.");
                return true;
            }
            catch (NotSupportedException)
            {
                Console.WriteLine($"> ⚠️ History not supported by this trunk. Cannot undo squabble for '{id}'.");
                return false;
            }
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

        public TreeStats GetNutStats()
        {
            return new TreeStats
            {
                TotalStashed = _totalStashed,
                TotalTossed = _totalTossed,
                SquabblesResolved = _squabblesResolved,
                SmushesPerformed = _smushesPerformed,
                ActiveTangles = _tangles.Count
            };
        }

        /// <summary>
        /// Initialize ID extractor using reflection (cached for performance)
        /// </summary>
        private void InitializeIdExtractor()
        {
            if (_idExtractorInitialized) return;

            var type = typeof(T);

            // Check if implements INutment<T>
            var nutmentInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Models.INutment<>));

            if (nutmentInterface != null)
            {
                var idProperty = type.GetProperty("Id");
                if (idProperty != null)
                {
                    _idExtractor = (item) => idProperty.GetValue(item)?.ToString() ?? string.Empty;
                    _idExtractorInitialized = true;
                    return;
                }
            }

            // Try common ID property names: Id, ID, Key, KEY
            var candidateNames = new[] { "Id", "ID", "Key", "KEY", "id", "key" };
            foreach (var name in candidateNames)
            {
                var property = type.GetProperty(name);
                if (property != null && property.CanRead)
                {
                    _idExtractor = (item) => property.GetValue(item)?.ToString() ?? string.Empty;
                    _idExtractorInitialized = true;
                    return;
                }
            }

            _idExtractorInitialized = true;
        }

        /// <summary>
        /// Extract ID from an object using cached reflection
        /// </summary>
        private string ExtractId(T item)
        {
            if (_idExtractor == null)
            {
                throw new InvalidOperationException(
                    $"Cannot auto-detect ID for type {typeof(T).Name}. " +
                    "Either implement INutment<TKey>, add an 'Id' or 'Key' property, " +
                    "or use Stash(id, item) with an explicit ID.");
            }

            var id = _idExtractor(item);
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException(
                    $"Extracted ID for {typeof(T).Name} is null or empty. " +
                    "Ensure the ID property has a value before stashing.");
            }

            return id;
        }
    }
}
