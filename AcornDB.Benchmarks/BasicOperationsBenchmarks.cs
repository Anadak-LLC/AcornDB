using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for basic Tree operations: Stash, Crack, Toss
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 5)]
    public class BasicOperationsBenchmarks
    {
        private Tree<TestItem>? _memoryTree;
        private Tree<TestItem>? _fileTree;
        private const int ItemCount = 1000;

        public class TestItem
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
            public DateTime Timestamp { get; set; }
        }

        [GlobalSetup]
        public void Setup()
        {
            // Create trees with different trunk types
            _memoryTree = new Tree<TestItem>(new MemoryTrunk<TestItem>());
            _fileTree = new Tree<TestItem>(new FileTrunk<TestItem>());
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            // Clean up file-based storage
            if (Directory.Exists("data"))
            {
                Directory.Delete("data", recursive: true);
            }
        }

        [Benchmark]
        public void Stash_MemoryTrunk_1000Items()
        {
            for (int i = 0; i < ItemCount; i++)
            {
                _memoryTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Test Item {i}",
                    Value = i,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [Benchmark]
        public void Stash_FileTrunk_1000Items()
        {
            for (int i = 0; i < ItemCount; i++)
            {
                _fileTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Test Item {i}",
                    Value = i,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [Benchmark]
        public void Crack_MemoryTrunk_1000Items()
        {
            // Setup: Stash items first
            for (int i = 0; i < ItemCount; i++)
            {
                _memoryTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Test Item {i}",
                    Value = i,
                    Timestamp = DateTime.UtcNow
                });
            }

            // Benchmark: Crack items
            for (int i = 0; i < ItemCount; i++)
            {
                var item = _memoryTree!.Crack($"item-{i}");
            }
        }

        [Benchmark]
        public void Toss_MemoryTrunk_1000Items()
        {
            // Setup: Stash items first
            for (int i = 0; i < ItemCount; i++)
            {
                _memoryTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Test Item {i}",
                    Value = i,
                    Timestamp = DateTime.UtcNow
                });
            }

            // Benchmark: Toss items
            for (int i = 0; i < ItemCount; i++)
            {
                _memoryTree!.Toss($"item-{i}");
            }
        }

        [Benchmark]
        public void StashAndCrack_Mixed_1000Operations()
        {
            // Simulate realistic mixed workload
            for (int i = 0; i < ItemCount / 2; i++)
            {
                // Stash
                _memoryTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Test Item {i}",
                    Value = i,
                    Timestamp = DateTime.UtcNow
                });

                // Crack (read back)
                var item = _memoryTree!.Crack($"item-{i}");

                // Stash again (update)
                _memoryTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Updated Item {i}",
                    Value = i * 2,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
    }
}
