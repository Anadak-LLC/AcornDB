using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Sync;
using System.Text;
using System.Text.Json;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for synchronization protocol overhead: serialization, network simulation, delta computation.
    /// Extends existing SyncBenchmarks with protocol-level measurements.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 5)]
    public class SyncProtocolBenchmarks
    {
        private Tree<TestDocument>? _sourceTree;
        private Tree<TestDocument>? _targetTree;

        public class TestDocument
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public int Value { get; set; }
            public DateTime Timestamp { get; set; }
        }

        [Params(1_000, 10_000)]
        public int DocumentCount;

        [Params(10, 50)] // Percentage of documents changed
        public int ChangePercentage;

        [GlobalSetup]
        public void Setup()
        {
            // Source tree
            var sourceTrunk = new MemoryTrunk<TestDocument>();
            _sourceTree = new Tree<TestDocument>(sourceTrunk);
            _sourceTree.TtlEnforcementEnabled = false;
            _sourceTree.CacheEvictionEnabled = false;

            // Target tree
            var targetTrunk = new MemoryTrunk<TestDocument>();
            _targetTree = new Tree<TestDocument>(targetTrunk);
            _targetTree.TtlEnforcementEnabled = false;
            _targetTree.CacheEvictionEnabled = false;

            // Pre-populate both trees
            for (int i = 0; i < DocumentCount; i++)
            {
                var doc = CreateDocument(i);
                _sourceTree.Stash(doc);
                _targetTree.Stash(doc);
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _sourceTree = null;
            _targetTree = null;
        }

        private TestDocument CreateDocument(int index, int contentSize = 1024)
        {
            return new TestDocument
            {
                Id = $"doc-{index}",
                Name = $"Document {index}",
                Content = new string('x', contentSize),
                Value = index,
                Timestamp = DateTime.UtcNow
            };
        }

        // ===== Serialization Overhead =====

        [Benchmark]
        public void Serialization_ExportChanges_JSON()
        {
            // Modify some documents
            var changesToMake = (DocumentCount * ChangePercentage) / 100;
            for (int i = 0; i < changesToMake; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2; // Modified
                _sourceTree!.Stash(doc);
            }

            // Export changes (serialization)
            var changes = _sourceTree.ExportChangesSince(DateTime.MinValue);

            // Measure: JSON serialization cost
            var json = JsonSerializer.Serialize(changes);

            // Expected: ~1ms per 100 documents
        }

        [Benchmark]
        public void Deserialization_ImportChanges_JSON()
        {
            // Modify and export
            var changesToMake = (DocumentCount * ChangePercentage) / 100;
            for (int i = 0; i < changesToMake; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2;
                _sourceTree!.Stash(doc);
            }

            var changes = _sourceTree.ExportChangesSince(DateTime.MinValue);
            var json = JsonSerializer.Serialize(changes);

            // Measure: Deserialization cost
            var deserialized = JsonSerializer.Deserialize<List<Nut<TestDocument>>>(json);

            // Expected: Similar to serialization (~1ms per 100 docs)
        }

        // ===== Change Detection Overhead =====

        [Benchmark]
        public void ChangeDetection_ExportChanges_SmallDelta()
        {
            // Small delta (10% changed)
            var changesToMake = (DocumentCount * 10) / 100;
            var cutoffTime = DateTime.UtcNow;

            System.Threading.Thread.Sleep(10); // Ensure timestamp difference

            for (int i = 0; i < changesToMake; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2;
                _sourceTree!.Stash(doc);
            }

            // Measure: Change detection (filter by timestamp)
            var changes = _sourceTree.ExportChangesSince(cutoffTime);

            // Expected: O(n) scan with timestamp comparison
        }

        [Benchmark]
        public void ChangeDetection_ExportChanges_LargeDelta()
        {
            // Large delta (50% changed)
            var changesToMake = (DocumentCount * 50) / 100;
            var cutoffTime = DateTime.UtcNow;

            System.Threading.Thread.Sleep(10);

            for (int i = 0; i < changesToMake; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2;
                _sourceTree!.Stash(doc);
            }

            var changes = _sourceTree.ExportChangesSince(cutoffTime);

            // Expected: Linear growth with change percentage
        }

        // ===== Payload Size vs Sync Time =====

        [Benchmark]
        public void SyncPayload_SmallDocuments_1KB()
        {
            var changesToMake = (DocumentCount * ChangePercentage) / 100;
            for (int i = 0; i < changesToMake; i++)
            {
                var doc = CreateDocument(i, contentSize: 1024); // 1KB
                _sourceTree!.Stash(doc);
            }

            var changes = _sourceTree.ExportChangesSince(DateTime.MinValue);
            foreach (var nut in changes)
            {
                _targetTree!.Stash(nut.Id, nut.Payload);
            }

            // Expected: Fast, minimal payload
        }

        [Benchmark]
        public void SyncPayload_MediumDocuments_10KB()
        {
            var changesToMake = (DocumentCount * ChangePercentage) / 100;
            for (int i = 0; i < changesToMake; i++)
            {
                var doc = CreateDocument(i, contentSize: 10240); // 10KB
                _sourceTree!.Stash(doc);
            }

            var changes = _sourceTree.ExportChangesSince(DateTime.MinValue);
            // Import directly to trunk (Tree doesn't have ImportChanges)
            foreach (var nut in changes)
            {
                _targetTree!.Stash(nut.Id, nut.Payload);
            }

            // Expected: 10x slower than 1KB
        }

        [Benchmark]
        public void SyncPayload_LargeDocuments_100KB()
        {
            var changesToMake = (DocumentCount * ChangePercentage) / 100;
            for (int i = 0; i < changesToMake; i++)
            {
                var doc = CreateDocument(i, contentSize: 102400); // 100KB
                _sourceTree!.Stash(doc);
            }

            var changes = _sourceTree.ExportChangesSince(DateTime.MinValue);
            // Import directly to trunk (Tree doesn't have ImportChanges)
            foreach (var nut in changes)
            {
                _targetTree!.Stash(nut.Id, nut.Payload);
            }

            // Expected: 100x slower than 1KB
        }

        // ===== Network Simulation (Serialization + Deserialization) =====

        [Benchmark]
        public void NetworkSimulation_FullRoundTrip()
        {
            // Simulate: Export -> Serialize -> "Network" -> Deserialize -> Import
            var changesToMake = (DocumentCount * ChangePercentage) / 100;
            for (int i = 0; i < changesToMake; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2;
                _sourceTree!.Stash(doc);
            }

            // Export
            var changes = _sourceTree.ExportChangesSince(DateTime.MinValue);

            // Serialize (send over network)
            var json = JsonSerializer.Serialize(changes);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Simulate network latency (not measured here, add if needed)
            // await Task.Delay(10);

            // Deserialize (receive from network)
            var receivedJson = Encoding.UTF8.GetString(bytes);
            var receivedChanges = JsonSerializer.Deserialize<List<Nut<TestDocument>>>(receivedJson);

            // Import
            foreach (var nut in receivedChanges!)
            {
                _targetTree!.Stash(nut.Id, nut.Payload);
            }

            // Expected: Sum of serialization + deserialization + import overhead
        }

        // ===== Bandwidth Estimation =====

        [Benchmark]
        public void Bandwidth_Measurement_DeltaSync()
        {
            var changesToMake = (DocumentCount * ChangePercentage) / 100;
            for (int i = 0; i < changesToMake; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2;
                _sourceTree!.Stash(doc);
            }

            var changes = _sourceTree.ExportChangesSince(DateTime.MinValue);
            var json = JsonSerializer.Serialize(changes);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Bandwidth used: bytes.Length
            // For reporting: bytes per document, total KB transferred
            var bytesPerDoc = bytes.Length / changesToMake;
            var totalKB = bytes.Length / 1024.0;

            // Expected: ~1.5KB per document (1KB content + 0.5KB metadata)
        }

        [Benchmark]
        public void Bandwidth_Measurement_FullSync()
        {
            // Full sync (no timestamp filter)
            var changes = _sourceTree!.ExportChangesSince(DateTime.MinValue);
            var json = JsonSerializer.Serialize(changes);
            var bytes = Encoding.UTF8.GetBytes(json);

            var totalKB = bytes.Length / 1024.0;
            var bytesPerDoc = bytes.Length / DocumentCount;

            // Expected: DocumentCount × bytesPerDoc
        }

        // ===== Bidirectional Sync =====

        [Benchmark]
        public void BidirectionalSync_NoConflicts()
        {
            // Source modifies first 50%
            for (int i = 0; i < DocumentCount / 2; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2;
                _sourceTree!.Stash(doc);
            }

            // Target modifies second 50%
            for (int i = DocumentCount / 2; i < DocumentCount; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 3;
                _targetTree!.Stash(doc);
            }

            // Sync both directions
            var sourceChanges = _sourceTree.ExportChangesSince(DateTime.MinValue);
            var targetChanges = _targetTree.ExportChangesSince(DateTime.MinValue);

            foreach (var nut in sourceChanges)
            {
                _targetTree.Stash(nut.Id, nut.Payload);
            }
            foreach (var nut in targetChanges)
            {
                _sourceTree.Stash(nut.Id, nut.Payload);
            }

            // Expected: 2x the cost of unidirectional sync
        }

        [Benchmark]
        public void BidirectionalSync_WithConflicts()
        {
            // Both modify same documents (conflicts)
            for (int i = 0; i < DocumentCount / 2; i++)
            {
                var sourceDoc = CreateDocument(i);
                sourceDoc.Value = i * 2;
                _sourceTree!.Stash(sourceDoc);

                var targetDoc = CreateDocument(i);
                targetDoc.Value = i * 3; // Conflicting value
                _targetTree!.Stash(targetDoc);
            }

            // Sync with conflict resolution
            var sourceChanges = _sourceTree.ExportChangesSince(DateTime.MinValue);
            var targetChanges = _targetTree.ExportChangesSince(DateTime.MinValue);

            // Import with conflict resolution via Tree.Squabble
            foreach (var nut in sourceChanges)
            {
                _targetTree.Squabble(nut.Id, nut, Sync.ConflictDirection.PreferRemote);
            }
            foreach (var nut in targetChanges)
            {
                _sourceTree.Squabble(nut.Id, nut, Sync.ConflictDirection.PreferLocal);
            }

            // Expected: Conflict resolution adds overhead
        }

        // ===== Incremental Sync Performance =====

        [Benchmark]
        public void IncrementalSync_FirstSync_Full()
        {
            // First sync: full dataset transfer
            var changes = _sourceTree!.ExportChangesSince(DateTime.MinValue);
            foreach (var nut in changes)
            {
                _targetTree!.Stash(nut.Id, nut.Payload);
            }

            // Expected: Largest payload, one-time cost
        }

        [Benchmark]
        public void IncrementalSync_SubsequentSync_SmallDelta()
        {
            // Initial sync
            var initialChanges = _sourceTree!.ExportChangesSince(DateTime.MinValue);
            foreach (var nut in initialChanges)
            {
                _targetTree!.Stash(nut.Id, nut.Payload);
            }

            var cutoffTime = DateTime.UtcNow;
            System.Threading.Thread.Sleep(10);

            // Small changes
            var changesToMake = (DocumentCount * 5) / 100; // 5% changed
            for (int i = 0; i < changesToMake; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2;
                _sourceTree.Stash(doc);
            }

            // Subsequent sync (delta only)
            var deltaChanges = _sourceTree.ExportChangesSince(cutoffTime);
            foreach (var nut in deltaChanges)
            {
                _targetTree.Stash(nut.Id, nut.Payload);
            }

            // Expected: Much faster than full sync
        }

        // ===== Compression Impact on Sync =====

        [Benchmark]
        public void Sync_WithCompression_Simulation()
        {
            var changesToMake = (DocumentCount * ChangePercentage) / 100;
            for (int i = 0; i < changesToMake; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2;
                _sourceTree!.Stash(doc);
            }

            var changes = _sourceTree.ExportChangesSince(DateTime.MinValue);
            var json = JsonSerializer.Serialize(changes);
            var uncompressedBytes = Encoding.UTF8.GetBytes(json);

            // Simulate compression (Gzip)
            using var compressedStream = new System.IO.MemoryStream();
            using (var gzip = new System.IO.Compression.GZipStream(compressedStream, System.IO.Compression.CompressionLevel.Fastest))
            {
                gzip.Write(uncompressedBytes, 0, uncompressedBytes.Length);
            }
            var compressedBytes = compressedStream.ToArray();

            // Compression ratio
            var ratio = (double)compressedBytes.Length / uncompressedBytes.Length;

            // Expected: 60-70% size reduction for JSON (high compressibility)
        }

        // ===== Conflict Resolution Overhead =====

        [Benchmark]
        public void ConflictResolution_LocalWins()
        {
            // Create conflicts
            for (int i = 0; i < DocumentCount / 2; i++)
            {
                var sourceDoc = CreateDocument(i);
                sourceDoc.Value = i * 2;
                _sourceTree!.Stash(sourceDoc);

                var targetDoc = CreateDocument(i);
                targetDoc.Value = i * 3;
                _targetTree!.Stash(targetDoc);
            }

            // Use Tree.Squabble for conflict resolution
            var sourceChanges = _sourceTree!.ExportChangesSince(DateTime.MinValue);
            foreach (var nut in sourceChanges)
            {
                _targetTree!.Squabble(nut.Id, nut, Sync.ConflictDirection.PreferLocal);
            }

            // Expected: Fast (simple timestamp comparison)
        }

        [Benchmark]
        public void ConflictResolution_IncomingWins()
        {
            for (int i = 0; i < DocumentCount / 2; i++)
            {
                var sourceDoc = CreateDocument(i);
                sourceDoc.Value = i * 2;
                _sourceTree!.Stash(sourceDoc);

                var targetDoc = CreateDocument(i);
                targetDoc.Value = i * 3;
                _targetTree!.Stash(targetDoc);
            }

            // Use Tree.Squabble for conflict resolution
            var sourceChanges = _sourceTree!.ExportChangesSince(DateTime.MinValue);
            foreach (var nut in sourceChanges)
            {
                _targetTree!.Squabble(nut.Id, nut, Sync.ConflictDirection.PreferRemote);
            }

            // Expected: Similar to LocalWins
        }

        [Benchmark]
        public void ConflictResolution_CustomMerge()
        {
            for (int i = 0; i < DocumentCount / 2; i++)
            {
                var sourceDoc = CreateDocument(i);
                sourceDoc.Value = i * 2;
                _sourceTree!.Stash(sourceDoc);

                var targetDoc = CreateDocument(i);
                targetDoc.Value = i * 3;
                _targetTree!.Stash(targetDoc);
            }

            // Use Tree.Squabble with custom merge logic
            var sourceChanges = _sourceTree!.ExportChangesSince(DateTime.MinValue);
            foreach (var nut in sourceChanges)
            {
                // Custom merge: sum values
                var existing = _targetTree!.Crack(nut.Id);
                if (existing != null)
                {
                    nut.Payload.Value = existing.Value + nut.Payload.Value;
                }
                _targetTree.Squabble(nut.Id, nut, Sync.ConflictDirection.PreferRemote);
            }

            // Expected: Slower due to custom merge logic
        }
    }

    /// <summary>
    /// Expected Sync Protocol Results:
    ///
    /// Serialization Overhead:
    /// - JSON Serialization: ~1ms per 100 documents
    /// - JSON Deserialization: ~1ms per 100 documents
    /// - Binary formats (e.g., MessagePack) could be 2-3x faster
    ///
    /// Change Detection:
    /// - Small Delta (10%): ~2ms for 10K docs (timestamp filtering)
    /// - Large Delta (50%): ~5ms for 10K docs (linear growth)
    /// - Full Scan: O(n) - scales linearly with dataset size
    ///
    /// Payload Size Impact:
    /// - 1KB docs: ~10ms for 1K docs
    /// - 10KB docs: ~100ms for 1K docs (10x slower)
    /// - 100KB docs: ~1s for 1K docs (100x slower)
    /// - Network bandwidth becomes bottleneck with large documents
    ///
    /// Network Round Trip (Export + Serialize + Deserialize + Import):
    /// - 1K docs, 10% changed: ~15ms total
    /// - 10K docs, 10% changed: ~100ms total
    /// - Add network latency: +10-50ms per round trip (LAN), +100-500ms (WAN)
    ///
    /// Bandwidth Usage:
    /// - JSON overhead: ~1.5KB per document (1KB content + 0.5KB metadata)
    /// - 1K docs × 10% changed = 100 docs × 1.5KB = ~150KB transferred
    /// - 10K docs × 50% changed = 5K docs × 1.5KB = ~7.5MB transferred
    /// - Compression: Reduces JSON bandwidth by 60-70%
    ///
    /// Bidirectional Sync:
    /// - No Conflicts: 2x unidirectional cost (both directions)
    /// - With Conflicts: +20% overhead (conflict detection + resolution)
    ///
    /// Incremental Sync:
    /// - First Sync (Full): ~100ms for 10K docs (one-time cost)
    /// - Subsequent Sync (5% delta): ~10ms (10x faster than full)
    /// - Recommendation: Always use incremental sync after initial load
    ///
    /// Compression Impact:
    /// - Gzip Compression: 60-70% size reduction for JSON
    /// - Compression time: +10-20ms for 1K docs
    /// - Decompression time: +5-10ms for 1K docs
    /// - Trade-off: Smaller payloads vs CPU overhead
    /// - Recommended for WAN (network bandwidth limited)
    /// - Skip for LAN (CPU overhead not worth it)
    ///
    /// Conflict Resolution:
    /// - LocalWins: ~2ms overhead for 1K conflicts (timestamp comparison)
    /// - IncomingWins: ~2ms overhead (similar to LocalWins)
    /// - Custom Merge: ~5-10ms overhead (depends on arbitrator logic)
    ///
    /// Optimization Recommendations:
    /// 1. Use incremental sync (timestamp-based change tracking)
    /// 2. Enable compression for WAN (60-70% bandwidth savings)
    /// 3. Batch small changes (avoid frequent tiny syncs)
    /// 4. Consider binary serialization (MessagePack, Protobuf) for 2-3x faster serialization
    /// 5. Implement connection pooling for HTTP-based sync
    /// 6. Use delta compression for large documents (binary diff)
    /// 7. Paginate large sync payloads (>10K documents)
    ///
    /// Performance Targets:
    /// - LAN Sync (1K docs, 10% delta): <50ms end-to-end
    /// - WAN Sync (1K docs, 10% delta): <200ms end-to-end (including network latency)
    /// - Mobile Sync (1K docs, 10% delta): <500ms (3G/4G latency + limited bandwidth)
    /// </summary>
}
