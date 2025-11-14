using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Comprehensive trunk performance comparison across all storage backends.
    /// Helps users choose the right trunk for their use case.
    ///
    /// TRUNK COMPARISON PURPOSE:
    /// This benchmark tests ALL core trunk implementations to help users decide which storage backend fits their needs:
    /// - MemoryTrunk: Fastest (ephemeral, no persistence)
    /// - BTreeTrunk: Balanced (memory-mapped files, fast cold start)
    /// - DocumentStoreTrunk: Versioned (append-only log, full history)
    /// - FileTrunk: Simplest (individual files, easy to debug)
    ///
    /// Use these results to compare performance tradeoffs across storage strategies.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 5)]
    public class TrunkPerformanceBenchmarks
    {
        private Tree<TestDocument>? _memoryTree;
        private Tree<TestDocument>? _fileTree;
        private Tree<TestDocument>? _btreeTree;
        private Tree<TestDocument>? _docStoreTree;
        private BTreeTrunk<TestDocument>? _btreeTrunk;
        private DocumentStoreTrunk<TestDocument>? _docStoreTrunk;

        public class TestDocument
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int Value { get; set; }
            public DateTime Created { get; set; }
            public bool IsActive { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();
        }

        [Params(1_000, 10_000, 100_000)]
        public int DocumentCount;

        private string _tempDir = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"acorndb_bench_{Guid.NewGuid()}");
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

        // ===== Sequential Write Benchmarks =====

        [Benchmark(Baseline = true)]
        public void Sequential_Write_MemoryTrunk()
        {
            _memoryTree = CreateTree(new MemoryTrunk<TestDocument>());

            for (int i = 0; i < DocumentCount; i++)
            {
                _memoryTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Description = $"Description for document {i}",
                    Value = i,
                    Created = DateTime.UtcNow,
                    IsActive = i % 2 == 0,
                    Data = new byte[512] // 512 bytes per doc
                });
            }
        }

        [Benchmark]
        public void Sequential_Write_FileTrunk()
        {
            var fileDir = Path.Combine(_tempDir, $"file_{Guid.NewGuid()}");
            _fileTree = CreateTree(new FileTrunk<TestDocument>(fileDir));

            for (int i = 0; i < DocumentCount; i++)
            {
                _fileTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Description = $"Description for document {i}",
                    Value = i,
                    Created = DateTime.UtcNow,
                    IsActive = i % 2 == 0,
                    Data = new byte[512]
                });
            }
        }

        [Benchmark]
        public void Sequential_Write_BTreeTrunk()
        {
            var btreeDir = Path.Combine(_tempDir, $"btree_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(btreeDir);
            _btreeTree = CreateTree(_btreeTrunk);

            for (int i = 0; i < DocumentCount; i++)
            {
                _btreeTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Description = $"Description for document {i}",
                    Value = i,
                    Created = DateTime.UtcNow,
                    IsActive = i % 2 == 0,
                    Data = new byte[512]
                });
            }

            // Ensure flush for fair comparison
            _btreeTrunk?.Dispose();
            _btreeTrunk = null;
            _btreeTree = null;
        }

        [Benchmark]
        public void Sequential_Write_DocumentStoreTrunk()
        {
            var docStoreDir = Path.Combine(_tempDir, $"docstore_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(docStoreDir);
            _docStoreTree = CreateTree(_docStoreTrunk);

            for (int i = 0; i < DocumentCount; i++)
            {
                _docStoreTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Description = $"Description for document {i}",
                    Value = i,
                    Created = DateTime.UtcNow,
                    IsActive = i % 2 == 0,
                    Data = new byte[512]
                });
            }

            // Ensure flush
            _docStoreTrunk?.Dispose();
            _docStoreTrunk = null;
            _docStoreTree = null;
        }

        // ===== Random Read Benchmarks =====

        [Benchmark]
        public void Random_Read_MemoryTrunk()
        {
            _memoryTree = CreateTree(new MemoryTrunk<TestDocument>());

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _memoryTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            // Random reads
            var random = new Random(42);
            for (int i = 0; i < DocumentCount; i++)
            {
                var id = $"doc-{random.Next(0, DocumentCount)}";
                var doc = _memoryTree.Crack(id);
            }
        }

        [Benchmark]
        public void Random_Read_FileTrunk()
        {
            var fileDir = Path.Combine(_tempDir, $"file_read_{Guid.NewGuid()}");
            _fileTree = CreateTree(new FileTrunk<TestDocument>(fileDir));

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _fileTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            // Random reads
            var random = new Random(42);
            for (int i = 0; i < DocumentCount; i++)
            {
                var id = $"doc-{random.Next(0, DocumentCount)}";
                var doc = _fileTree.Crack(id);
            }
        }

        [Benchmark]
        public void Random_Read_BTreeTrunk()
        {
            var btreeDir = Path.Combine(_tempDir, $"btree_read_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(btreeDir);
            _btreeTree = CreateTree(_btreeTrunk);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _btreeTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            // Random reads
            var random = new Random(42);
            for (int i = 0; i < DocumentCount; i++)
            {
                var id = $"doc-{random.Next(0, DocumentCount)}";
                var doc = _btreeTree.Crack(id);
            }

            _btreeTrunk?.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void Random_Read_DocumentStoreTrunk()
        {
            var docStoreDir = Path.Combine(_tempDir, $"docstore_read_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(docStoreDir);
            _docStoreTree = CreateTree(_docStoreTrunk);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _docStoreTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            // Random reads
            var random = new Random(42);
            for (int i = 0; i < DocumentCount; i++)
            {
                var id = $"doc-{random.Next(0, DocumentCount)}";
                var doc = _docStoreTree.Crack(id);
            }

            _docStoreTrunk?.Dispose();
            _docStoreTrunk = null;
        }

        // ===== Mixed Workload (70% Read, 30% Write) =====

        [Benchmark]
        public void Mixed_70Read_30Write_MemoryTrunk()
        {
            _memoryTree = CreateTree(new MemoryTrunk<TestDocument>());

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _memoryTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            // Mixed workload
            var random = new Random(42);
            for (int i = 0; i < DocumentCount; i++)
            {
                if (random.NextDouble() < 0.7)
                {
                    // Read (70%)
                    var id = $"doc-{random.Next(0, DocumentCount)}";
                    var doc = _memoryTree.Crack(id);
                }
                else
                {
                    // Write (30%)
                    _memoryTree.Stash(new TestDocument
                    {
                        Id = $"doc-{random.Next(0, DocumentCount)}",
                        Name = $"Updated {i}",
                        Value = i * 2
                    });
                }
            }
        }

        [Benchmark]
        public void Mixed_70Read_30Write_BTreeTrunk()
        {
            var btreeDir = Path.Combine(_tempDir, $"btree_mixed_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(btreeDir);
            _btreeTree = CreateTree(_btreeTrunk);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _btreeTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            // Mixed workload
            var random = new Random(42);
            for (int i = 0; i < DocumentCount; i++)
            {
                if (random.NextDouble() < 0.7)
                {
                    // Read (70%)
                    var id = $"doc-{random.Next(0, DocumentCount)}";
                    var doc = _btreeTree.Crack(id);
                }
                else
                {
                    // Write (30%)
                    _btreeTree.Stash(new TestDocument
                    {
                        Id = $"doc-{random.Next(0, DocumentCount)}",
                        Name = $"Updated {i}",
                        Value = i * 2
                    });
                }
            }

            _btreeTrunk?.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void Mixed_70Read_30Write_DocumentStoreTrunk()
        {
            var docStoreDir = Path.Combine(_tempDir, $"docstore_mixed_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(docStoreDir);
            _docStoreTree = CreateTree(_docStoreTrunk);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _docStoreTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            // Mixed workload
            var random = new Random(42);
            for (int i = 0; i < DocumentCount; i++)
            {
                if (random.NextDouble() < 0.7)
                {
                    // Read (70%)
                    var id = $"doc-{random.Next(0, DocumentCount)}";
                    var doc = _docStoreTree.Crack(id);
                }
                else
                {
                    // Write (30%)
                    _docStoreTree.Stash(new TestDocument
                    {
                        Id = $"doc-{random.Next(0, DocumentCount)}",
                        Name = $"Updated {i}",
                        Value = i * 2
                    });
                }
            }

            _docStoreTrunk?.Dispose();
            _docStoreTrunk = null;
        }

        // ===== Cold Start (Startup Time) =====

        [Benchmark]
        public void Startup_Time_FileTrunk_ColdLoad()
        {
            var fileDir = Path.Combine(_tempDir, $"file_startup_{Guid.NewGuid()}");
            var setupTree = CreateTree(new FileTrunk<TestDocument>(fileDir));

            // Pre-populate
            for (int i = 0; i < Math.Min(10_000, DocumentCount); i++)
            {
                setupTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            setupTree = null;

            // Measure cold start
            _fileTree = CreateTree(new FileTrunk<TestDocument>(fileDir));
        }

        [Benchmark]
        public void Startup_Time_BTreeTrunk_ColdLoad()
        {
            var btreeDir = Path.Combine(_tempDir, $"btree_startup_{Guid.NewGuid()}");
            var setupTrunk = new BTreeTrunk<TestDocument>(btreeDir);
            var setupTree = CreateTree(setupTrunk);

            // Pre-populate
            for (int i = 0; i < Math.Min(10_000, DocumentCount); i++)
            {
                setupTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            setupTrunk.Dispose();
            setupTree = null;

            // Measure cold start
            _btreeTrunk = new BTreeTrunk<TestDocument>(btreeDir);
            _btreeTree = CreateTree(_btreeTrunk);

            _btreeTrunk?.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void Startup_Time_DocumentStoreTrunk_ColdLoad()
        {
            var docStoreDir = Path.Combine(_tempDir, $"docstore_startup_{Guid.NewGuid()}");
            var setupTrunk = new DocumentStoreTrunk<TestDocument>(docStoreDir);
            var setupTree = CreateTree(setupTrunk);

            // Pre-populate
            for (int i = 0; i < Math.Min(10_000, DocumentCount); i++)
            {
                setupTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            setupTrunk.Dispose();
            setupTree = null;

            // Measure cold start (includes log replay)
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(docStoreDir);
            _docStoreTree = CreateTree(_docStoreTrunk);

            _docStoreTrunk?.Dispose();
            _docStoreTrunk = null;
        }

        // ===== Update Performance =====

        [Benchmark]
        public void Update_Performance_MemoryTrunk()
        {
            _memoryTree = CreateTree(new MemoryTrunk<TestDocument>());

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _memoryTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            // Update all documents
            for (int i = 0; i < DocumentCount; i++)
            {
                _memoryTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Updated Document {i}",
                    Value = i * 2
                });
            }
        }

        [Benchmark]
        public void Update_Performance_BTreeTrunk()
        {
            var btreeDir = Path.Combine(_tempDir, $"btree_update_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(btreeDir);
            _btreeTree = CreateTree(_btreeTrunk);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _btreeTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            // Update all documents
            for (int i = 0; i < DocumentCount; i++)
            {
                _btreeTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Updated Document {i}",
                    Value = i * 2
                });
            }

            _btreeTrunk?.Dispose();
            _btreeTrunk = null;
        }
    }

    /// <summary>
    /// Expected Results Guide:
    ///
    /// Sequential Writes (1K docs):
    /// - MemoryTrunk: ~2-5ms (baseline, in-memory)
    /// - FileTrunk: ~50-100ms (file I/O overhead)
    /// - BTreeTrunk: ~10-30ms (memory-mapped + buffering)
    /// - DocumentStoreTrunk: ~15-40ms (append-only log)
    ///
    /// Random Reads (1K docs):
    /// - MemoryTrunk: ~0.5-1ms (direct dictionary lookup)
    /// - FileTrunk: ~20-50ms (file I/O per read)
    /// - BTreeTrunk: ~5-15ms (memory-mapped access)
    /// - DocumentStoreTrunk: ~0.5-1ms (in-memory with log)
    ///
    /// Startup Time (10K docs):
    /// - FileTrunk: ~50-100ms (load from disk)
    /// - BTreeTrunk: ~10-30ms (index loading)
    /// - DocumentStoreTrunk: ~100-200ms (log replay)
    ///
    /// Recommendation Matrix:
    /// - Ultra-low latency, no persistence: MemoryTrunk
    /// - Balanced performance + persistence: BTreeTrunk
    /// - Versioning/history required: DocumentStoreTrunk
    /// - Simple file-per-document: FileTrunk
    /// </summary>
}
