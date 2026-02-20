using System;
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

        /// <summary>
        /// Cached entry count, updated atomically with each commit.
        /// Persisted in the superblock for O(1) Count queries.
        /// </summary>
        private long _entryCount;

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
            _options.Validate();

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

                _pageManager = new PageManager(_dataFilePath, _options.PageSize, _options.ValidateChecksumsOnRead);
                _pageCache = new PageCache(_options.MaxCachePages, _options.PageSize);
                _walManager = new WalManager(_walFilePath, _pageManager, _options.PageSize);
                _navigator = new BPlusTreeNavigator(_pageManager, _pageCache, _options.PageSize);

                // Recover from WAL if needed, then load root pointer from superblock
                _walManager.Recover();
                var (rootPageId, generation, entryCount) = _pageManager.ReadSuperblock();
                Volatile.Write(ref _rootPageId, rootPageId);
                Volatile.Write(ref _rootGeneration, generation);

                // Migration from v1 files: if entry count is 0 but tree is non-empty, recount.
                if (entryCount == 0 && rootPageId != 0)
                {
                    entryCount = _navigator.CountEntries(rootPageId);
                    _pageManager.WriteSuperblock(rootPageId, generation, entryCount);
                }
                Volatile.Write(ref _entryCount, entryCount);

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
            // Flush pending writes to ensure read-your-writes consistency
            FlushPendingWrites();

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
            // Flush pending writes so we delete from the latest state
            FlushPendingWrites();

            var keyBytes = Encoding.UTF8.GetBytes(id);
            long currentRoot = Volatile.Read(ref _rootPageId);

            if (currentRoot == 0)
                return; // Empty tree, nothing to delete

            var (newRoot, found) = _navigator.Delete(currentRoot, keyBytes, _walManager);
            long newCount = Volatile.Read(ref _entryCount) - (found ? 1 : 0);

            // Atomically update root pointer and persist via WAL
            var newGeneration = Interlocked.Increment(ref _rootGeneration);
            _walManager.CommitRootUpdate(newRoot, newGeneration, newCount);
            _pageManager.WriteSuperblock(newRoot, newGeneration, newCount);
            Volatile.Write(ref _rootPageId, newRoot);
            Volatile.Write(ref _entryCount, newCount);

            CheckpointIfNeeded();
        }

        public override IEnumerable<Nut<T>> CrackAll()
        {
            // Flush pending writes to ensure scan sees all committed data
            FlushPendingWrites();

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

            var (newRoot, isNewKey) = _navigator.Insert(currentRoot, keyBytes, data, _walManager);
            long newCount = Volatile.Read(ref _entryCount) + (isNewKey ? 1 : 0);

            var newGeneration = Interlocked.Increment(ref _rootGeneration);
            _walManager.CommitRootUpdate(newRoot, newGeneration, newCount);
            _pageManager.WriteSuperblock(newRoot, newGeneration, newCount);
            Volatile.Write(ref _rootPageId, newRoot);
            Volatile.Write(ref _entryCount, newCount);

            CheckpointIfNeeded();

            return Task.CompletedTask;
        }

        protected override Task WriteBatchToStorageAsync(List<PendingWrite> batch)
        {
            long currentRoot = Volatile.Read(ref _rootPageId);
            long newKeyCount = 0;

            // Apply all inserts to the tree, accumulating dirty pages in the WAL
            foreach (var write in batch)
            {
                var keyBytes = Encoding.UTF8.GetBytes(write.Id);
                var (newRoot, isNewKey) = _navigator.Insert(currentRoot, keyBytes, write.ProcessedData, _walManager);
                currentRoot = newRoot;
                if (isNewKey) newKeyCount++;
            }

            long newCount = Volatile.Read(ref _entryCount) + newKeyCount;

            // Single WAL commit + fsync for the entire batch (group commit)
            var newGeneration = Interlocked.Increment(ref _rootGeneration);
            _walManager.CommitRootUpdate(currentRoot, newGeneration, newCount);
            _pageManager.WriteSuperblock(currentRoot, newGeneration, newCount);
            Volatile.Write(ref _rootPageId, currentRoot);
            Volatile.Write(ref _entryCount, newCount);

            CheckpointIfNeeded();

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
            // Flush pending writes to ensure range scan sees all committed data
            FlushPendingWrites();

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
        /// O(1) via cached superblock metadata.
        /// </summary>
        public long Count
        {
            get
            {
                FlushPendingWrites();
                return Volatile.Read(ref _entryCount);
            }
        }

        #endregion

        #region Maintenance

        /// <summary>
        /// Flush any pending batched writes to the B+Tree so that subsequent reads
        /// see the latest committed state (read-your-writes consistency).
        /// This is a no-op if the write buffer is empty.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushPendingWrites()
        {
            if (PendingWriteCount > 0)
                FlushBatchAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Force a WAL checkpoint: apply all WAL entries to the data file and truncate the WAL.
        /// This is normally done automatically when the committed entry count exceeds
        /// <see cref="BPlusTreeOptions.CheckpointThreshold"/>, but can also be triggered manually.
        /// </summary>
        public void Checkpoint()
        {
            _walManager.Checkpoint();
        }

        /// <summary>
        /// Triggers a WAL checkpoint if the number of committed page images since the last
        /// checkpoint exceeds the configured threshold. Safe to call under concurrent reads
        /// since checkpoint only truncates the WAL (pages are already applied to the data file).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckpointIfNeeded()
        {
            if (_walManager.CommittedSinceCheckpoint >= _options.CheckpointThreshold)
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
