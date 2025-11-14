using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for concurrent access patterns and thread safety.
    /// Critical for validating production multi-threaded scenarios.
    ///
    /// TRUNK SELECTION RATIONALE:
    /// Tests multiple trunk types to validate thread safety across storage backends:
    /// - MemoryTrunk: Tests raw concurrency without I/O (baseline thread safety)
    /// - BTreeTrunk: Tests concurrent file-based access (lock contention + I/O)
    /// - DocumentStoreTrunk: Tests concurrent append-only log writes
    ///
    /// Thread safety must work across ALL trunk types for production readiness.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 5)]
    public class ConcurrencyBenchmarks
    {
        private Tree<TestDocument>? _memoryTree;
        private Tree<TestDocument>? _btreeTree;
        private Tree<TestDocument>? _docStoreTree;
        private BTreeTrunk<TestDocument>? _btreeTrunk;
        private DocumentStoreTrunk<TestDocument>? _docStoreTrunk;

        public class TestDocument
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
            public DateTime Timestamp { get; set; }
        }

        [Params(2, 4, 8, 16)]
        public int ThreadCount;

        [Params(1_000, 10_000)]
        public int OperationsPerThread;

        private string _tempDir = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"acorndb_concurrency_{Guid.NewGuid()}");
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

        // ===== Concurrent Writes (Single Tree) =====

        [Benchmark(Baseline = true)]
        public void Concurrent_Writes_MemoryTrunk()
        {
            _memoryTree = CreateTree(new MemoryTrunk<TestDocument>());

            var tasks = new Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < OperationsPerThread; i++)
                    {
                        _memoryTree.Stash(new TestDocument
                        {
                            Id = $"doc-{threadId}-{i}",
                            Name = $"Document from thread {threadId}",
                            Value = i,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                });
            }

            Task.WaitAll(tasks);

            // Verify all writes succeeded
            var expectedCount = ThreadCount * OperationsPerThread;
            if (_memoryTree.NutCount != expectedCount)
            {
                throw new Exception($"Expected {expectedCount} documents, got {_memoryTree.NutCount}");
            }
        }

        [Benchmark]
        public void Concurrent_Writes_BTreeTrunk()
        {
            var dir = Path.Combine(_tempDir, $"btree_writes_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _btreeTree = CreateTree(_btreeTrunk);

            var tasks = new Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < OperationsPerThread; i++)
                    {
                        _btreeTree.Stash(new TestDocument
                        {
                            Id = $"doc-{threadId}-{i}",
                            Name = $"Document from thread {threadId}",
                            Value = i,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                });
            }

            Task.WaitAll(tasks);

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void Concurrent_Writes_DocumentStoreTrunk()
        {
            var dir = Path.Combine(_tempDir, $"docstore_writes_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _docStoreTree = CreateTree(_docStoreTrunk);

            var tasks = new Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < OperationsPerThread; i++)
                    {
                        _docStoreTree.Stash(new TestDocument
                        {
                            Id = $"doc-{threadId}-{i}",
                            Name = $"Document from thread {threadId}",
                            Value = i,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                });
            }

            Task.WaitAll(tasks);

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }

        // ===== Concurrent Reads (High Contention) =====

        [Benchmark]
        public void Concurrent_Reads_MemoryTrunk_HighContention()
        {
            _memoryTree = CreateTree(new MemoryTrunk<TestDocument>());

            // Pre-populate
            for (int i = 0; i < 1000; i++)
            {
                _memoryTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            // Concurrent reads of same documents (high contention)
            var tasks = new Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    var random = new Random(t);
                    for (int i = 0; i < OperationsPerThread; i++)
                    {
                        var id = $"doc-{random.Next(0, 100)}"; // Read from small pool (high contention)
                        var doc = _memoryTree.Crack(id);
                    }
                });
            }

            Task.WaitAll(tasks);
        }

        [Benchmark]
        public void Concurrent_Reads_BTreeTrunk_HighContention()
        {
            var dir = Path.Combine(_tempDir, $"btree_reads_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _btreeTree = CreateTree(_btreeTrunk);

            // Pre-populate
            for (int i = 0; i < 1000; i++)
            {
                _btreeTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            // Concurrent reads
            var tasks = new Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    var random = new Random(t);
                    for (int i = 0; i < OperationsPerThread; i++)
                    {
                        var id = $"doc-{random.Next(0, 100)}";
                        var doc = _btreeTree.Crack(id);
                    }
                });
            }

            Task.WaitAll(tasks);

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Mixed Workload (70% Read, 30% Write) =====

        [Benchmark]
        public void Concurrent_Mixed_70Read_30Write_MemoryTrunk()
        {
            _memoryTree = CreateTree(new MemoryTrunk<TestDocument>());

            // Pre-populate
            for (int i = 0; i < 1000; i++)
            {
                _memoryTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            var tasks = new Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    var random = new Random(threadId);
                    for (int i = 0; i < OperationsPerThread; i++)
                    {
                        if (random.NextDouble() < 0.7)
                        {
                            // Read (70%)
                            var id = $"doc-{random.Next(0, 1000)}";
                            var doc = _memoryTree.Crack(id);
                        }
                        else
                        {
                            // Write (30%)
                            _memoryTree.Stash(new TestDocument
                            {
                                Id = $"doc-{threadId}-{i}",
                                Name = $"Thread {threadId} write",
                                Value = i
                            });
                        }
                    }
                });
            }

            Task.WaitAll(tasks);
        }

        [Benchmark]
        public void Concurrent_Mixed_70Read_30Write_BTreeTrunk()
        {
            var dir = Path.Combine(_tempDir, $"btree_mixed_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _btreeTree = CreateTree(_btreeTrunk);

            // Pre-populate
            for (int i = 0; i < 1000; i++)
            {
                _btreeTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            var tasks = new Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    var random = new Random(threadId);
                    for (int i = 0; i < OperationsPerThread; i++)
                    {
                        if (random.NextDouble() < 0.7)
                        {
                            // Read (70%)
                            var id = $"doc-{random.Next(0, 1000)}";
                            var doc = _btreeTree.Crack(id);
                        }
                        else
                        {
                            // Write (30%)
                            _btreeTree.Stash(new TestDocument
                            {
                                Id = $"doc-{threadId}-{i}",
                                Name = $"Thread {threadId} write",
                                Value = i
                            });
                        }
                    }
                });
            }

            Task.WaitAll(tasks);

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Lock Contention Analysis =====

        [Benchmark]
        public void Lock_Contention_Hotspot_Updates()
        {
            _memoryTree = CreateTree(new MemoryTrunk<TestDocument>());

            // Pre-populate a single hot document
            _memoryTree.Stash(new TestDocument
            {
                Id = "hot-document",
                Name = "Frequently Updated",
                Value = 0
            });

            // All threads update the same document (maximum contention)
            var tasks = new Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < OperationsPerThread; i++)
                    {
                        _memoryTree.Stash(new TestDocument
                        {
                            Id = "hot-document",
                            Name = $"Updated by thread {threadId}",
                            Value = i
                        });
                    }
                });
            }

            Task.WaitAll(tasks);
        }

        // ===== DocumentStoreTrunk Concurrent Log Writes =====

        [Benchmark]
        public void DocumentStoreTrunk_Concurrent_Log_Writes()
        {
            var dir = Path.Combine(_tempDir, $"docstore_log_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _docStoreTree = CreateTree(_docStoreTrunk);

            // Test append-only log concurrency
            var tasks = new Task[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < OperationsPerThread; i++)
                    {
                        _docStoreTree.Stash(new TestDocument
                        {
                            Id = $"doc-{threadId}-{i}",
                            Name = $"Thread {threadId}",
                            Value = i
                        });
                    }
                });
            }

            Task.WaitAll(tasks);

            // Verify log integrity (all writes recorded)
            var expectedCount = ThreadCount * OperationsPerThread;
            if (_docStoreTree.NutCount != expectedCount)
            {
                throw new Exception($"Log corruption: Expected {expectedCount} documents, got {_docStoreTree.NutCount}");
            }

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }

        // ===== Thread Safety Stress Test =====

        [Benchmark]
        public void ThreadSafety_StressTest_AllOperations()
        {
            _memoryTree = CreateTree(new MemoryTrunk<TestDocument>());

            // Pre-populate
            for (int i = 0; i < 1000; i++)
            {
                _memoryTree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Value = i
                });
            }

            var tasks = new Task[ThreadCount];
            var errors = new ConcurrentBag<Exception>();

            for (int t = 0; t < ThreadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        var random = new Random(threadId);
                        for (int i = 0; i < OperationsPerThread; i++)
                        {
                            var operation = random.Next(0, 4);
                            var id = $"doc-{random.Next(0, 1000)}";

                            switch (operation)
                            {
                                case 0: // Stash
                                    _memoryTree.Stash(new TestDocument
                                    {
                                        Id = id,
                                        Name = $"Thread {threadId}",
                                        Value = i
                                    });
                                    break;
                                case 1: // Crack
                                    var doc = _memoryTree.Crack(id);
                                    break;
                                case 2: // Toss
                                    _memoryTree.Toss(id);
                                    break;
                                case 3: // NutCount
                                    var count = _memoryTree.NutCount;
                                    break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                });
            }

            Task.WaitAll(tasks);

            // Verify no thread safety violations
            if (errors.Count > 0)
            {
                throw new AggregateException("Thread safety violations detected", errors);
            }
        }

        // ===== Scalability Analysis =====

        [Benchmark]
        public void Scalability_Throughput_vs_ThreadCount()
        {
            _memoryTree = CreateTree(new MemoryTrunk<TestDocument>());

            var totalOps = ThreadCount * OperationsPerThread;
            var tasks = new Task[ThreadCount];

            for (int t = 0; t < ThreadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < OperationsPerThread; i++)
                    {
                        _memoryTree.Stash(new TestDocument
                        {
                            Id = $"doc-{threadId}-{i}",
                            Name = $"Document {threadId}",
                            Value = i
                        });
                    }
                });
            }

            Task.WaitAll(tasks);

            // BenchmarkDotNet will show throughput scaling across thread counts
            // Ideal: Linear scaling (2x threads = 2x throughput)
            // Reality: Sub-linear due to lock contention
        }
    }

    /// <summary>
    /// Expected Concurrency Results:
    ///
    /// Concurrent Writes (MemoryTrunk):
    /// - 2 threads: ~1.8x throughput vs single thread (good scaling)
    /// - 4 threads: ~3.2x throughput (slight contention)
    /// - 8 threads: ~5.0x throughput (moderate contention)
    /// - 16 threads: ~7.0x throughput (lock contention visible)
    ///
    /// Concurrent Reads (Low Contention):
    /// - Near-linear scaling up to CPU core count
    /// - Memory bandwidth becomes bottleneck beyond cores
    ///
    /// Mixed Workload (70/30):
    /// - 2-4 threads: ~2.5-3.5x throughput
    /// - 8+ threads: Write lock contention starts to degrade scaling
    ///
    /// BTreeTrunk Concurrency:
    /// - Memory-mapped file access has additional kernel lock overhead
    /// - Expect 10-20% lower scaling vs pure MemoryTrunk
    ///
    /// DocumentStoreTrunk:
    /// - Append-only log has write serialization
    /// - Read performance scales well (in-memory)
    /// - Write performance: sub-linear scaling due to log lock
    ///
    /// Thread Safety Guarantee:
    /// - All operations must be thread-safe (zero errors)
    /// - No data corruption under concurrent access
    /// - Deterministic final state
    /// </summary>
}
