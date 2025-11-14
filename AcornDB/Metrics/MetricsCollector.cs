using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AcornDB;

namespace AcornDB.Metrics
{
    /// <summary>
    /// Collects and exports metrics for AcornDB operations.
    /// Compatible with Prometheus and OpenTelemetry exporters.
    ///
    /// Tracked Metrics:
    /// - Operation counts (stash, crack, toss, squabble)
    /// - Operation latencies (P50, P95, P99)
    /// - Sync statistics (pushes, pulls, conflicts)
    /// - Cache performance (hit rate, evictions)
    /// - Trunk health (errors, retries, fallbacks)
    /// - Tree/Grove counts and sizes
    /// </summary>
    public class MetricsCollector
    {
        private static readonly Lazy<MetricsCollector> _instance = new Lazy<MetricsCollector>(() => new MetricsCollector());

        public static MetricsCollector Instance => _instance.Value;

        // Operation counters
        private long _totalStashes = 0;
        private long _totalCracks = 0;
        private long _totalTosses = 0;
        private long _totalSquabbles = 0;
        private long _totalShakes = 0;

        // Sync counters
        private long _totalPushes = 0;
        private long _totalPulls = 0;
        private long _totalConflicts = 0;
        private long _totalSyncErrors = 0;

        // Cache metrics
        private long _cacheHits = 0;
        private long _cacheMisses = 0;
        private long _cacheEvictions = 0;

        // Resilience metrics
        private long _totalRetries = 0;
        private long _totalFallbacks = 0;
        private long _circuitBreakerTrips = 0;

        // Latency tracking (simplified histogram)
        private readonly ConcurrentBag<double> _stashLatencies = new();
        private readonly ConcurrentBag<double> _crackLatencies = new();
        private readonly ConcurrentBag<double> _tossLatencies = new();

        // Tree/Grove tracking
        private readonly ConcurrentDictionary<string, TreeMetrics> _treeMetrics = new();
        private int _activeGroves = 0;
        private int _activeTangles = 0;

        // Labels for grouping metrics
        private readonly Dictionary<string, string> _labels = new();

        private MetricsCollector()
        {
            // Default labels
            _labels["application"] = "AcornDB";
            _labels["version"] = "0.4.0";
        }

        /// <summary>
        /// Add custom label for metrics (e.g., environment, region, instance)
        /// </summary>
        public void AddLabel(string key, string value)
        {
            _labels[key] = value;
        }

        // ===== Operation Metrics =====

        public void RecordStash(string treeId, double durationMs)
        {
            System.Threading.Interlocked.Increment(ref _totalStashes);
            _stashLatencies.Add(durationMs);
            GetOrCreateTreeMetrics(treeId).RecordStash();
        }

        public void RecordCrack(string treeId, double durationMs, bool cacheHit)
        {
            System.Threading.Interlocked.Increment(ref _totalCracks);
            _crackLatencies.Add(durationMs);

            if (cacheHit)
                System.Threading.Interlocked.Increment(ref _cacheHits);
            else
                System.Threading.Interlocked.Increment(ref _cacheMisses);

            GetOrCreateTreeMetrics(treeId).RecordCrack(cacheHit);
        }

        public void RecordToss(string treeId, double durationMs)
        {
            System.Threading.Interlocked.Increment(ref _totalTosses);
            _tossLatencies.Add(durationMs);
            GetOrCreateTreeMetrics(treeId).RecordToss();
        }

        public void RecordSquabble(string treeId)
        {
            System.Threading.Interlocked.Increment(ref _totalSquabbles);
            GetOrCreateTreeMetrics(treeId).RecordSquabble();
        }

        public void RecordShake(string treeId)
        {
            System.Threading.Interlocked.Increment(ref _totalShakes);
        }

        // ===== Sync Metrics =====

        public void RecordPush(string branchId, int count = 1)
        {
            System.Threading.Interlocked.Add(ref _totalPushes, count);
        }

        public void RecordPull(string branchId, int count = 1)
        {
            System.Threading.Interlocked.Add(ref _totalPulls, count);
        }

        public void RecordConflict(string treeId)
        {
            System.Threading.Interlocked.Increment(ref _totalConflicts);
            GetOrCreateTreeMetrics(treeId).RecordConflict();
        }

