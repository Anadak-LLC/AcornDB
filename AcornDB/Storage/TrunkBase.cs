using System;
using AcornDB.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Policy;
using AcornDB.Storage.Serialization;

namespace AcornDB.Storage
{
    /// <summary>
    /// Abstract base class for all trunk implementations.
    /// Provides unified IRoot pipeline support and optional write batching, eliminating code duplication across trunks.
    ///
    /// All trunks inherit common functionality:
    /// - IRoot collection management (AddRoot, RemoveRoot, Roots)
    /// - IRoot processing pipeline (ascending for writes, descending for reads)
    /// - Optional write batching with configurable thresholds and flush intervals
    /// - Serializer abstraction
    /// - Disposal pattern
    ///
    /// Derived classes only need to implement storage-specific operations:
    /// - Stash (write to storage) - or use StashWithBatchingAsync for batched writes
    /// - Crack (read from storage)
    /// - Toss (delete from storage)
    /// - CrackAll (read all from storage)
    /// </summary>
    /// <typeparam name="T">The payload type stored in this trunk</typeparam>
    public abstract class TrunkBase<T> : ITrunk<T>, IDisposable where T : class
    {
        #region Protected Fields (Available to Derived Classes)

        /// <summary>
        /// Collection of IRoot processors for byte transformation pipeline
        /// </summary>
        protected readonly List<IRoot> _roots = new();

        /// <summary>
        /// Lock for thread-safe access to root collection
        /// </summary>
        protected readonly object _rootsLock = new();

        /// <summary>
        /// Serializer for JSON serialization/deserialization
        /// </summary>
        protected readonly ISerializer _serializer;

        /// <summary>
        /// Disposal flag to prevent double-disposal
        /// </summary>
        protected bool _disposed;

        // Optional write batching infrastructure (null if batching disabled)
        private readonly List<PendingWrite>? _writeBuffer;
        private readonly SemaphoreSlim? _writeLock;
        private readonly Timer? _flushTimer;
        private readonly int _batchThreshold;
        private readonly int _flushIntervalMs;

        /// <summary>
        /// Represents a write operation pending in the batch buffer.
        /// Contains pre-processed data (already through IRoot pipeline).
        /// </summary>
        protected struct PendingWrite
        {
            public string Id;
            public byte[] ProcessedData;  // Already through IRoot pipeline
            public DateTime Timestamp;
            public int Version;
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialize base trunk with optional serializer and write batching
        /// </summary>
        /// <param name="serializer">Custom serializer (defaults to NewtonsoftJsonSerializer)</param>
        /// <param name="enableBatching">Enable write batching for performance optimization</param>
        /// <param name="batchThreshold">Number of writes to buffer before auto-flush (default: 100)</param>
        /// <param name="flushIntervalMs">Flush interval in milliseconds (default: 200ms)</param>
        protected TrunkBase(
            ISerializer? serializer = null,
            bool enableBatching = false,
            int batchThreshold = 100,
            int flushIntervalMs = 200)
        {
            _serializer = serializer ?? new NewtonsoftJsonSerializer();
            _batchThreshold = batchThreshold;
            _flushIntervalMs = flushIntervalMs;

            if (enableBatching)
            {
                _writeBuffer = new List<PendingWrite>(batchThreshold);
                _writeLock = new SemaphoreSlim(1, 1);

                // Auto-flush timer for write batching
                _flushTimer = new Timer(async _ =>
                {
                    try
                    {
                        await FlushBatchAsync();
                    }
                    catch (Exception ex)
                    {
                        // Log but don't throw from timer callback
                        AcornLog.Error($"⚠️ Write batch flush failed: {ex.Message}");
                    }
                }, null, flushIntervalMs, flushIntervalMs);
            }
        }

        #endregion

        #region IRoot Pipeline Implementation (UNIFIED - No Duplication)

        /// <summary>
        /// Get all registered root processors (thread-safe)
        /// </summary>
        public IReadOnlyList<IRoot> Roots
        {
            get
            {
                lock (_rootsLock)
                {
                    return _roots.ToList();
                }
            }
        }

        /// <summary>
        /// Add a root processor to the transformation pipeline.
        /// Roots are automatically sorted by sequence number.
        /// </summary>
        /// <param name="root">Root processor to add</param>
        public void AddRoot(IRoot root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            lock (_rootsLock)
            {
                _roots.Add(root);
                // Sort by sequence to ensure correct execution order
                _roots.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            }
        }

        /// <summary>
        /// Remove a root processor from the transformation pipeline
        /// </summary>
        /// <param name="name">Name of the root to remove</param>
        /// <returns>True if removed, false if not found</returns>
        public bool RemoveRoot(string name)
        {
            lock (_rootsLock)
            {
                var root = _roots.FirstOrDefault(r => r.Name == name);
                if (root != null)
                {
                    _roots.Remove(root);
                    return true;
                }
                return false;
            }
        }

        #endregion

