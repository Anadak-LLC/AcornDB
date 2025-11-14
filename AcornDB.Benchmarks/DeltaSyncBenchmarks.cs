using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Sync;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for delta sync efficiency (SY-004)
    /// Compares delta sync vs full sync for various change percentages
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 5)]
    public class DeltaSyncBenchmarks
    {
        private Tree<TestItem>? _sourceTree;
        private Tree<TestItem>? _targetTree;

        public class TestItem
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
            public DateTime Modified { get; set; }
        }

        [Params(10_000, 50_000, 100_000)]
        public int TotalDocuments;

        [Params(0.01, 0.05, 0.10, 0.50)]  // 1%, 5%, 10%, 50% changed
        public double ChangePercentage;

        [IterationSetup]
        public void Setup()
        {
            _sourceTree = new Tree<TestItem>(new MemoryTrunk<TestItem>());
            _targetTree = new Tree<TestItem>(new MemoryTrunk<TestItem>());

            // Populate source tree with initial data
            for (int i = 0; i < TotalDocuments; i++)
            {
                _sourceTree.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Item {i}",
                    Value = i,
                    Modified = DateTime.UtcNow
                });
            }

            // Mark sync completed (for delta sync baseline)
            _sourceTree.MarkSyncCompleted();

            // Simulate changes (based on ChangePercentage)
            int changeCount = (int)(TotalDocuments * ChangePercentage);
            System.Threading.Thread.Sleep(100); // Ensure timestamp difference

            for (int i = 0; i < changeCount; i++)
            {
                _sourceTree.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Updated Item {i}",
                    Value = i * 2,
                    Modified = DateTime.UtcNow
                });
            }
        }

        // ===== Full Sync Benchmarks (Baseline) =====

        [Benchmark(Baseline = true)]
        public void FullSync_ExportAll()
        {
            // Export all documents (full sync)
            var changes = _sourceTree!.ExportChanges().ToList();

            if (changes.Count != TotalDocuments)
            {
                throw new Exception($"Expected {TotalDocuments} changes, got {changes.Count}");
            }
        }

        [Benchmark]
        public void FullSync_ExportAndImport()
        {
            // Full sync: export from source, import to target
            var allChanges = _sourceTree!.ExportChanges().ToList();

            foreach (var nut in allChanges)
            {
                _targetTree!.Squabble(nut.Id, nut);
            }
        }

        // ===== Delta Sync Benchmarks =====

        [Benchmark]
        public void DeltaSync_ExportChangesSince()
        {
            // Export only changed documents (delta sync)
            var changes = _sourceTree!.ExportChangesSince(_sourceTree.LastSyncTimestamp).ToList();

            int expectedChanges = (int)(TotalDocuments * ChangePercentage);

            // Verify we got the expected number of changes
            if (Math.Abs(changes.Count - expectedChanges) > 10) // Allow small variance
            {
                throw new Exception($"Expected ~{expectedChanges} changes, got {changes.Count}");
            }
        }

        [Benchmark]
        public void DeltaSync_ExportDeltaChanges()
        {
            // Use the built-in delta export method
            var changes = _sourceTree!.ExportDeltaChanges().ToList();

            int expectedChanges = (int)(TotalDocuments * ChangePercentage);

            if (Math.Abs(changes.Count - expectedChanges) > 10)
            {
                throw new Exception($"Expected ~{expectedChanges} changes, got {changes.Count}");
            }
        }

        [Benchmark]
        public void DeltaSync_ExportAndImport()
        {
            // Delta sync: export only changes, import to target
            var deltaChanges = _sourceTree!.ExportChangesSince(_sourceTree.LastSyncTimestamp).ToList();

            foreach (var nut in deltaChanges)
            {
                _targetTree!.Squabble(nut.Id, nut);
            }
        }

        // ===== Efficiency Analysis =====

        [Benchmark]
        public void DeltaSync_BandwidthSavings_Analysis()
        {
            // Calculate bandwidth savings
            var fullSync = _sourceTree!.ExportChanges().ToList();
            var deltaSync = _sourceTree.ExportChangesSince(_sourceTree.LastSyncTimestamp).ToList();

            int fullSyncCount = fullSync.Count;
            int deltaSyncCount = deltaSync.Count;

            double savingsPercentage = (1.0 - ((double)deltaSyncCount / fullSyncCount)) * 100;

            // For reporting (BenchmarkDotNet will capture timing)
            // Savings should correlate with (1 - ChangePercentage)
        }

        // ===== Incremental Updates =====

        [Benchmark]
        public void DeltaSync_MultipleRounds()
        {
            // Simulate multiple delta sync rounds
            var changeSize = (int)(TotalDocuments * ChangePercentage);

            // Round 1
            for (int i = 0; i < changeSize; i++)
            {
                _sourceTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Round 1 Update {i}",
                    Value = i * 3,
                    Modified = DateTime.UtcNow
                });
            }
            var round1 = _sourceTree!.ExportDeltaChanges().ToList();

            System.Threading.Thread.Sleep(50);

            // Round 2
            for (int i = 0; i < changeSize; i++)
            {
                _sourceTree!.Stash(new TestItem
                {
                    Id = $"item-{i + changeSize}",
                    Name = $"Round 2 Update {i}",
                    Value = i * 4,
                    Modified = DateTime.UtcNow
                });
            }
            var round2 = _sourceTree!.ExportDeltaChanges().ToList();

            System.Threading.Thread.Sleep(50);

            // Round 3
            for (int i = 0; i < changeSize; i++)
            {
                _sourceTree!.Stash(new TestItem
                {
                    Id = $"item-{i + (changeSize * 2)}",
                    Name = $"Round 3 Update {i}",
                    Value = i * 5,
                    Modified = DateTime.UtcNow
                });
            }
            var round3 = _sourceTree!.ExportDeltaChanges().ToList();

            // Each round should export only the changes for that round
        }

        // ===== Large Dataset Delta Sync =====

        [Benchmark]
        public void DeltaSync_LargeDataset_SmallChange()
        {
            // Simulate a very large dataset with minimal changes
            // This shows maximum benefit of delta sync

            var largeTree = new Tree<TestItem>(new MemoryTrunk<TestItem>());

            // Add 100K items
            for (int i = 0; i < 100_000; i++)
            {
                largeTree.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Item {i}",
                    Value = i
                });
            }

            largeTree.MarkSyncCompleted();

            System.Threading.Thread.Sleep(100);

            // Change only 100 items (0.1%)
            for (int i = 0; i < 100; i++)
            {
                largeTree.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Updated {i}",
                    Value = i * 2
                });
            }

            // Export delta (should be ~100 items instead of 100K)
            var delta = largeTree.ExportDeltaChanges().ToList();

            if (delta.Count > 200)
            {
                throw new Exception($"Expected ~100 changes, got {delta.Count}");
            }
        }

        // ===== Performance Characteristics =====

        [Benchmark]
        public void DeltaSync_FilteringOverhead()
        {
            // Measure the overhead of timestamp filtering
            var timestamp = DateTime.UtcNow.AddHours(-1);

            // This measures the cost of filtering by timestamp
            var changes = _sourceTree!.ExportChangesSince(timestamp).ToList();
        }

        [Benchmark]
        public void DeltaSync_MemoryFootprint()
        {
            // Measure memory usage of delta sync vs full sync

            // Full sync (baseline)
            var fullSync = _sourceTree!.ExportChanges().ToList();

            // Delta sync
            var deltaSync = _sourceTree.ExportChangesSince(_sourceTree.LastSyncTimestamp).ToList();

            // Memory diagnostics will show the difference
            // Delta sync should use significantly less memory for small change percentages
        }
    }

    /// <summary>
    /// Expected Results:
    ///
    /// For 100,000 documents with 1% change:
    /// - Full Sync: Export 100,000 nuts (~100MB payload)
    /// - Delta Sync: Export 1,000 nuts (~1MB payload)
    /// - Savings: 99% bandwidth reduction
    ///
    /// For 100,000 documents with 10% change:
    /// - Full Sync: Export 100,000 nuts (~100MB payload)
    /// - Delta Sync: Export 10,000 nuts (~10MB payload)
    /// - Savings: 90% bandwidth reduction
    ///
    /// Performance Impact:
    /// - Delta sync should be 2-5x faster than full sync for < 10% changes
    /// - Memory usage should scale with change size, not total dataset size
    /// - Timestamp filtering overhead should be minimal (< 1ms for 100K documents)
    /// </summary>
}
