using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Storage.Serialization;

namespace AcornDB.Storage.BPlusTree
{
    /// <summary>
    /// True page-based B+Tree trunk with WAL-based crash safety, page cache, and ordered access.
    ///
    /// Architecture:
    ///   - Fixed-size pages (default 8KB) with slotted-page layout
    ///   - B+Tree: values in leaves only, leaf chaining for ordered scans
    ///   - WAL (write-ahead log) for crash safety with group commit
    ///   - Clock-based page cache for reduced I/O
    ///   - Single writer / concurrent readers (snapshot isolation via atomic root pointer)
    ///
    /// Storage Pipeline (root processing handled by TrunkBase):
    ///   Write: Nut{T} -> Serialize -> RootsAscending -> B+Tree.Insert(keyBytes, valueBytes)
    ///   Read:  B+Tree.Lookup(keyBytes) -> valueBytes -> RootsDescending -> Deserialize -> Nut{T}
    ///
    /// The B+Tree itself is type-agnostic: it stores (byte[] key, byte[] value) pairs.
    /// Keys are UTF-8 encoded document IDs. Values are post-root-pipeline opaque payloads.
    /// </summary>
    public sealed class BPlusTreeTrunk<T> : TrunkBase<T>, IDisposable where T : class
    {
        private readonly string _dataDirectory;
        private readonly string _dataFilePath;
        private readonly string _walFilePath;
        private readonly BPlusTreeOptions _options;

        private PageManager _pageManager;
        private PageCache _pageCache;
        private WalManager _walManager;
        private BPlusTreeNavigator _navigator;

        /// <summary>
        /// Current root page ID. Updated atomically after each committed batch.
        /// Readers snapshot this value at the start of an operation for consistent reads.
        /// </summary>
        private long _rootPageId;

        /// <summary>
        /// Monotonically increasing generation counter. Incremented on each root pointer update.
        /// Used for snapshot isolation: readers use the generation at read-start.
        /// </summary>
        private long _rootGeneration;

        private bool _initialized;
        private readonly object _initLock = new();

        // Batching constants (tuned for B+Tree page-write patterns)
        private const int BUFFER_THRESHOLD = 256;
        private const int FLUSH_INTERVAL_MS = 100;

        public BPlusTreeTrunk(string? customPath = null, ISerializer? serializer = null, BPlusTreeOptions? options = null)
            : base(
                serializer,
                enableBatching: true,
                batchThreshold: BUFFER_THRESHOLD,
                flushIntervalMs: FLUSH_INTERVAL_MS)
        {
            _options = options ?? BPlusTreeOptions.Default;

            var typeName = typeof(T).Name;
            _dataDirectory = customPath ?? Path.Combine(Directory.GetCurrentDirectory(), "data", typeName);
            Directory.CreateDirectory(_dataDirectory);

            _dataFilePath = Path.Combine(_dataDirectory, "bplustree.db");
            _walFilePath = Path.Combine(_dataDirectory, "bplustree.wal");

            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;

                _pageManager = new PageManager(_dataFilePath, _options.PageSize);
                _pageCache = new PageCache(_options.MaxCachePages, _options.PageSize);
                _walManager = new WalManager(_walFilePath, _pageManager, _options.PageSize);
                _navigator = new BPlusTreeNavigator(_pageManager, _pageCache, _options.PageSize);

                // Recover from WAL if needed, then load root pointer from superblock
                _walManager.Recover();
                var (rootPageId, generation) = _pageManager.ReadSuperblock();
                Volatile.Write(ref _rootPageId, rootPageId);
                Volatile.Write(ref _rootGeneration, generation);

                _initialized = true;
            }
        }

        #region ITrunk<T> Implementation

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Stash(string id, Nut<T> nut)
        {
            StashWithBatchingAsync(id, nut).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Nut<T>? Crack(string id)
        {
            var keyBytes = Encoding.UTF8.GetBytes(id);
            long rootSnapshot = Volatile.Read(ref _rootPageId);

            if (rootSnapshot == 0)
                return null; // Empty tree

            var valueBytes = _navigator.Search(rootSnapshot, keyBytes);
            if (valueBytes == null)
                return null;

            // Run root pipeline descending on the value payload only
            var processedBytes = ProcessThroughRootsDescending(valueBytes, id);

            // Deserialize: the value is a serialized Nut<T> (JSON via ISerializer)
            var json = Encoding.UTF8.GetString(processedBytes);
            var nut = _serializer.Deserialize<Nut<T>>(json);
            return nut;
        }

        public override void Toss(string id)
        {
            var keyBytes = Encoding.UTF8.GetBytes(id);
            long currentRoot = Volatile.Read(ref _rootPageId);

            if (currentRoot == 0)
                return; // Empty tree, nothing to delete

            var (newRoot, _) = _navigator.Delete(currentRoot, keyBytes, _walManager);

            // Atomically update root pointer and persist via WAL
            var newGeneration = Interlocked.Increment(ref _rootGeneration);
            _walManager.CommitRootUpdate(newRoot, newGeneration);
            _pageManager.WriteSuperblock(newRoot, newGeneration);
            Volatile.Write(ref _rootPageId, newRoot);
        }

        public override IEnumerable<Nut<T>> CrackAll()
        {
            long rootSnapshot = Volatile.Read(ref _rootPageId);
            if (rootSnapshot == 0)
                return Enumerable.Empty<Nut<T>>();

            var results = new List<Nut<T>>();
            foreach (var (keyBytes, valueBytes) in _navigator.ScanAll(rootSnapshot))
            {
                var id = Encoding.UTF8.GetString(keyBytes);
                var processedBytes = ProcessThroughRootsDescending(valueBytes, id);
                var json = Encoding.UTF8.GetString(processedBytes);
                var nut = _serializer.Deserialize<Nut<T>>(json);
                if (nut != null)
                    results.Add(nut);
            }

            return results;
        }

        public override IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException(
                "BPlusTreeTrunk does not support history. Use DocumentStoreTrunk for versioning.");
        }