        #region IRoot Processing Helpers (UNIFIED)

        /// <summary>
        /// Process byte array through IRoot chain in ascending sequence order (for writes).
        /// Each root transforms the bytes sequentially: compression → encryption → policy
        /// </summary>
        /// <param name="data">Input bytes (typically JSON)</param>
        /// <param name="documentId">Document ID for context</param>
        /// <returns>Processed bytes after all roots</returns>
        protected byte[] ProcessThroughRootsAscending(byte[] data, string documentId)
        {
            // No roots? Return data unchanged
            if (_roots.Count == 0) return data;

            var context = new RootProcessingContext
            {
                PolicyContext = new PolicyContext { Operation = "Write" },
                DocumentId = documentId
            };

            var processedBytes = data;
            lock (_rootsLock)
            {
                foreach (var root in _roots)
                {
                    processedBytes = root.OnStash(processedBytes, context);
                }
            }

            return processedBytes;
        }

        /// <summary>
        /// Process byte array through IRoot chain in descending sequence order (for reads).
        /// Each root reverses its transformation: policy → encryption → compression
        /// </summary>
        /// <param name="data">Input bytes (from storage)</param>
        /// <param name="documentId">Document ID for context</param>
        /// <returns>Processed bytes after all roots</returns>
        protected byte[] ProcessThroughRootsDescending(byte[] data, string documentId)
        {
            // No roots? Return data unchanged
            if (_roots.Count == 0) return data;

            var context = new RootProcessingContext
            {
                PolicyContext = new PolicyContext { Operation = "Read" },
                DocumentId = documentId
            };

            var processedBytes = data;
            lock (_rootsLock)
            {
                // Reverse iteration for read path
                for (int i = _roots.Count - 1; i >= 0; i--)
                {
                    processedBytes = _roots[i].OnCrack(processedBytes, context);
                }
            }

            return processedBytes;
        }

        /// <summary>
        /// Helper to detect if data needs Base64 decoding (backward compatibility).
        /// Tries to decode as Base64, falls back to treating as plain UTF8.
        /// </summary>
        /// <param name="data">String data from storage</param>
        /// <returns>Byte array (Base64 decoded or UTF8 encoded)</returns>
        protected byte[] DecodeStoredData(string data)
        {
            try
            {
                // Try Base64 decode (IRoot pipeline data)
                return Convert.FromBase64String(data);
            }
            catch (FormatException)
            {
                // Not Base64 - it's plain JSON (backward compatibility)
                return Encoding.UTF8.GetBytes(data);
            }
        }

        /// <summary>
        /// Helper to encode data for storage.
        /// Uses Base64 if roots are present, otherwise returns plain JSON.
        /// </summary>
        /// <param name="processedBytes">Bytes after root processing</param>
        /// <param name="json">Original JSON (for no-root case)</param>
        /// <returns>String to store</returns>
        protected string EncodeForStorage(byte[] processedBytes, string json)
        {
            return _roots.Count > 0
                ? Convert.ToBase64String(processedBytes)
                : json;
        }

        #endregion

        #region Write Batching Support (OPTIONAL - Available to Derived Classes)

        /// <summary>
        /// Helper for derived classes to stash with batching support.
        /// Processes through IRoot pipeline, then either batches or writes immediately.
        /// </summary>
        /// <param name="id">Document ID</param>
        /// <param name="nut">Nut to stash</param>
        protected async Task StashWithBatchingAsync(string id, Nut<T> nut)
        {
            // Process through IRoot pipeline first
            var json = _serializer.Serialize(nut);
            var bytes = Encoding.UTF8.GetBytes(json);
            var processedBytes = ProcessThroughRootsAscending(bytes, id);

            if (_writeBuffer == null)
            {
                // No batching enabled - write immediately
                await WriteToStorageAsync(id, processedBytes, nut.Timestamp, nut.Version);
                return;
            }

            // Add to batch buffer
            bool shouldFlush = false;
            lock (_writeBuffer)
            {
                _writeBuffer.Add(new PendingWrite
                {
                    Id = id,
                    ProcessedData = processedBytes,
                    Timestamp = nut.Timestamp,
                    Version = nut.Version
                });

                // Check if buffer reached threshold
                if (_writeBuffer.Count >= _batchThreshold)
                {
                    shouldFlush = true;
                }
            }

            // Flush outside the lock to avoid contention
            if (shouldFlush)
            {
                await FlushBatchAsync();
            }
        }

