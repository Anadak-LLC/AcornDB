using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for conflict resolution (Squabble) overhead
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 5)]
    public class ConflictResolutionBenchmarks
    {
        private Tree<TestItem>? _tree;

        public class TestItem
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
            public DateTime LastModified { get; set; }
        }

        [Params(100, 500, 1000)]
        public int ConflictCount;

        [IterationSetup]
        public void Setup()
        {
            _tree = new Tree<TestItem>(new MemoryTrunk<TestItem>());

            // Pre-populate with base items
            for (int i = 0; i < ConflictCount; i++)
            {
                _tree.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Base Item {i}",
                    Value = i,
                    LastModified = DateTime.UtcNow
                });
            }
        }

        [Benchmark]
        public void Squabble_LocalWins()
        {
            // Create incoming nuts with OLDER timestamps (local should win)
            for (int i = 0; i < ConflictCount; i++)
            {
                var incomingNut = new Nut<TestItem>
                {
                    Id = $"item-{i}",
                    Payload = new TestItem
                    {
                        Id = $"item-{i}",
                        Name = $"Incoming Item {i}",
                        Value = i * 2,
                        LastModified = DateTime.UtcNow
                    },
                    Timestamp = DateTime.UtcNow.AddSeconds(-10) // Older timestamp
                };

                _tree!.Squabble($"item-{i}", incomingNut);
            }
        }

        [Benchmark]
        public void Squabble_IncomingWins()
        {
            // Create incoming nuts with NEWER timestamps (incoming should win)
            for (int i = 0; i < ConflictCount; i++)
            {
                var incomingNut = new Nut<TestItem>
                {
                    Id = $"item-{i}",
                    Payload = new TestItem
                    {
                        Id = $"item-{i}",
                        Name = $"Incoming Item {i}",
                        Value = i * 2,
                        LastModified = DateTime.UtcNow
                    },
                    Timestamp = DateTime.UtcNow.AddSeconds(10) // Newer timestamp
                };

                _tree!.Squabble($"item-{i}", incomingNut);
            }
        }

        [Benchmark]
        public void Squabble_MixedResults()
        {
            // 50% local wins, 50% incoming wins
            for (int i = 0; i < ConflictCount; i++)
            {
                var isIncomingNewer = i % 2 == 0;
                var timestampOffset = isIncomingNewer ? 10 : -10;

                var incomingNut = new Nut<TestItem>
                {
                    Id = $"item-{i}",
                    Payload = new TestItem
                    {
                        Id = $"item-{i}",
                        Name = $"Incoming Item {i}",
                        Value = i * 2,
                        LastModified = DateTime.UtcNow
                    },
                    Timestamp = DateTime.UtcNow.AddSeconds(timestampOffset)
                };

                _tree!.Squabble($"item-{i}", incomingNut);
            }
        }

        [Benchmark]
        public void Squabble_WithHistoryRetrieval()
        {
            // Use DocumentStoreTrunk for history support (if available)
            var historyTree = new Tree<TestItem>(new MemoryTrunk<TestItem>());

            // Add initial items
            for (int i = 0; i < ConflictCount; i++)
            {
                historyTree.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Version 1",
                    Value = i,
                    LastModified = DateTime.UtcNow
                });
            }

            // Create conflicts
            for (int i = 0; i < ConflictCount; i++)
            {
                var incomingNut = new Nut<TestItem>
                {
                    Id = $"item-{i}",
                    Payload = new TestItem
                    {
                        Id = $"item-{i}",
                        Name = $"Version 2",
                        Value = i * 2,
                        LastModified = DateTime.UtcNow
                    },
                    Timestamp = DateTime.UtcNow.AddSeconds(10)
                };

                historyTree.Squabble($"item-{i}", incomingNut);

                // Try to retrieve history (may throw NotSupportedException for MemoryTrunk)
                try
                {
                    var history = historyTree.GetHistory($"item-{i}");
                }
                catch (NotSupportedException)
                {
                    // Expected for MemoryTrunk
                }
            }
        }

        [Benchmark]
        public void NoConflict_DirectStash()
        {
            // Baseline: no conflict resolution, just direct stash
            var baselineTree = new Tree<TestItem>(new MemoryTrunk<TestItem>());

            for (int i = 0; i < ConflictCount; i++)
            {
                baselineTree.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Item {i}",
                    Value = i,
                    LastModified = DateTime.UtcNow
                });
            }
        }
    }
}
