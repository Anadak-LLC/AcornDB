using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using System.Diagnostics;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for large dataset performance and scaling behavior.
    /// Critical for understanding memory footprint, query degradation, and production limits.
    ///
    /// TRUNK SELECTION RATIONALE:
    /// - Cold Load: BTreeTrunk + DocumentStoreTrunk (tests startup time from disk)
    /// - Memory Footprint: All trunk types (shows memory usage differences)
    /// - Query Benchmarks: MemoryTrunk (isolates query algorithm performance without I/O)
    /// - Bulk Insert: All persistent trunks (compares write throughput)
    /// - Disk Space: BTreeTrunk + DocumentStoreTrunk (measures storage efficiency)
    ///
    /// Why query benchmarks use MemoryTrunk:
    /// Query scalability tests should measure algorithm performance (O(1) vs O(n)) without
    /// interference from disk I/O. Using MemoryTrunk isolates pure query performance.
    /// For real-world query latency with persistence, see RealWorldWorkloadBenchmarks.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 1, iterationCount: 3)] // Reduced iterations for large datasets
    public class ScalabilityBenchmarks
    {
        private BTreeTrunk<TestDocument>? _btreeTrunk;
        private DocumentStoreTrunk<TestDocument>? _docStoreTrunk;
        private Tree<TestDocument>? _tree;

        public class TestDocument
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public int Value { get; set; }
            public DateTime Timestamp { get; set; }
            public string[] Tags { get; set; } = Array.Empty<string>();
        }

        [Params(10_000, 100_000, 1_000_000)]
        public int DatasetSize;

        private string _tempDir = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"acorndb_scale_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _btreeTrunk?.Dispose();
            _docStoreTrunk?.Dispose();

            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); } catch { }
            }
        }

        private Tree<TestDocument> CreateTree(ITrunk<TestDocument> trunk)
        {
            var tree = new Tree<TestDocument>(trunk);
            tree.TtlEnforcementEnabled = false;
            tree.CacheEvictionEnabled = false;
            return tree;
        }

        private TestDocument CreateDocument(int index)
        {
            return new TestDocument
            {
                Id = $"doc-{index}",
                Name = $"Document {index}",
                Category = $"Category-{index % 100}",
                Value = index,
                Timestamp = DateTime.UtcNow.AddMinutes(-index),
                Tags = new[] { $"tag-{index % 10}", $"tag-{index % 20}" }
            };
        }

        // ===== Cold Load Performance (Startup Time) =====

        [Benchmark]
        public void ColdLoad_BTreeTrunk_Startup_Time()
        {
            var dir = Path.Combine(_tempDir, $"btree_cold_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            // Pre-populate and persist
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _tree = null;
            _btreeTrunk.Dispose();

            // Benchmark: Cold load from disk
            var sw = Stopwatch.StartNew();
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            // Force load by accessing count
            var count = _tree.NutCount;
            sw.Stop();

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void ColdLoad_DocumentStoreTrunk_Startup_Time()
        {
            var dir = Path.Combine(_tempDir, $"docstore_cold_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            // Pre-populate and persist
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _tree = null;
            _docStoreTrunk.Dispose();

            // Benchmark: Cold load from disk (replay log)
            var sw = Stopwatch.StartNew();
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            var count = _tree.NutCount;
            sw.Stop();

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }

        // ===== Memory Footprint Growth =====

        [Benchmark]
        public void MemoryFootprint_MemoryTrunk_FullDataset()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Load full dataset into memory
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // BenchmarkDotNet's MemoryDiagnoser will capture allocations
            // Expected: ~200-300 bytes per document + overhead
        }

        [Benchmark]
        public void MemoryFootprint_BTreeTrunk_FullDataset()
        {
            var dir = Path.Combine(_tempDir, $"btree_mem_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Memory-mapped files should have lower in-memory footprint
            // Expected: ~100-150 bytes per document (metadata only)

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void MemoryFootprint_DocumentStoreTrunk_FullDataset()
        {
            var dir = Path.Combine(_tempDir, $"docstore_mem_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Append-only log with in-memory index
            // Expected: Similar to MemoryTrunk (full payload in memory)

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }

        // ===== Query Performance Degradation =====

        [Benchmark]
        public void QueryPerformance_Random_Read_10Percent()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Benchmark: Random reads of 10% of dataset
            var random = new Random(42);
            int queryCount = DatasetSize / 10;

            for (int i = 0; i < queryCount; i++)
            {
                var id = $"doc-{random.Next(0, DatasetSize)}";
                var doc = _tree.Crack(id);
            }

            // Expected: O(1) lookup, should scale linearly with dataset size
        }

        [Benchmark]
        public void QueryPerformance_Sequential_Read_10Percent()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Benchmark: Sequential reads
            int queryCount = DatasetSize / 10;

            for (int i = 0; i < queryCount; i++)
            {
                var doc = _tree.Crack($"doc-{i}");
            }
        }

        // ===== Full Scan Performance =====

        [Benchmark]
        public void FullScan_AllDocuments_NoFilter()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Benchmark: Full scan using All()
            var all = _tree.NutShells().ToList();

            // Expected: O(n) scan, should scale linearly
        }

        [Benchmark]
        public void FullScan_WithFilter_SimpleCondition()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Benchmark: Full scan with filter (WHERE Value > threshold)
            int threshold = DatasetSize / 2;
            var filtered = _tree.NutShells()
                .Where(n => n.Payload.Value > threshold)
                .ToList();

            // Expected: Still O(n) but with predicate evaluation overhead
        }

        [Benchmark]
        public void FullScan_WithAggregation()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Benchmark: Full scan with aggregation (SUM, AVG, GROUP BY)
            var categoryStats = _tree.NutShells()
                .GroupBy(n => n.Payload.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Count = g.Count(),
                    AvgValue = g.Average(n => n.Payload.Value)
                })
                .ToList();

            // Expected: O(n) scan + grouping overhead
        }

        // ===== Update Performance at Scale =====

        [Benchmark]
        public void UpdatePerformance_10Percent_Random()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Benchmark: Update 10% of dataset randomly
            var random = new Random(42);
            int updateCount = DatasetSize / 10;

            for (int i = 0; i < updateCount; i++)
            {
                int index = random.Next(0, DatasetSize);
                var doc = CreateDocument(index);
                doc.Value = index * 2; // Modified value
                _tree.Stash(doc);
            }
        }

        // ===== Delete Performance at Scale =====

        [Benchmark]
        public void DeletePerformance_10Percent_Random()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Benchmark: Delete 10% of dataset randomly
            var random = new Random(42);
            int deleteCount = DatasetSize / 10;

            for (int i = 0; i < deleteCount; i++)
            {
                var id = $"doc-{random.Next(0, DatasetSize)}";
                _tree.Toss(id);
            }
        }

        // ===== Bulk Insert Performance =====

        [Benchmark]
        public void BulkInsert_Sequential_MemoryTrunk()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Benchmark: Bulk insert of full dataset
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Expected: O(n) with minimal overhead per document
        }

        [Benchmark]
        public void BulkInsert_Sequential_BTreeTrunk()
        {
            var dir = Path.Combine(_tempDir, $"btree_bulk_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void BulkInsert_Sequential_DocumentStoreTrunk()
        {
            var dir = Path.Combine(_tempDir, $"docstore_bulk_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }

        // ===== GC Pressure Analysis =====

        [Benchmark]
        public void GCPressure_Rapid_Allocations()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Benchmark: Rapid allocations (no reuse)
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Category = $"Category-{i % 100}",
                    Value = i,
                    Timestamp = DateTime.UtcNow,
                    Tags = new[] { $"tag-{i % 10}" }
                });
            }

            // MemoryDiagnoser will show Gen0/Gen1/Gen2 collections
            // Expected: High Gen0 collections, some Gen1, rare Gen2
        }

        // ===== Disk Space Consumption =====

        [Benchmark]
        public void DiskSpace_BTreeTrunk_FullDataset()
        {
            var dir = Path.Combine(_tempDir, $"btree_disk_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _btreeTrunk.Dispose();

            // Calculate disk usage
            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            long totalSize = files.Sum(f => new FileInfo(f).Length);

            // Expected: ~300-500 bytes per document (JSON + B-tree overhead)

            _btreeTrunk = null;
        }

        [Benchmark]
        public void DiskSpace_DocumentStoreTrunk_FullDataset()
        {
            var dir = Path.Combine(_tempDir, $"docstore_disk_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _docStoreTrunk.Dispose();

            // Calculate disk usage
            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            long totalSize = files.Sum(f => new FileInfo(f).Length);

            // Expected: Similar to BTree (JSON serialization + log metadata)

            _docStoreTrunk = null;
        }

        // ===== Pagination Performance =====

        [Benchmark]
        public void Pagination_Skip_Take_FirstPage()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Benchmark: First page (Skip 0, Take 100)
            var page = _tree.NutShells()
                .Skip(0)
                .Take(100)
                .ToList();

            // Expected: O(100) - efficient
        }

        [Benchmark]
        public void Pagination_Skip_Take_MiddlePage()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Benchmark: Middle page (Skip DatasetSize/2, Take 100)
            var page = _tree.NutShells()
                .Skip(DatasetSize / 2)
                .Take(100)
                .ToList();

            // Expected: O(DatasetSize/2 + 100) - degrades with offset
        }

        [Benchmark]
        public void Pagination_Skip_Take_LastPage()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Pre-populate
            for (int i = 0; i < DatasetSize; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Benchmark: Last page (Skip DatasetSize-100, Take 100)
            var page = _tree.NutShells()
                .Skip(DatasetSize - 100)
                .Take(100)
                .ToList();

            // Expected: O(DatasetSize) - worst case for Skip/Take
        }
    }

    /// <summary>
    /// Expected Scalability Results:
    ///
    /// Cold Load Startup Time:
    /// - 10K docs: BTree ~50ms, DocumentStore ~100ms (log replay)
    /// - 100K docs: BTree ~200ms, DocumentStore ~500ms
    /// - 1M docs: BTree ~1-2s, DocumentStore ~5-8s (log replay bottleneck)
    ///
    /// Memory Footprint:
    /// - MemoryTrunk: ~300 bytes/doc → 300MB for 1M docs
    /// - BTreeTrunk: ~150 bytes/doc → 150MB for 1M docs (memory-mapped files)
    /// - DocumentStoreTrunk: ~300 bytes/doc → 300MB for 1M docs (in-memory)
    ///
    /// Query Performance:
    /// - Random Read: O(1) dictionary lookup - scales well
    /// - Full Scan: O(n) - 10K docs ~5ms, 100K docs ~50ms, 1M docs ~500ms
    /// - Filtered Scan: O(n) + predicate eval - +20% overhead
    /// - Aggregation: O(n) + grouping - +50% overhead
    ///
    /// Bulk Insert:
    /// - MemoryTrunk: 10K ~5ms, 100K ~50ms, 1M ~500ms (linear)
    /// - BTreeTrunk: 10K ~20ms, 100K ~300ms, 1M ~4s (file I/O overhead)
    /// - DocumentStoreTrunk: 10K ~30ms, 100K ~400ms, 1M ~5s (log writes)
    ///
    /// GC Pressure:
    /// - 10K docs: Gen0 ~5, Gen1 ~1, Gen2 ~0
    /// - 100K docs: Gen0 ~50, Gen1 ~10, Gen2 ~2
    /// - 1M docs: Gen0 ~500, Gen1 ~100, Gen2 ~20 (potential performance impact)
    ///
    /// Disk Space:
    /// - BTree: ~400 bytes/doc → 400MB for 1M docs
    /// - DocumentStore: ~400 bytes/doc → 400MB for 1M docs (similar JSON overhead)
    ///
    /// Pagination:
    /// - First Page (Skip 0): O(100) - fast for all dataset sizes
    /// - Middle Page (Skip 500K): O(500K) - 10K ~0.5ms, 1M ~250ms
    /// - Last Page (Skip 999K): O(1M) - degrades significantly
    ///
    /// Recommendation:
    /// - MemoryTrunk: Best for datasets < 100K docs (fast, but memory-limited)
    /// - BTreeTrunk: Best for datasets 100K-10M docs (balanced memory/disk)
    /// - DocumentStoreTrunk: Best for write-heavy workloads with history/versioning
    /// - Consider pagination alternatives (cursor-based) for large datasets
    /// - Monitor GC pressure for datasets > 1M docs
    /// </summary>
}
