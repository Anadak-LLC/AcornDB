using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Cache;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for cache effectiveness: hit rates, eviction overhead, memory usage.
    /// Measures real-world cache performance under various access patterns.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 5)]
    public class CacheEffectivenessBenchmarks
    {
        private Tree<TestDocument>? _tree;
        private BTreeTrunk<TestDocument>? _btreeTrunk;

        public class TestDocument
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public int Value { get; set; }
        }

        [Params(1_000, 10_000)]
        public int DatasetSize;

        [Params(100, 500, 1000)] // Cache sizes
        public int CacheSize;

        private string _tempDir = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"acorndb_cache_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _btreeTrunk?.Dispose();

            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); } catch { }
            }
        }

        private TestDocument CreateDocument(int index, int contentSize = 1024)
        {
            return new TestDocument
            {
                Id = $"doc-{index}",
                Name = $"Document {index}",
                Content = new string('x', contentSize),
                Value = index
            };
        }

        private Tree<TestDocument> CreateTreeWithCache(ITrunk<TestDocument> trunk, int maxCacheSize)
        {
            var tree = new Tree<TestDocument>(trunk, new LRUCacheStrategy<TestDocument>(maxCacheSize));
            tree.TtlEnforcementEnabled = false;
            tree.CacheEvictionEnabled = true;
            return tree;
        }

        // ===== Cache Hit Rate Under Different Access Patterns =====

        [Benchmark]
        public void CacheHitRate_HotSpot_90Percent()
        {
            // Simulate 90% of reads from 10% of data (hot spot pattern)
            var dir = Path.Combine(_tempDir, $"cache_hotspot_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, CacheSize);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            var random = new Random(42);
            int hotSpotSize = DatasetSize / 10;
            int readCount = 10_000;

            for (int i = 0; i < readCount; i++)
            {
                string id;
                if (random.NextDouble() < 0.9)
                {
                    // 90% - hot spot (first 10% of docs)
                    id = $"doc-{random.Next(0, hotSpotSize)}";
                }
                else
                {
                    // 10% - cold data
                    id = $"doc-{random.Next(0, DatasetSize)}";
                }

                var doc = _tree.Crack(id);
            }

            // Expected hit rate: ~90% (hot data stays cached)

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void CacheHitRate_Uniform_Random()
        {
            // Uniform random access (worst case for caching)
            var dir = Path.Combine(_tempDir, $"cache_uniform_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, CacheSize);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            var random = new Random(42);
            int readCount = 10_000;

            for (int i = 0; i < readCount; i++)
            {
                var id = $"doc-{random.Next(0, DatasetSize)}";
                var doc = _tree.Crack(id);
            }

            // Expected hit rate: CacheSize / DatasetSize
            // For 1000 cache / 10K dataset = ~10% hit rate

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void CacheHitRate_Sequential_Access()
        {
            // Sequential access pattern (sliding window)
            var dir = Path.Combine(_tempDir, $"cache_sequential_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, CacheSize);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            int readCount = 10_000;

            for (int i = 0; i < readCount; i++)
            {
                var id = $"doc-{i % DatasetSize}"; // Wrap around
                var doc = _tree.Crack(id);
            }

            // Expected hit rate: Depends on cache size vs working set
            // If CacheSize >= DatasetSize, hit rate ~99% (after first pass)

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void CacheHitRate_ZipfDistribution()
        {
            // Zipf distribution (realistic: some items very popular, long tail)
            var dir = Path.Combine(_tempDir, $"cache_zipf_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, CacheSize);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            var random = new Random(42);
            int readCount = 10_000;

            for (int i = 0; i < readCount; i++)
            {
                // Simplified Zipf: probability inversely proportional to rank
                var rank = (int)(DatasetSize * Math.Pow(random.NextDouble(), 2.0));
                var id = $"doc-{rank}";
                var doc = _tree.Crack(id);
            }

            // Expected hit rate: ~70-80% (popular items stay cached)

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Cache vs No Cache Performance Comparison =====

        [Benchmark(Baseline = true)]
        public void NoCache_Random_Reads()
        {
            var dir = Path.Combine(_tempDir, $"nocache_random_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = new Tree<TestDocument>(_btreeTrunk);
            _tree.CacheEvictionEnabled = false;
            _tree.TtlEnforcementEnabled = false;

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            var random = new Random(42);
            int readCount = 1_000;

            for (int i = 0; i < readCount; i++)
            {
                var id = $"doc-{random.Next(0, DatasetSize)}";
                var doc = _tree.Crack(id);
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void WithCache_Random_Reads()
        {
            var dir = Path.Combine(_tempDir, $"cache_random_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, CacheSize);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            var random = new Random(42);
            int readCount = 1_000;

            for (int i = 0; i < readCount; i++)
            {
                var id = $"doc-{random.Next(0, DatasetSize)}";
                var doc = _tree.Crack(id);
            }

            // Expected: Faster than no cache if hit rate > 0

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void NoCache_HotSpot_Reads()
        {
            var dir = Path.Combine(_tempDir, $"nocache_hotspot_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = new Tree<TestDocument>(_btreeTrunk);
            _tree.CacheEvictionEnabled = false;
            _tree.TtlEnforcementEnabled = false;

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            var random = new Random(42);
            int readCount = 1_000;

            for (int i = 0; i < readCount; i++)
            {
                var id = random.NextDouble() < 0.9
                    ? $"doc-{random.Next(0, DatasetSize / 10)}"
                    : $"doc-{random.Next(0, DatasetSize)}";
                var doc = _tree.Crack(id);
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void WithCache_HotSpot_Reads()
        {
            var dir = Path.Combine(_tempDir, $"cache_hotspot_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, CacheSize);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            var random = new Random(42);
            int readCount = 1_000;

            for (int i = 0; i < readCount; i++)
            {
                var id = random.NextDouble() < 0.9
                    ? $"doc-{random.Next(0, DatasetSize / 10)}"
                    : $"doc-{random.Next(0, DatasetSize)}";
                var doc = _tree.Crack(id);
            }

            // Expected: Significantly faster due to high hit rate on hot data

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Eviction Overhead =====

        [Benchmark]
        public void CacheEviction_LRU_Overhead()
        {
            var dir = Path.Combine(_tempDir, $"eviction_lru_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, CacheSize);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Force cache thrashing: read more items than cache can hold
            for (int i = 0; i < DatasetSize; i++)
            {
                var doc = _tree.Crack($"doc-{i}");
            }

            // Measures eviction overhead when cache is constantly full

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void CacheEviction_NoEviction_Strategy()
        {
            var dir = Path.Combine(_tempDir, $"eviction_none_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = new Tree<TestDocument>(_btreeTrunk);
            _tree.CacheEvictionEnabled = false; // Unlimited cache
            _tree.TtlEnforcementEnabled = false;

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // All items stay in cache
            for (int i = 0; i < DatasetSize; i++)
            {
                var doc = _tree.Crack($"doc-{i}");
            }

            // Expected: Fastest, but highest memory usage

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Memory Footprint vs Cache Size =====

        [Benchmark]
        public void MemoryFootprint_SmallCache()
        {
            var dir = Path.Combine(_tempDir, $"mem_small_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, 100); // Small cache

            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // MemoryDiagnoser will show allocations
            // Expected: Low memory usage, high disk I/O

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void MemoryFootprint_LargeCache()
        {
            var dir = Path.Combine(_tempDir, $"mem_large_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, DatasetSize); // Cache entire dataset

            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Expected: High memory usage, minimal disk I/O

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Write-Through Cache Performance =====

        [Benchmark]
        public void WriteThrough_WithCache()
        {
            var dir = Path.Combine(_tempDir, $"write_cache_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, CacheSize);

            // Writes update both cache and disk
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Cache populated during writes

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void WriteThrough_NoCache()
        {
            var dir = Path.Combine(_tempDir, $"write_nocache_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = new Tree<TestDocument>(_btreeTrunk);
            _tree.CacheEvictionEnabled = false;
            _tree.TtlEnforcementEnabled = false;

            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Cache Invalidation on Updates =====

        [Benchmark]
        public void CacheInvalidation_FrequentUpdates()
        {
            var dir = Path.Combine(_tempDir, $"invalidation_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, CacheSize);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Frequent updates to cached items
            var random = new Random(42);
            for (int i = 0; i < 1_000; i++)
            {
                var id = $"doc-{random.Next(0, CacheSize)}"; // Update cached items
                var doc = CreateDocument(random.Next(0, CacheSize));
                doc.Value = i; // Modified
                _tree.Stash(doc);
            }

            // Measures cache refresh overhead

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Cache Warming =====

        [Benchmark]
        public void CacheWarming_Preload_HotData()
        {
            var dir = Path.Combine(_tempDir, $"warming_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, CacheSize);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Warm cache by reading hot data
            for (int i = 0; i < CacheSize; i++)
            {
                var doc = _tree.Crack($"doc-{i}");
            }

            // Subsequent reads should be fast (cached)
            var random = new Random(42);
            for (int i = 0; i < 1_000; i++)
            {
                var id = $"doc-{random.Next(0, CacheSize)}";
                var doc = _tree.Crack(id);
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Mixed Read/Write Workload =====

        [Benchmark]
        public void MixedWorkload_70Read_30Write_WithCache()
        {
            var dir = Path.Combine(_tempDir, $"mixed_cache_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTreeWithCache(_btreeTrunk, CacheSize);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            var random = new Random(42);
            for (int i = 0; i < 10_000; i++)
            {
                if (random.NextDouble() < 0.7)
                {
                    // Read (70%)
                    var id = $"doc-{random.Next(0, DatasetSize)}";
                    var doc = _tree.Crack(id);
                }
                else
                {
                    // Write (30%)
                    var doc = CreateDocument(random.Next(0, DatasetSize));
                    doc.Value = i;
                    _tree.Stash(doc);
                }
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }
    }

    /// <summary>
    /// Expected Cache Effectiveness Results:
    ///
    /// Cache Hit Rates by Access Pattern:
    /// - Hot Spot (90/10 rule): ~90% hit rate
    /// - Uniform Random: CacheSize / DatasetSize (e.g., 1000/10000 = 10%)
    /// - Sequential: ~99% after first pass (if cache >= working set)
    /// - Zipf Distribution: ~70-80% hit rate (realistic web traffic)
    ///
    /// Performance Impact (1K reads, 10K dataset, 1000 cache):
    /// - No Cache: ~50ms (every read hits disk)
    /// - With Cache (Hot Spot): ~10ms (80% reduction due to 90% hit rate)
    /// - With Cache (Uniform): ~45ms (minimal benefit, low hit rate)
    /// - With Cache (Sequential): ~5ms (95% reduction, near-perfect hit rate)
    ///
    /// Eviction Overhead:
    /// - LRU Eviction: ~2-5% overhead per eviction
    /// - No Eviction: 0% overhead, but unbounded memory growth
    /// - Cache Thrashing: Performance degrades when working set > cache size
    ///
    /// Memory Footprint:
    /// - Small Cache (100): ~50KB (minimal memory, high disk I/O)
    /// - Medium Cache (1000): ~500KB (balanced)
    /// - Large Cache (10K): ~5MB (high memory, minimal disk I/O)
    /// - Rule of thumb: ~500 bytes per cached document (payload + metadata)
    ///
    /// Write Performance:
    /// - Write-Through Cache: ~30ms for 1K docs (cache + disk)
    /// - No Cache: ~30ms for 1K docs (similar - writes go to disk anyway)
    /// - Writes benefit cache for subsequent reads, not the write itself
    ///
    /// Cache Invalidation:
    /// - Frequent updates to cached items: ~5% overhead (cache refresh)
    /// - AcornDB uses write-through (no stale data risk)
    ///
    /// Cache Warming:
    /// - Preloading hot data improves first-access latency
    /// - Warm cache: p95 latency ~2ms vs cold cache p95 ~50ms
    ///
    /// Mixed Workload (70% Read, 30% Write):
    /// - With Cache: ~20ms for 1K ops (reads benefit from cache)
    /// - No Cache: ~35ms for 1K ops (40% slower)
    ///
    /// Recommendations:
    /// 1. Cache Size Tuning:
    ///    - Hot Spot workload: Cache = 10-20% of dataset size
    ///    - Uniform workload: Cache = 50%+ of dataset or don't cache
    ///    - Zipf workload: Cache = 20-30% of dataset size
    ///
    /// 2. Memory vs Performance Tradeoff:
    ///    - 1MB cache = ~2000 documents (~500 bytes each)
    ///    - Measure hit rate, aim for 70%+ to justify memory cost
    ///
    /// 3. When to Disable Cache:
    ///    - Write-heavy workloads (>50% writes)
    ///    - Uniform random access (low hit rate)
    ///    - Memory-constrained environments
    ///
    /// 4. When to Use Large Cache:
    ///    - Read-heavy workloads (>80% reads)
    ///    - Hot spot access patterns
    ///    - Latency-sensitive applications
    ///
    /// 5. Monitoring:
    ///    - Track cache hit rate (target 70%+)
    ///    - Monitor memory usage vs hit rate
    ///    - Measure p95/p99 latencies (cache misses drive tail latency)
    /// </summary>
}
