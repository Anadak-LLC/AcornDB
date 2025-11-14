using System;
using AcornDB.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AcornDB.Sync
{
    /// <summary>
    /// MetricsBranch: Example observability branch that tracks operation metrics
    /// Demonstrates non-sync use of IBranch for monitoring/telemetry
    /// </summary>
    public class MetricsBranch : IBranch
    {
        public string BranchId { get; }
        public BranchCapabilities Capabilities => BranchCapabilities.All;

        private readonly object _lock = new object();
        private long _stashCount = 0;
        private long _tossCount = 0;
        private long _squabbleCount = 0;
        private long _shakeCount = 0;
        private DateTime _startTime = DateTime.UtcNow;
        private DateTime? _lastEventTime = null;

        // Track operations per tree
        private readonly Dictionary<string, TreeMetrics> _treeMetrics = new();

        // Track hop count distribution
        private readonly Dictionary<int, long> _hopDistribution = new();

        private bool _isDisposed;

        public MetricsBranch()
        {
            BranchId = $"metrics-{Guid.NewGuid().ToString().Substring(0, 8)}";
        }

        /// <summary>
        /// Get overall metrics summary
        /// </summary>
        public MetricsSummary GetSummary()
        {
            lock (_lock)
            {
                var uptime = DateTime.UtcNow - _startTime;
                var totalOperations = _stashCount + _tossCount + _squabbleCount;

                return new MetricsSummary
                {
                    TotalStash = _stashCount,
                    TotalToss = _tossCount,
                    TotalSquabble = _squabbleCount,
                    TotalShake = _shakeCount,
                    TotalOperations = totalOperations,
                    OperationsPerSecond = uptime.TotalSeconds > 0 ? totalOperations / uptime.TotalSeconds : 0,
                    Uptime = uptime,
                    LastEventTime = _lastEventTime,
                    UniqueTreesSeen = _treeMetrics.Count
                };
            }
        }

        /// <summary>
        /// Get metrics for a specific tree
        /// </summary>
        public TreeMetrics? GetTreeMetrics(string treeId)
        {
            lock (_lock)
            {
                return _treeMetrics.TryGetValue(treeId, out var metrics) ? metrics : null;
            }
        }

        /// <summary>
        /// Get all tree metrics
        /// </summary>
        public Dictionary<string, TreeMetrics> GetAllTreeMetrics()
        {
            lock (_lock)
            {
                return new Dictionary<string, TreeMetrics>(_treeMetrics);
            }
        }

        /// <summary>
        /// Get hop count distribution
        /// </summary>
        public Dictionary<int, long> GetHopDistribution()
        {
            lock (_lock)
            {
                return new Dictionary<int, long>(_hopDistribution);
            }
        }

        /// <summary>
        /// Reset all metrics
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _stashCount = 0;
                _tossCount = 0;
                _squabbleCount = 0;
                _shakeCount = 0;
                _startTime = DateTime.UtcNow;
                _lastEventTime = null;
                _treeMetrics.Clear();
                _hopDistribution.Clear();
            }
        }

        public void OnStash<T>(Leaf<T> leaf)
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                _stashCount++;
                _lastEventTime = DateTime.UtcNow;
                TrackTreeMetrics(leaf.OriginTreeId, "stash");
                TrackHopCount(leaf.HopCount);
            }
        }

        public void OnToss<T>(Leaf<T> leaf)
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                _tossCount++;
                _lastEventTime = DateTime.UtcNow;
                TrackTreeMetrics(leaf.OriginTreeId, "toss");
                TrackHopCount(leaf.HopCount);
            }
        }

        public void OnSquabble<T>(Leaf<T> leaf)
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                _squabbleCount++;
                _lastEventTime = DateTime.UtcNow;
                TrackTreeMetrics(leaf.OriginTreeId, "squabble");
                TrackHopCount(leaf.HopCount);
            }
        }

        public Task OnShakeAsync<T>(Tree<T> tree) where T : class
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                _shakeCount++;
                _lastEventTime = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }

        public void FlushBatch()
        {
            // No batching needed for metrics
        }

        public string? GetRemoteTreeId()
        {
            // Metrics branches don't have remote trees
            return null;
        }

        private void TrackTreeMetrics(string treeId, string operation)
        {
            if (!_treeMetrics.TryGetValue(treeId, out var metrics))
            {
                metrics = new TreeMetrics { TreeId = treeId };
                _treeMetrics[treeId] = metrics;
            }

            switch (operation)
            {
                case "stash":
                    metrics.StashCount++;
                    break;
                case "toss":
                    metrics.TossCount++;
                    break;
                case "squabble":
                    metrics.SquabbleCount++;
                    break;
            }

            metrics.LastSeen = DateTime.UtcNow;
        }

        private void TrackHopCount(int hopCount)
        {
            if (!_hopDistribution.ContainsKey(hopCount))
            {
                _hopDistribution[hopCount] = 0;
            }
            _hopDistribution[hopCount]++;
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(MetricsBranch),
                    $"Cannot use metrics branch {BranchId} - it has been disposed.");
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            var summary = GetSummary();
            AcornLog.Info($"> 📊 MetricsBranch {BranchId} disposed - {summary.TotalOperations} operations tracked");

            GC.SuppressFinalize(this);
        }
    }
}
