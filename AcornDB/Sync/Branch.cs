// Placeholder for Branch.cs


using System.Text;
using AcornDB.Logging;
using System.Text.Json;

namespace AcornDB.Sync
{
    public partial class Branch : IBranch
    {
        public string BranchId { get; }
        public string RemoteUrl { get; }
        public SyncMode SyncMode { get; set; } = SyncMode.Bidirectional;
        public ConflictDirection ConflictDirection { get; set; } = ConflictDirection.UseJudge;

        /// <summary>
        /// Capabilities supported by this branch (all by default)
        /// </summary>
        public BranchCapabilities Capabilities => BranchCapabilities.All;

        private readonly HttpClient _httpClient;
        private readonly HashSet<string> _pushedNuts = new(); // Track pushed nuts to avoid re-pushing
        private readonly HashSet<string> _deletedNuts = new(); // Track deleted nuts to avoid re-deleting
        private long _pullCount = 0; // Track nuts pulled from remote
        private long _conflictCount = 0; // Track conflicts resolved
        private DateTime _lastSyncTimestamp = DateTime.MinValue; // Track last successful sync for delta sync
        private bool _useDeltaSync = true; // Enable incremental/delta sync by default
        private bool _isDisposed;

        // Batching support
        private readonly List<BatchOperation> _batchQueue = new();
        private readonly object _batchLock = new();
        private System.Threading.Timer? _batchTimer;
        private int _batchSize = 10; // Max operations per batch
        private int _batchTimeoutMs = 100; // Auto-flush after 100ms
        private bool _batchingEnabled = false; // Batching disabled by default for backwards compatibility

