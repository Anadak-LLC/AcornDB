using AcornDB.Storage;
using AcornDB.Sync;
using AcornDB.Conflict;
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
        private readonly EventManager<T> _eventManager = new();
        private readonly IConflictJudge<T> _conflictJudge;

        // Reactive change notifications
        internal event Action<string, T, Nut<T>>? OnStashEvent;
        internal event Action<string>? OnTossEvent;
        internal event Action<string, Nut<T>>? OnSquabbleEvent;

        // Auto-ID detection caching
        private Func<T, string>? _idExtractor = null;
        private bool _idExtractorInitialized = false;

        // Stats tracking
        private int _totalStashed = 0;
        private int _totalTossed = 0;
        private int _squabblesResolved = 0;
        private int _smushesPerformed = 0;

        // Sync tracking for incremental/delta sync
        private DateTime _lastSyncTimestamp = DateTime.MinValue;

        // Public properties
        public int NutCount => _cache.Count;
        public DateTime LastSyncTimestamp => _lastSyncTimestamp;

        /// <summary>
        /// Get all nuts in the tree (payload + metadata)
        /// Useful for queries and exports
        /// </summary>
        public IEnumerable<Nut<T>> GetAllNuts()
        {
            return _cache.Values.ToList();
        }

        /// <summary>
        /// Get all payloads in the tree
        /// </summary>
        public IEnumerable<T> GetAll()
        {
            return _cache.Values.Select(nut => nut.Payload).ToList();
        }

        public Tree(ITrunk<T>? trunk = null, Cache.ICacheStrategy<T>? cacheStrategy = null, IConflictJudge<T>? conflictJudge = null)
        {
            _trunk = trunk ?? new FileTrunk<T>(); // defaults to FileTrunk
            _cacheStrategy = cacheStrategy ?? new Cache.LRUCacheStrategy<T>(maxSize: 10_000); // defaults to LRU with 10k limit
            _conflictJudge = conflictJudge ?? new TimestampJudge<T>(); // defaults to last-write-wins
            InitializeIdExtractor();
            LoadFromTrunk();
            StartExpirationTimer(); // Start TTL enforcement
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

            // Notify cache strategy
            _cacheStrategy?.OnStash(id, nut);

            // Notify subscribers
            _eventManager.RaiseChanged(item);

            // Raise reactive event
            OnStashEvent?.Invoke(id, item, nut);

            foreach (var branch in _branches)
            {
                branch.TryPush(id, nut);
            }

            // Check if cache eviction is needed
            CheckAndEvictCache();
        }

        public T? Crack(string id)
        {
            if (_cache.TryGetValue(id, out var shell))
            {
                // Notify cache strategy of access (for LRU tracking)
                _cacheStrategy?.OnCrack(id);
                return shell.Payload;
            }

            var fromTrunk = _trunk.Load(id);
            if (fromTrunk != null)
            {
                _cache[id] = fromTrunk;
                // Notify cache strategy of new item
                _cacheStrategy?.OnStash(id, fromTrunk);
                return fromTrunk.Payload;
            }

            return default;
        }

        public void Toss(string id)
        {
            var item = Crack(id);
            _cache.Remove(id);
            _trunk.Delete(id);
            _totalTossed++;

            // Notify cache strategy
            _cacheStrategy?.OnToss(id);

            // Raise reactive event
            OnTossEvent?.Invoke(id);

            // Notify subscribers if item existed
            if (item != null)
                _eventManager.RaiseChanged(item);
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
                // Use the conflict judge to determine which nut to keep
                var winner = _conflictJudge.Judge(existing, incoming);
                _squabblesResolved++;

                OnSquabbleEvent?.Invoke(id, winner);

                // If the winner is the existing nut, keep it and return
                if (ReferenceEquals(winner, existing))
                    return;

                // Otherwise, save the incoming nut
                _cache[id] = winner;
                _trunk.Save(id, winner);
            }
            else
            {
                // No conflict, just stash the incoming nut
                _cache[id] = incoming;
                _trunk.Save(id, incoming);
                OnStashEvent?.Invoke(id, incoming.Payload, incoming);
            }
        }

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            // Delegate to trunk - may throw NotSupportedException if trunk doesn't support history
            return _trunk.GetHistory(id);
        }

        /// <summary>
        /// Export all nuts for synchronization
        /// </summary>
        public IEnumerable<Nut<T>> ExportChanges()
        {
            return _trunk.ExportChanges();
        }

        /// <summary>
        /// Export only nuts that have changed since a specific timestamp (incremental/delta sync)
        /// This is much more efficient than exporting all changes for large trees
        /// </summary>
        /// <param name="since">Only export nuts modified after this timestamp</param>
        /// <returns>Nuts that were modified after the given timestamp</returns>
        public IEnumerable<Nut<T>> ExportChangesSince(DateTime since)
        {
            // Filter nuts by timestamp - only return those modified after 'since'
            return _cache.Values
                .Where(nut => nut.Timestamp > since)
                .ToList();
        }

        /// <summary>
        /// Export changes since the last sync (delta sync)
        /// Automatically tracks the last sync timestamp
        /// </summary>
        public IEnumerable<Nut<T>> ExportDeltaChanges()
        {
            var changes = ExportChangesSince(_lastSyncTimestamp);
            _lastSyncTimestamp = DateTime.UtcNow;
            return changes;
        }

        /// <summary>
        /// Mark that a sync operation completed successfully
        /// Updates the last sync timestamp to enable delta sync
        /// </summary>
        public void MarkSyncCompleted()
        {
            _lastSyncTimestamp = DateTime.UtcNow;
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

                // Squabble undone successfully
                return true;
            }
            catch (NotSupportedException)
            {
                // History not supported by this trunk
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

        /// <summary>
        /// Subscribe to changes in this tree (nutty style!)
        /// </summary>
        public void Subscribe(Action<T> callback)
        {
            _eventManager.Subscribe(callback);
        }
    }
}