        public override IEnumerable<Nut<T>> ExportChanges()
        {
            return CrackAll();
        }

        public override void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            foreach (var nut in incoming)
            {
                Stash(nut.Id, nut);
            }

            FlushBatchAsync().GetAwaiter().GetResult();
        }

        public override ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = false,
            SupportsNativeIndexes = false,
            SupportsFullTextSearch = false,
            SupportsComputedIndexes = false,
            TrunkType = "BPlusTreeTrunk"
        };

        #endregion

        #region Batched Write Path

        protected override Task WriteToStorageAsync(string id, byte[] data, DateTime timestamp, int version)
        {
            var keyBytes = Encoding.UTF8.GetBytes(id);
            long currentRoot = Volatile.Read(ref _rootPageId);

            var newRoot = _navigator.Insert(currentRoot, keyBytes, data, _walManager);

            var newGeneration = Interlocked.Increment(ref _rootGeneration);
            _walManager.CommitRootUpdate(newRoot, newGeneration);
            _pageManager.WriteSuperblock(newRoot, newGeneration);
            Volatile.Write(ref _rootPageId, newRoot);

            return Task.CompletedTask;
        }

        protected override Task WriteBatchToStorageAsync(List<PendingWrite> batch)
        {
            long currentRoot = Volatile.Read(ref _rootPageId);

            // Apply all inserts to the tree, accumulating dirty pages in the WAL
            foreach (var write in batch)
            {
                var keyBytes = Encoding.UTF8.GetBytes(write.Id);
                currentRoot = _navigator.Insert(currentRoot, keyBytes, write.ProcessedData, _walManager);
            }

            // Single WAL commit + fsync for the entire batch (group commit)
            var newGeneration = Interlocked.Increment(ref _rootGeneration);
            _walManager.CommitRootUpdate(currentRoot, newGeneration);
            _pageManager.WriteSuperblock(currentRoot, newGeneration);
            Volatile.Write(ref _rootPageId, currentRoot);

            return Task.CompletedTask;
        }

        #endregion

        #region Range Operations

        /// <summary>
        /// Scan all entries whose keys fall within [startKey, endKey] (inclusive).
        /// Leverages B+Tree leaf chaining for efficient ordered traversal.
        /// </summary>
        public IEnumerable<Nut<T>> RangeScan(string startKey, string endKey)
        {
            var startBytes = Encoding.UTF8.GetBytes(startKey);
            var endBytes = Encoding.UTF8.GetBytes(endKey);
            long rootSnapshot = Volatile.Read(ref _rootPageId);

            if (rootSnapshot == 0)
                yield break;

            foreach (var (keyBytes, valueBytes) in _navigator.RangeScan(rootSnapshot, startBytes, endBytes))
            {
                var id = Encoding.UTF8.GetString(keyBytes);
                var processedBytes = ProcessThroughRootsDescending(valueBytes, id);
                var json = Encoding.UTF8.GetString(processedBytes);
                var nut = _serializer.Deserialize<Nut<T>>(json);
                if (nut != null)
                    yield return nut;
            }
        }

        /// <summary>
        /// Get the number of entries currently in the tree.
        /// </summary>
        public long Count
        {
            get
            {
                long rootSnapshot = Volatile.Read(ref _rootPageId);
                if (rootSnapshot == 0) return 0;
                return _navigator.CountEntries(rootSnapshot);
            }
        }

        #endregion

        #region Maintenance

        /// <summary>
        /// Force a WAL checkpoint: apply all WAL entries to the data file and truncate the WAL.
        /// This is normally done automatically, but can be triggered manually.
        /// </summary>
        public void Checkpoint()
        {
            _walManager.Checkpoint();
        }

        #endregion

        #region Disposal

        public override void Dispose()
        {
            if (_disposed) return;

            // TrunkBase flushes pending batches and disposes timer/lock
            base.Dispose();

            // Dispose B+Tree-specific resources
            _walManager?.Dispose();
            _pageCache?.Dispose();
            _pageManager?.Dispose();
        }

        #endregion
    }
}
