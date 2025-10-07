using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Sync;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for sync performance: in-process vs HTTP
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 3)]
    public class SyncBenchmarks
    {
        private Tree<TestItem>? _sourceTree;
        private Tree<TestItem>? _targetTree;

        public class TestItem
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
        }

        [Params(100, 500, 1000)]
        public int ItemCount;

        [IterationSetup]
        public void Setup()
        {
            _sourceTree = new Tree<TestItem>(new MemoryTrunk<TestItem>());
            _targetTree = new Tree<TestItem>(new MemoryTrunk<TestItem>());
        }

        [Benchmark]
        public void InProcessSync_Push()
        {
            // Add items to source tree
            for (int i = 0; i < ItemCount; i++)
            {
                _sourceTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Test Item {i}",
                    Value = i
                });
            }

            // Entangle trees (in-process sync)
            _sourceTree!.Entangle(_targetTree!);

            // Trigger sync
            _sourceTree.Shake();
        }

        [Benchmark]
        public void InProcessSync_Bidirectional()
        {
            // Add items to both trees
            for (int i = 0; i < ItemCount / 2; i++)
            {
                _sourceTree!.Stash(new TestItem
                {
                    Id = $"source-{i}",
                    Name = $"Source Item {i}",
                    Value = i
                });

                _targetTree!.Stash(new TestItem
                {
                    Id = $"target-{i}",
                    Name = $"Target Item {i}",
                    Value = i
                });
            }

            // Entangle both ways
            _sourceTree!.Entangle(_targetTree!);
            _targetTree!.Entangle(_sourceTree);

            // Sync both directions
            _sourceTree.Shake();
            _targetTree.Shake();
        }

        [Benchmark]
        public void ExportChanges_Performance()
        {
            // Add items
            for (int i = 0; i < ItemCount; i++)
            {
                _sourceTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Test Item {i}",
                    Value = i
                });
            }

            // Export all changes
            var changes = _sourceTree!.ExportChanges().ToList();

            if (changes.Count != ItemCount)
            {
                throw new Exception($"Expected {ItemCount} changes, got {changes.Count}");
            }
        }

        [Benchmark]
        public void SquabbleResolution_Performance()
        {
            // Create conflicting items
            for (int i = 0; i < ItemCount; i++)
            {
                var item = new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Source Item {i}",
                    Value = i
                };

                _sourceTree!.Stash(item);

                // Wait a bit to ensure different timestamps
                Thread.Sleep(1);

                // Create conflicting version
                var incomingNut = new Nut<TestItem>
                {
                    Id = $"item-{i}",
                    Payload = new TestItem
                    {
                        Id = $"item-{i}",
                        Name = $"Incoming Item {i}",
                        Value = i * 2
                    },
                    Timestamp = DateTime.UtcNow.AddSeconds(1) // Newer timestamp
                };

                // Trigger squabble resolution
                _sourceTree.Squabble($"item-{i}", incomingNut);
            }
        }
    }
}