        /// <summary>
        /// Flush all pending writes to storage.
        /// Called automatically by timer or when batch threshold is reached.
        /// Can also be called explicitly by derived classes.
        /// </summary>
        protected async Task FlushBatchAsync()
        {
            if (_writeBuffer == null || _writeLock == null) return;

            List<PendingWrite> toWrite;

            // Copy buffer and clear under lock
            lock (_writeBuffer)
            {
                if (_writeBuffer.Count == 0) return;
                toWrite = new List<PendingWrite>(_writeBuffer);
                _writeBuffer.Clear();
            }

            // Write outside the lock
            await _writeLock.WaitAsync();
            try
            {
                await WriteBatchToStorageAsync(toWrite);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Check if write batching is enabled for this trunk
        /// </summary>
        protected bool IsBatchingEnabled => _writeBuffer != null;

        /// <summary>
        /// Get current number of pending writes in the batch buffer (0 if batching disabled)
        /// </summary>
        protected int PendingWriteCount
        {
            get
            {
                if (_writeBuffer == null) return 0;
                lock (_writeBuffer)
                {
                    return _writeBuffer.Count;
                }
            }
        }

        #endregion

        #region Abstract/Virtual Methods for Batching (Optional - Override if using batching)

        /// <summary>
        /// Write a single item to storage (used when batching is disabled or for immediate writes).
        /// Override this if your trunk uses StashWithBatchingAsync.
        /// </summary>
        /// <param name="id">Document ID</param>
        /// <param name="data">Processed bytes (already through IRoot pipeline)</param>
        /// <param name="timestamp">Timestamp</param>
        /// <param name="version">Version</param>
        protected virtual Task WriteToStorageAsync(string id, byte[] data, DateTime timestamp, int version)
        {
            throw new NotImplementedException(
                "Trunk uses batching but did not implement WriteToStorageAsync. " +
                "Override this method to support immediate writes.");
        }

        /// <summary>
        /// Write a batch of items to storage (used when batching is enabled).
        /// Override this if your trunk uses StashWithBatchingAsync.
        /// Default implementation calls WriteToStorageAsync for each item.
        /// </summary>
        /// <param name="batch">Batch of pending writes</param>
        protected virtual async Task WriteBatchToStorageAsync(List<PendingWrite> batch)
        {
            // Default: Write each item individually
            // Derived classes should override for true batch optimization
            foreach (var write in batch)
            {
                await WriteToStorageAsync(write.Id, write.ProcessedData, write.Timestamp, write.Version);
            }
        }

        #endregion

        #region Abstract Methods (Storage-Specific - Implemented by Derived Classes)

        /// <summary>
        /// Store a nut in the underlying storage system
        /// </summary>
        public abstract void Stash(string id, Nut<T> nut);

        /// <summary>
        /// Retrieve a nut from the underlying storage system
        /// </summary>
        public abstract Nut<T>? Crack(string id);

        /// <summary>
        /// Delete a nut from the underlying storage system
        /// </summary>
        public abstract void Toss(string id);

        /// <summary>
        /// Retrieve all nuts from the underlying storage system
        /// </summary>
        public abstract IEnumerable<Nut<T>> CrackAll();

        /// <summary>
        /// Get version history for a document (if supported by trunk)
        /// </summary>
        public abstract IReadOnlyList<Nut<T>> GetHistory(string id);

        /// <summary>
        /// Export all changes for sync operations
        /// </summary>
        public abstract IEnumerable<Nut<T>> ExportChanges();

        /// <summary>
        /// Import changes from sync operations
        /// </summary>
        public abstract void ImportChanges(IEnumerable<Nut<T>> incoming);

        /// <summary>
        /// Get trunk capabilities metadata
        /// </summary>
        public abstract ITrunkCapabilities Capabilities { get; }

        #endregion

        #region Obsolete Methods (Backward Compatibility)

        /// <summary>
        /// [Obsolete] Use Stash() instead
        /// </summary>
        [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
        public void Save(string id, Nut<T> nut) => Stash(id, nut);

        /// <summary>
        /// [Obsolete] Use Crack() instead
        /// </summary>
        [Obsolete("Use Crack() instead. This method will be removed in a future version.")]
        public Nut<T>? Load(string id) => Crack(id);

        /// <summary>
        /// [Obsolete] Use Toss() instead
        /// </summary>
        [Obsolete("Use Toss() instead. This method will be removed in a future version.")]
        public void Delete(string id) => Toss(id);

        /// <summary>
        /// [Obsolete] Use CrackAll() instead
        /// </summary>
        [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
        public IEnumerable<Nut<T>> LoadAll() => CrackAll();

        #endregion

        #region Disposal Pattern

        /// <summary>
        /// Dispose of trunk resources. Override in derived classes to add storage-specific cleanup.
        /// Always call base.Dispose() when overriding.
        /// </summary>
        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop the flush timer first
            _flushTimer?.Dispose();

            // Flush any pending batched writes
            if (_writeBuffer != null)
            {
                try
                {
                    FlushBatchAsync().Wait();
                }
                catch (Exception ex)
                {
                    AcornLog.Error($"⚠️ ERROR: Failed to flush write batch during disposal: {ex.Message}");
                    // Don't rethrow - disposal must succeed to release resources
                }
            }

            // Dispose synchronization primitives
            _writeLock?.Dispose();

            // Derived classes should override and add their own cleanup
            // e.g., close connections, dispose file streams, etc.
        }

        #endregion
    }
}
