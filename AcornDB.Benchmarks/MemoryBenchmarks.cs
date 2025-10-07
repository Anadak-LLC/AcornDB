using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Cache;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for memory usage under load with different cache strategies
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 1, iterationCount: 3)]
    public class MemoryBenchmarks
    {
        private Tree<TestItem>? _unlimitedCacheTree;
        private Tree<TestItem>? _lruCacheTree;
        private Tree<TestItem>? _noEvictionTree;

        public class TestItem
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public byte[] Data { get; set; } = Array.Empty<byte>();
        }

        [Params(10_000, 50_000, 100_000)]
        public int ItemCount;

        [GlobalSetup]
        public void Setup()
        {
            // Tree with LRU cache (limited to 10k items)
            _lruCacheTree = new Tree<TestItem>(
                new MemoryTrunk<TestItem>(),
                new LRUCacheStrategy<TestItem>(maxSize: 10_000)
            );

            // Tree with no eviction (unlimited cache)
            _noEvictionTree = new Tree<TestItem>(
                new MemoryTrunk<TestItem>(),
                new NoEvictionStrategy<TestItem>()
            );

            // Tree with disabled eviction (for comparison)
            _unlimitedCacheTree = new Tree<TestItem>(new MemoryTrunk<TestItem>())
            {
                CacheEvictionEnabled = false
            };
        }

        [Benchmark]
        public void MemoryUsage_LRU_Cache()
        {
            for (int i = 0; i < ItemCount; i++)
            {
                _lruCacheTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Test Item {i}",
                    Data = new byte[1024] // 1KB per item
                });
            }
        }

        [Benchmark]
        public void MemoryUsage_Unlimited_Cache()
        {
            for (int i = 0; i < ItemCount; i++)
            {
                _unlimitedCacheTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Test Item {i}",
                    Data = new byte[1024] // 1KB per item
                });
            }
        }

        [Benchmark]
        public void MemoryUsage_NoEviction_Strategy()
        {
            for (int i = 0; i < ItemCount; i++)
            {
                _noEvictionTree!.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Test Item {i}",
                    Data = new byte[1024] // 1KB per item
                });
            }
        }

        [Benchmark]
        public void LRU_EvictionPerformance_100k_Items()
        {
            var tree = new Tree<TestItem>(
                new MemoryTrunk<TestItem>(),
                new LRUCacheStrategy<TestItem>(maxSize: 5_000)
            );

            // This will trigger multiple evictions
            for (int i = 0; i < 100_000; i++)
            {
                tree.Stash(new TestItem
                {
                    Id = $"item-{i}",
                    Name = $"Test Item {i}",
                    Data = new byte[512]
                });
            }

            // Verify cache is bounded
            if (tree.NutCount > 5_000)
            {
                throw new Exception($"Cache exceeded limit: {tree.NutCount}");
            }
        }
    }
}