        public void RecordSyncError(string branchId)
        {
            System.Threading.Interlocked.Increment(ref _totalSyncErrors);
        }

        // ===== Cache Metrics =====

        public void RecordCacheEviction(int count = 1)
        {
            System.Threading.Interlocked.Add(ref _cacheEvictions, count);
        }

        // ===== Resilience Metrics =====

        public void RecordRetry(string trunkId)
        {
            System.Threading.Interlocked.Increment(ref _totalRetries);
        }

        public void RecordFallback(string trunkId)
        {
            System.Threading.Interlocked.Increment(ref _totalFallbacks);
        }

        public void RecordCircuitBreakerTrip(string trunkId)
        {
            System.Threading.Interlocked.Increment(ref _circuitBreakerTrips);
        }

        // ===== Tree/Grove Tracking =====

        public void RegisterTree(string treeId, string treeType)
        {
            GetOrCreateTreeMetrics(treeId).TreeType = treeType;
        }

        public void UnregisterTree(string treeId)
        {
            _treeMetrics.TryRemove(treeId, out _);
        }

        public void UpdateTreeNutCount(string treeId, int nutCount)
        {
            GetOrCreateTreeMetrics(treeId).NutCount = nutCount;
        }

        public void SetActiveGroves(int count)
        {
            _activeGroves = count;
        }

        public void SetActiveTangles(int count)
        {
            _activeTangles = count;
        }

        private TreeMetrics GetOrCreateTreeMetrics(string treeId)
        {
            return _treeMetrics.GetOrAdd(treeId, _ => new TreeMetrics { TreeId = treeId });
        }

        // ===== Metrics Export =====