        public Branch(string remoteUrl, SyncMode syncMode = SyncMode.Bidirectional)
        {
            BranchId = $"remote-{Guid.NewGuid().ToString().Substring(0, 8)}";
            RemoteUrl = remoteUrl.TrimEnd('/');
            SyncMode = syncMode;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Configure sync mode (fluent API)
        /// </summary>
        public Branch WithSyncMode(SyncMode syncMode)
        {
            SyncMode = syncMode;
            return this;
        }

        /// <summary>
        /// Configure conflict direction (fluent API)
        /// </summary>
        public Branch WithConflictDirection(ConflictDirection conflictDirection)
        {
            ConflictDirection = conflictDirection;
            return this;
        }

        /// <summary>
        /// Configure delta sync (fluent API)
        /// When enabled, only syncs changes since last successful sync
        /// </summary>
        public Branch WithDeltaSync(bool enabled)
        {
            _useDeltaSync = enabled;
            return this;
        }

        /// <summary>
        /// Enable batching for push/delete operations (fluent API)
        /// Batches multiple operations together to reduce network overhead
        /// </summary>
        /// <param name="batchSize">Maximum number of operations per batch (default: 10)</param>
        /// <param name="batchTimeoutMs">Auto-flush timeout in milliseconds (default: 100ms)</param>
        public Branch WithBatching(int batchSize = 10, int batchTimeoutMs = 100)
        {
            _batchingEnabled = true;
            _batchSize = batchSize;
            _batchTimeoutMs = batchTimeoutMs;
            InitializeBatchTimer();
            return this;
        }

        private void InitializeBatchTimer()
        {
            if (_batchTimer != null) return;

            _batchTimer = new System.Threading.Timer(
                callback: _ => FlushBatch(),
                state: null,
                dueTime: _batchTimeoutMs,
                period: _batchTimeoutMs
            );
        }

        public virtual void TryPush<T>(string id, Nut<T> shell)
        {
            ThrowIfDisposed();

            // Respect sync mode - only push if push is enabled
            if (SyncMode == SyncMode.PullOnly || SyncMode == SyncMode.Disabled)
                return;

            // Check if we've already pushed this nut to avoid duplicates
            var nutKey = $"{id}:{shell.Timestamp.Ticks}";
            if (_pushedNuts.Contains(nutKey))
                return;

            _pushedNuts.Add(nutKey);

            // Use batching if enabled
            if (_batchingEnabled)
            {
                AddToBatch(new BatchOperation
                {
                    Type = BatchOperationType.Push,
                    Id = id,
                    Nut = shell,
                    TypeName = typeof(T).Name
                });
            }
            else
            {
                _ = PushAsync(id, shell);
            }
        }

        public virtual void TryDelete<T>(string id)
        {
            ThrowIfDisposed();

            // Respect sync mode - only delete if push is enabled
            if (SyncMode == SyncMode.PullOnly || SyncMode == SyncMode.Disabled)
                return;

            // Check if we've already deleted this nut to avoid duplicates
            if (_deletedNuts.Contains(id))
                return;

            _deletedNuts.Add(id);

            // Use batching if enabled
            if (_batchingEnabled)
            {
                AddToBatch(new BatchOperation
                {
                    Type = BatchOperationType.Delete,
                    Id = id,
                    TypeName = typeof(T).Name
                });
            }
            else
            {
                _ = DeleteAsync<T>(id);
            }
        }

        private async Task DeleteAsync<T>(string id)
        {
            try
            {
                var treeName = typeof(T).Name.ToLowerInvariant();
                var endpoint = $"{RemoteUrl}/bark/{treeName}/toss/{id}";

                var response = await _httpClient.DeleteAsync(endpoint);

                if (!response.IsSuccessStatusCode)
                {
                    AcornLog.Info($"> Failed to delete nut {id} from {RemoteUrl}: {response.StatusCode}");
                }
                else
                {
                    AcornLog.Info($"> Nut {id} deleted from {RemoteUrl}.");
                }
            }
            catch (Exception ex)
            {
                AcornLog.Info($"> Branch delete failed: {ex.Message}");
            }
        }

        private async Task PushAsync<T>(string id, Nut<T> shell)
        {
            try
            {
                var json = JsonSerializer.Serialize(shell);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var treeName = typeof(T).Name.ToLowerInvariant(); // naive default mapping
                var endpoint = $"{RemoteUrl}/bark/{treeName}/stash";

                var response = await _httpClient.PostAsync(endpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    AcornLog.Info($"> Failed to push nut {id} to {RemoteUrl}: {response.StatusCode}");
                }
                else
                {
                    AcornLog.Info($"> Nut {id} synced to {RemoteUrl}.");
                }
            }
            catch (Exception ex)
            {
                AcornLog.Info($"> Branch push failed: {ex.Message}");
            }
        }

        public virtual async Task ShakeAsync<T>(Tree<T> targetTree) where T : class
        {
            ThrowIfDisposed();

            // Respect sync mode - only pull if pull is enabled
            if (SyncMode == SyncMode.PushOnly || SyncMode == SyncMode.Disabled)
                return;

            try
            {
                var treeName = typeof(T).Name.ToLowerInvariant();

                // Determine if we should use delta sync
                bool isFirstSync = _lastSyncTimestamp == DateTime.MinValue;
                bool useDelta = _useDeltaSync && !isFirstSync;

                // Build endpoint with optional delta sync parameter
                var endpoint = $"{RemoteUrl}/bark/{treeName}/export";
                if (useDelta)
                {
                    endpoint += $"?since={_lastSyncTimestamp.Ticks}";
                }

                var response = await _httpClient.GetAsync(endpoint);
                if (!response.IsSuccessStatusCode)
                {
                    // If delta sync failed, fallback to full sync
                    if (useDelta)
                    {
                        AcornLog.Info($"> Delta sync failed from {RemoteUrl}: {response.StatusCode}, falling back to full sync");
                        endpoint = $"{RemoteUrl}/bark/{treeName}/export";
                        response = await _httpClient.GetAsync(endpoint);

                        if (!response.IsSuccessStatusCode)
                        {
                            AcornLog.Info($"> Failed to shake branch from {RemoteUrl}: {response.StatusCode}");
                            return;
                        }
                    }
                    else
                    {
                        AcornLog.Info($"> Failed to shake branch from {RemoteUrl}: {response.StatusCode}");
                        return;
                    }
                }

                var json = await response.Content.ReadAsStringAsync();
                var nuts = JsonSerializer.Deserialize<List<Nut<T>>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (nuts == null) return;

                foreach (var nut in nuts)
                {
                    // Check if nut exists (potential conflict)
                    bool isConflict = targetTree.Crack(nut.Id) != null;

                    // Use the branch's conflict direction preference
                    targetTree.Squabble(nut.Id, nut, ConflictDirection);

                    // Track statistics
                    System.Threading.Interlocked.Increment(ref _pullCount);
                    if (isConflict)
                    {
                        System.Threading.Interlocked.Increment(ref _conflictCount);
                    }
                }

                // Update last sync timestamp for next delta sync
                _lastSyncTimestamp = DateTime.UtcNow;

                var syncType = useDelta ? "delta" : (isFirstSync ? "initial" : "full");
                AcornLog.Info($"> Shake complete ({syncType}): {nuts.Count} nuts received from {RemoteUrl}");
            }
            catch (Exception ex)
            {
                AcornLog.Info($"> Branch shake failed: {ex.Message}");
            }
        }

        // ===== IBranch Interface Implementation =====

        /// <summary>
        /// Handle a stash leaf (IBranch interface)
        /// </summary>
        public void OnStash<T>(Leaf<T> leaf)
        {
            if (leaf.Data != null)
            {
                TryPush(leaf.Key, leaf.Data);
            }
        }

        /// <summary>
        /// Handle a toss leaf (IBranch interface)
        /// </summary>
        public void OnToss<T>(Leaf<T> leaf)
        {
            TryDelete<T>(leaf.Key);
        }

        /// <summary>
        /// Handle a squabble leaf (IBranch interface)
        /// </summary>
        public void OnSquabble<T>(Leaf<T> leaf)
        {
            // Squabbles are treated like stashes for remote sync
            if (leaf.Data != null)
            {
                TryPush(leaf.Key, leaf.Data);
            }
        }

        /// <summary>
        /// Handle shake operation (IBranch interface)
        /// </summary>
        public async Task OnShakeAsync<T>(Tree<T> tree) where T : class
        {
            await ShakeAsync(tree);
        }

        /// <summary>
        /// Flush any batched operations to the remote
        /// Sends all pending operations in a single HTTP request
        /// </summary>
        public void FlushBatch()
        {
            if (!_batchingEnabled) return;

            List<BatchOperation> operationsToFlush;

            lock (_batchLock)
            {
                if (_batchQueue.Count == 0) return;

                // Copy batch and clear queue
                operationsToFlush = new List<BatchOperation>(_batchQueue);
                _batchQueue.Clear();
            }

            // Send batch asynchronously (fire and forget)
            _ = SendBatchAsync(operationsToFlush);
        }

        private void AddToBatch(BatchOperation operation)
        {
            lock (_batchLock)
            {
                _batchQueue.Add(operation);

                // Auto-flush if batch size reached
                if (_batchQueue.Count >= _batchSize)
                {
                    FlushBatch();
                }
            }
        }

        private async Task SendBatchAsync(List<BatchOperation> operations)
        {
            try
            {
                // Group operations by type and tree
                var grouped = operations.GroupBy(op => new { op.TypeName, op.Type });

                foreach (var group in grouped)
                {
                    var treeName = group.Key.TypeName.ToLowerInvariant();
                    var operationType = group.Key.Type;

                    if (operationType == BatchOperationType.Push)
                    {
                        // Batch push operations
                        var endpoint = $"{RemoteUrl}/bark/{treeName}/batch/stash";
                        var nuts = group.Select(op => op.Nut).ToList();
                        var json = JsonSerializer.Serialize(nuts);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PostAsync(endpoint, content);
                        if (response.IsSuccessStatusCode)
                        {
                            AcornLog.Info($"> Batch push: {nuts.Count} nuts synced to {RemoteUrl}");
                        }
                        else
                        {
                            AcornLog.Info($"> Batch push failed: {response.StatusCode}");
                        }
                    }
                    else if (operationType == BatchOperationType.Delete)
                    {
                        // Batch delete operations
                        var endpoint = $"{RemoteUrl}/bark/{treeName}/batch/toss";
                        var ids = group.Select(op => op.Id).ToList();
                        var json = JsonSerializer.Serialize(ids);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        var response = await _httpClient.PostAsync(endpoint, content);
                        if (response.IsSuccessStatusCode)
                        {
                            AcornLog.Info($"> Batch delete: {ids.Count} nuts deleted from {RemoteUrl}");
                        }
                        else
                        {
                            AcornLog.Info($"> Batch delete failed: {response.StatusCode}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AcornLog.Info($"> Batch send failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the remote tree ID (if known - currently not tracked)
        /// </summary>
        public string? GetRemoteTreeId()
        {
            // TODO: In future, exchange tree IDs during handshake
            // For now, we don't track remote tree IDs
            return null;
        }

        /// <summary>
        /// Clear the tracking of pushed nuts to allow re-pushing
        /// Useful when you want to force a full re-sync
        /// </summary>
        public void ClearPushHistory()
        {
            _pushedNuts.Clear();
            _deletedNuts.Clear();
        }

        /// <summary>
        /// Get statistics about this branch
        /// </summary>
        public BranchStats GetStats()
        {
            return new BranchStats
            {
                RemoteUrl = RemoteUrl,
                SyncMode = SyncMode,
                ConflictDirection = ConflictDirection,
                TotalPushed = _pushedNuts.Count,
                TotalDeleted = _deletedNuts.Count,
                TotalPulled = _pullCount,
                TotalConflicts = _conflictCount,
                DeltaSyncEnabled = _useDeltaSync,
                LastSyncTimestamp = _lastSyncTimestamp
            };
        }

        /// <summary>
        /// Snap the branch connection (nutty alias for Dispose)
        /// Disconnects from remote and releases resources
        /// </summary>
        public void Snap()
        {
            if (!_isDisposed)
            {
                AcornLog.Info($"> 🪓 Branch to {RemoteUrl} snapped!");
            }
            Dispose();
        }

        /// <summary>
        /// Dispose of the branch and release resources
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            // Flush any remaining batched operations
            if (_batchingEnabled)
            {
                FlushBatch();
            }

            // Dispose batch timer
            _batchTimer?.Dispose();

            // Dispose HttpClient
            _httpClient?.Dispose();

            // Clear internal state
            _pushedNuts.Clear();
            _deletedNuts.Clear();
            lock (_batchLock)
            {
                _batchQueue.Clear();
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Check if this branch has been disposed
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(Branch),
                    $"Cannot use branch to {RemoteUrl} - it has been snapped (disposed).");
            }
        }
    }

    /// <summary>
    /// Represents a batched operation (push or delete)
    /// </summary>
    internal class BatchOperation
    {
        public BatchOperationType Type { get; set; }
        public string Id { get; set; } = "";
        public object? Nut { get; set; }
        public string TypeName { get; set; } = "";
    }

    /// <summary>
    /// Type of batch operation
    /// </summary>
    internal enum BatchOperationType
    {
        Push,
        Delete
    }
}