        /// <summary>
        /// Export metrics in Prometheus text format
        /// </summary>
        public string ExportPrometheus()
        {
            var sb = new StringBuilder();
            var labels = FormatLabels();

            // Operation counters
            sb.AppendLine($"# HELP acorndb_stash_total Total number of stash operations");
            sb.AppendLine($"# TYPE acorndb_stash_total counter");
            sb.AppendLine($"acorndb_stash_total{labels} {_totalStashes}");

            sb.AppendLine($"# HELP acorndb_crack_total Total number of crack operations");
            sb.AppendLine($"# TYPE acorndb_crack_total counter");
            sb.AppendLine($"acorndb_crack_total{labels} {_totalCracks}");

            sb.AppendLine($"# HELP acorndb_toss_total Total number of toss operations");
            sb.AppendLine($"# TYPE acorndb_toss_total counter");
            sb.AppendLine($"acorndb_toss_total{labels} {_totalTosses}");

            sb.AppendLine($"# HELP acorndb_squabble_total Total number of conflicts resolved");
            sb.AppendLine($"# TYPE acorndb_squabble_total counter");
            sb.AppendLine($"acorndb_squabble_total{labels} {_totalSquabbles}");

            // Sync metrics
            sb.AppendLine($"# HELP acorndb_sync_push_total Total nuts pushed to remote");
            sb.AppendLine($"# TYPE acorndb_sync_push_total counter");
            sb.AppendLine($"acorndb_sync_push_total{labels} {_totalPushes}");

            sb.AppendLine($"# HELP acorndb_sync_pull_total Total nuts pulled from remote");
            sb.AppendLine($"# TYPE acorndb_sync_pull_total counter");
            sb.AppendLine($"acorndb_sync_pull_total{labels} {_totalPulls}");

            sb.AppendLine($"# HELP acorndb_sync_conflict_total Total sync conflicts");
            sb.AppendLine($"# TYPE acorndb_sync_conflict_total counter");
            sb.AppendLine($"acorndb_sync_conflict_total{labels} {_totalConflicts}");

            sb.AppendLine($"# HELP acorndb_sync_error_total Total sync errors");
            sb.AppendLine($"# TYPE acorndb_sync_error_total counter");
            sb.AppendLine($"acorndb_sync_error_total{labels} {_totalSyncErrors}");

            // Cache metrics
            sb.AppendLine($"# HELP acorndb_cache_hit_total Total cache hits");
            sb.AppendLine($"# TYPE acorndb_cache_hit_total counter");
            sb.AppendLine($"acorndb_cache_hit_total{labels} {_cacheHits}");

            sb.AppendLine($"# HELP acorndb_cache_miss_total Total cache misses");
            sb.AppendLine($"# TYPE acorndb_cache_miss_total counter");
            sb.AppendLine($"acorndb_cache_miss_total{labels} {_cacheMisses}");

            var hitRate = _cacheHits + _cacheMisses > 0
                ? (double)_cacheHits / (_cacheHits + _cacheMisses)
                : 0;
            sb.AppendLine($"# HELP acorndb_cache_hit_rate Cache hit rate (0-1)");
            sb.AppendLine($"# TYPE acorndb_cache_hit_rate gauge");
            sb.AppendLine($"acorndb_cache_hit_rate{labels} {hitRate:F4}");

            sb.AppendLine($"# HELP acorndb_cache_eviction_total Total cache evictions");
            sb.AppendLine($"# TYPE acorndb_cache_eviction_total counter");
            sb.AppendLine($"acorndb_cache_eviction_total{labels} {_cacheEvictions}");

            // Resilience metrics
            sb.AppendLine($"# HELP acorndb_retry_total Total retry attempts");
            sb.AppendLine($"# TYPE acorndb_retry_total counter");
            sb.AppendLine($"acorndb_retry_total{labels} {_totalRetries}");

            sb.AppendLine($"# HELP acorndb_fallback_total Total fallback activations");
            sb.AppendLine($"# TYPE acorndb_fallback_total counter");
            sb.AppendLine($"acorndb_fallback_total{labels} {_totalFallbacks}");

            sb.AppendLine($"# HELP acorndb_circuit_breaker_trip_total Circuit breaker trips");
            sb.AppendLine($"# TYPE acorndb_circuit_breaker_trip_total counter");
            sb.AppendLine($"acorndb_circuit_breaker_trip_total{labels} {_circuitBreakerTrips}");

            // Tree/Grove metrics
            sb.AppendLine($"# HELP acorndb_tree_count Number of active trees");
            sb.AppendLine($"# TYPE acorndb_tree_count gauge");
            sb.AppendLine($"acorndb_tree_count{labels} {_treeMetrics.Count}");

            sb.AppendLine($"# HELP acorndb_grove_count Number of active groves");
            sb.AppendLine($"# TYPE acorndb_grove_count gauge");
            sb.AppendLine($"acorndb_grove_count{labels} {_activeGroves}");

            sb.AppendLine($"# HELP acorndb_tangle_count Number of active tangles");
            sb.AppendLine($"# TYPE acorndb_tangle_count gauge");
            sb.AppendLine($"acorndb_tangle_count{labels} {_activeTangles}");

            // Latency histograms (simplified)
            if (_stashLatencies.Any())
            {
                var stashLatencies = _stashLatencies.OrderBy(x => x).ToList();
                sb.AppendLine($"# HELP acorndb_stash_duration_ms Stash operation latency in milliseconds");
                sb.AppendLine($"# TYPE acorndb_stash_duration_ms summary");
                sb.AppendLine($"acorndb_stash_duration_ms{{quantile=\"0.5\",{labels.TrimStart(',')}}} {GetPercentile(stashLatencies, 0.5):F2}");
                sb.AppendLine($"acorndb_stash_duration_ms{{quantile=\"0.95\",{labels.TrimStart(',')}}} {GetPercentile(stashLatencies, 0.95):F2}");
                sb.AppendLine($"acorndb_stash_duration_ms{{quantile=\"0.99\",{labels.TrimStart(',')}}} {GetPercentile(stashLatencies, 0.99):F2}");
                sb.AppendLine($"acorndb_stash_duration_ms_count{labels} {stashLatencies.Count}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Export metrics as JSON (OpenTelemetry compatible)
        /// </summary>
        public string ExportJson()
        {
            var metrics = new
            {
                timestamp = DateTime.UtcNow,
                labels = _labels,
                counters = new
                {
                    stash_total = _totalStashes,
                    crack_total = _totalCracks,
                    toss_total = _totalTosses,
                    squabble_total = _totalSquabbles,
                    shake_total = _totalShakes,
                    sync_push_total = _totalPushes,
                    sync_pull_total = _totalPulls,
                    sync_conflict_total = _totalConflicts,
                    sync_error_total = _totalSyncErrors,
                    cache_hit_total = _cacheHits,
                    cache_miss_total = _cacheMisses,
                    cache_eviction_total = _cacheEvictions,
                    retry_total = _totalRetries,
                    fallback_total = _totalFallbacks,
                    circuit_breaker_trip_total = _circuitBreakerTrips
                },
                gauges = new
                {
                    tree_count = _treeMetrics.Count,
                    grove_count = _activeGroves,
                    tangle_count = _activeTangles,
                    cache_hit_rate = _cacheHits + _cacheMisses > 0
                        ? (double)_cacheHits / (_cacheHits + _cacheMisses)
                        : 0
                },
                trees = _treeMetrics.Values.Select(t => new
                {
                    tree_id = t.TreeId,
                    tree_type = t.TreeType,
                    nut_count = t.NutCount,
                    stash_total = t.StashCount,
                    crack_total = t.CrackCount,
                    toss_total = t.TossCount,
                    conflict_total = t.ConflictCount,
                    cache_hit_rate = t.CacheHits + t.CacheMisses > 0
                        ? (double)t.CacheHits / (t.CacheHits + t.CacheMisses)
                        : 0
                })
            };

            return System.Text.Json.JsonSerializer.Serialize(metrics, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        /// <summary>
        /// Reset all metrics (useful for testing)
        /// </summary>
        public void Reset()
        {
            _totalStashes = 0;
            _totalCracks = 0;
            _totalTosses = 0;
            _totalSquabbles = 0;
            _totalShakes = 0;
            _totalPushes = 0;
            _totalPulls = 0;
            _totalConflicts = 0;
            _totalSyncErrors = 0;
            _cacheHits = 0;
            _cacheMisses = 0;
            _cacheEvictions = 0;
            _totalRetries = 0;
            _totalFallbacks = 0;
            _circuitBreakerTrips = 0;
            _stashLatencies.Clear();
            _crackLatencies.Clear();
            _tossLatencies.Clear();
            _treeMetrics.Clear();
            _activeGroves = 0;
            _activeTangles = 0;
        }

        private string FormatLabels()
        {
            if (!_labels.Any()) return "";

            var labelStr = string.Join(",", _labels.Select(kvp => $"{kvp.Key}=\"{kvp.Value}\""));
            return $",{labelStr}";
        }

        private double GetPercentile(List<double> sorted, double percentile)
        {
            if (sorted.Count == 0) return 0;

            int index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            index = Math.Max(0, Math.Min(sorted.Count - 1, index));
            return sorted[index];
        }
    }

    /// <summary>
    /// Per-tree metrics tracking
    /// </summary>
    internal class TreeMetrics
    {
        public string TreeId { get; set; } = "";
        public string TreeType { get; set; } = "";
        public int NutCount { get; set; }

        // Use fields for Interlocked operations
        private long _stashCount;
        private long _crackCount;
        private long _tossCount;
        private long _squabbleCount;
        private long _conflictCount;
        private long _cacheHits;
        private long _cacheMisses;

        public long StashCount => _stashCount;
        public long CrackCount => _crackCount;
        public long TossCount => _tossCount;
        public long SquabbleCount => _squabbleCount;
        public long ConflictCount => _conflictCount;
        public long CacheHits => _cacheHits;
        public long CacheMisses => _cacheMisses;

        public void RecordStash() => System.Threading.Interlocked.Increment(ref _stashCount);
        public void RecordCrack(bool cacheHit)
        {
            System.Threading.Interlocked.Increment(ref _crackCount);
            if (cacheHit)
                System.Threading.Interlocked.Increment(ref _cacheHits);
            else
                System.Threading.Interlocked.Increment(ref _cacheMisses);
        }
        public void RecordToss() => System.Threading.Interlocked.Increment(ref _tossCount);
        public void RecordSquabble() => System.Threading.Interlocked.Increment(ref _squabbleCount);
        public void RecordConflict() => System.Threading.Interlocked.Increment(ref _conflictCount);
    }
}
