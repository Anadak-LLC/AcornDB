using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using System.Diagnostics;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for startup/shutdown/initialization lifecycle performance.
    /// Critical for serverless, edge computing, and container environments.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 5)]
    public class LifecycleBenchmarks
    {
        public class TestDocument
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public int Value { get; set; }
        }

        [Params(100, 1_000, 10_000)]
        public int DatasetSize;

        private string _tempDir = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"acorndb_lifecycle_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
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

        // ===== Cold Start (Initialization from Disk) =====

        [Benchmark]
        public void ColdStart_MemoryTrunk_Empty()
        {
            // Baseline: In-memory initialization (no disk)
            var sw = Stopwatch.StartNew();

            var trunk = new MemoryTrunk<TestDocument>();
            var tree = new Tree<TestDocument>(trunk);

            sw.Stop();

            // Expected: <1ms (minimal overhead)
        }

        [Benchmark]
        public void ColdStart_BTreeTrunk_Empty()
        {
            var dir = Path.Combine(_tempDir, $"btree_empty_{Guid.NewGuid()}");
            var sw = Stopwatch.StartNew();

            var trunk = new BTreeTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            sw.Stop();

            trunk.Dispose();

            // Expected: ~5-10ms (directory creation + file handles)
        }

        [Benchmark]
        public void ColdStart_BTreeTrunk_WithExistingData()
        {
            var dir = Path.Combine(_tempDir, $"btree_existing_{Guid.NewGuid()}");

            // Pre-populate data
            var setupTrunk = new BTreeTrunk<TestDocument>(dir);
            var setupTree = new Tree<TestDocument>(setupTrunk);
            for (int i = 0; i < DatasetSize; i++)
            {
                setupTree.Stash(CreateDocument(i));
            }
            setupTrunk.Dispose();

            // Benchmark: Cold start with existing data
            var sw = Stopwatch.StartNew();

            var trunk = new BTreeTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            // Force data load
            var count = tree.NutCount;

            sw.Stop();

            trunk.Dispose();

            // Expected: 100 docs ~10ms, 1K docs ~50ms, 10K docs ~300ms
        }

        [Benchmark]
        public void ColdStart_DocumentStoreTrunk_Empty()
        {
            var dir = Path.Combine(_tempDir, $"docstore_empty_{Guid.NewGuid()}");
            var sw = Stopwatch.StartNew();

            var trunk = new DocumentStoreTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            sw.Stop();

            trunk.Dispose();

            // Expected: ~5-10ms (log file initialization)
        }

        [Benchmark]
        public void ColdStart_DocumentStoreTrunk_WithExistingData()
        {
            var dir = Path.Combine(_tempDir, $"docstore_existing_{Guid.NewGuid()}");

            // Pre-populate data
            var setupTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            var setupTree = new Tree<TestDocument>(setupTrunk);
            for (int i = 0; i < DatasetSize; i++)
            {
                setupTree.Stash(CreateDocument(i));
            }
            setupTrunk.Dispose();

            // Benchmark: Cold start with log replay
            var sw = Stopwatch.StartNew();

            var trunk = new DocumentStoreTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            var count = tree.NutCount;

            sw.Stop();

            trunk.Dispose();

            // Expected: Slower than BTree due to log replay
            // 100 docs ~20ms, 1K docs ~150ms, 10K docs ~1.5s
        }

        [Benchmark]
        public void ColdStart_FileTrunk_Empty()
        {
            var dir = Path.Combine(_tempDir, $"file_empty_{Guid.NewGuid()}");
            var sw = Stopwatch.StartNew();

            var trunk = new FileTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            sw.Stop();

            // Expected: <5ms (directory creation)
        }

        [Benchmark]
        public void ColdStart_FileTrunk_WithExistingData()
        {
            var dir = Path.Combine(_tempDir, $"file_existing_{Guid.NewGuid()}");

            // Pre-populate data
            var setupTrunk = new FileTrunk<TestDocument>(dir);
            var setupTree = new Tree<TestDocument>(setupTrunk);
            for (int i = 0; i < DatasetSize; i++)
            {
                setupTree.Stash(CreateDocument(i));
            }

            // Benchmark: Cold start (directory scan)
            var sw = Stopwatch.StartNew();

            var trunk = new FileTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            var count = tree.NutCount;

            sw.Stop();

            // Expected: 100 docs ~30ms, 1K docs ~200ms, 10K docs ~2s (directory scan overhead)
        }

        // ===== Shutdown / Disposal Time =====

        [Benchmark]
        public void Shutdown_BTreeTrunk_CleanDispose()
        {
            var dir = Path.Combine(_tempDir, $"btree_shutdown_{Guid.NewGuid()}");
            var trunk = new BTreeTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            // Populate
            for (int i = 0; i < DatasetSize; i++)
            {
                tree.Stash(CreateDocument(i));
            }

            // Benchmark: Disposal time (flush to disk)
            var sw = Stopwatch.StartNew();

            trunk.Dispose();

            sw.Stop();

            // Expected: <10ms (memory-mapped files flush automatically)
        }

        [Benchmark]
        public void Shutdown_DocumentStoreTrunk_CleanDispose()
        {
            var dir = Path.Combine(_tempDir, $"docstore_shutdown_{Guid.NewGuid()}");
            var trunk = new DocumentStoreTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            for (int i = 0; i < DatasetSize; i++)
            {
                tree.Stash(CreateDocument(i));
            }

            var sw = Stopwatch.StartNew();

            trunk.Dispose();

            sw.Stop();

            // Expected: <20ms (log flush + file handle cleanup)
        }

        [Benchmark]
        public void Shutdown_MemoryTrunk_NoDisposal()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            var tree = new Tree<TestDocument>(trunk);

            for (int i = 0; i < DatasetSize; i++)
            {
                tree.Stash(CreateDocument(i));
            }

            // No disposal needed (in-memory only)
            // Expected: Instant (GC cleanup)
        }

        // ===== First Write After Startup =====

        [Benchmark]
        public void FirstWrite_BTreeTrunk_AfterColdStart()
        {
            var dir = Path.Combine(_tempDir, $"btree_firstwrite_{Guid.NewGuid()}");

            // Cold start
            var trunk = new BTreeTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            // Benchmark: First write (lazy initialization overhead)
            var sw = Stopwatch.StartNew();

            tree.Stash(CreateDocument(0));

            sw.Stop();

            trunk.Dispose();

            // Expected: ~5-10ms (file creation + first write)
        }

        [Benchmark]
        public void FirstWrite_DocumentStoreTrunk_AfterColdStart()
        {
            var dir = Path.Combine(_tempDir, $"docstore_firstwrite_{Guid.NewGuid()}");

            var trunk = new DocumentStoreTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            var sw = Stopwatch.StartNew();

            tree.Stash(CreateDocument(0));

            sw.Stop();

            trunk.Dispose();

            // Expected: ~10-20ms (log file creation + first append)
        }

        // ===== Rapid Start/Stop Cycles (Serverless Pattern) =====

        [Benchmark]
        public void ServerlessPattern_RapidCycles_BTreeTrunk()
        {
            var dir = Path.Combine(_tempDir, $"btree_serverless_{Guid.NewGuid()}");

            // Simulate serverless: start, write, stop, repeat
            for (int cycle = 0; cycle < 10; cycle++)
            {
                var trunk = new BTreeTrunk<TestDocument>(dir);
                var tree = new Tree<TestDocument>(trunk);

                // Quick operation
                tree.Stash(CreateDocument(cycle));

                trunk.Dispose();
            }

            // Expected: ~50-100ms for 10 cycles (startup + shutdown overhead)
        }

        [Benchmark]
        public void ServerlessPattern_RapidCycles_DocumentStoreTrunk()
        {
            var dir = Path.Combine(_tempDir, $"docstore_serverless_{Guid.NewGuid()}");

            for (int cycle = 0; cycle < 10; cycle++)
            {
                var trunk = new DocumentStoreTrunk<TestDocument>(dir);
                var tree = new Tree<TestDocument>(trunk);

                tree.Stash(CreateDocument(cycle));

                trunk.Dispose();
            }

            // Expected: ~100-200ms for 10 cycles (log replay + shutdown overhead)
        }

        [Benchmark]
        public void ServerlessPattern_RapidCycles_MemoryTrunk()
        {
            // Baseline: In-memory (no persistence)
            for (int cycle = 0; cycle < 10; cycle++)
            {
                var trunk = new MemoryTrunk<TestDocument>();
                var tree = new Tree<TestDocument>(trunk);

                tree.Stash(CreateDocument(cycle));

                // No disposal overhead
            }

            // Expected: <5ms for 10 cycles (minimal overhead)
        }

        // ===== Warm Cache After Cold Start =====

        [Benchmark]
        public void WarmCache_BTreeTrunk_AfterColdStart()
        {
            var dir = Path.Combine(_tempDir, $"btree_warmcache_{Guid.NewGuid()}");

            // Pre-populate
            var setupTrunk = new BTreeTrunk<TestDocument>(dir);
            var setupTree = new Tree<TestDocument>(setupTrunk);
            for (int i = 0; i < DatasetSize; i++)
            {
                setupTree.Stash(CreateDocument(i));
            }
            setupTrunk.Dispose();

            // Cold start
            var trunk = new BTreeTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk, new AcornDB.Cache.LRUCacheStrategy<TestDocument>(1000));
            tree.CacheEvictionEnabled = true;

            // Benchmark: Warm cache by reading hot data
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < Math.Min(1000, DatasetSize); i++)
            {
                var doc = tree.Crack($"doc-{i}");
            }

            sw.Stop();

            trunk.Dispose();

            // Expected: 100 docs ~10ms, 1K docs ~50ms (disk read + cache population)
        }

        // ===== Time to First Query (TTFQ) =====

        [Benchmark]
        public void TimeToFirstQuery_BTreeTrunk()
        {
            var dir = Path.Combine(_tempDir, $"btree_ttfq_{Guid.NewGuid()}");

            // Pre-populate
            var setupTrunk = new BTreeTrunk<TestDocument>(dir);
            var setupTree = new Tree<TestDocument>(setupTrunk);
            for (int i = 0; i < DatasetSize; i++)
            {
                setupTree.Stash(CreateDocument(i));
            }
            setupTrunk.Dispose();

            // Benchmark: Cold start + first query
            var sw = Stopwatch.StartNew();

            var trunk = new BTreeTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            var doc = tree.Crack("doc-0");

            sw.Stop();

            trunk.Dispose();

            // Expected: 100 docs ~15ms, 1K docs ~60ms, 10K docs ~350ms
            // Critical metric for user-facing applications
        }

        [Benchmark]
        public void TimeToFirstQuery_DocumentStoreTrunk()
        {
            var dir = Path.Combine(_tempDir, $"docstore_ttfq_{Guid.NewGuid()}");

            var setupTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            var setupTree = new Tree<TestDocument>(setupTrunk);
            for (int i = 0; i < DatasetSize; i++)
            {
                setupTree.Stash(CreateDocument(i));
            }
            setupTrunk.Dispose();

            var sw = Stopwatch.StartNew();

            var trunk = new DocumentStoreTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            var doc = tree.Crack("doc-0");

            sw.Stop();

            trunk.Dispose();

            // Expected: Slower than BTree due to log replay
            // 100 docs ~25ms, 1K docs ~160ms, 10K docs ~1.6s
        }

        [Benchmark]
        public void TimeToFirstQuery_MemoryTrunk()
        {
            var sw = Stopwatch.StartNew();

            var trunk = new MemoryTrunk<TestDocument>();
            var tree = new Tree<TestDocument>(trunk);

            tree.Stash(CreateDocument(0));
            var doc = tree.Crack("doc-0");

            sw.Stop();

            // Expected: <1ms (instant)
        }

        // ===== Lazy vs Eager Initialization =====

        [Benchmark]
        public void LazyInitialization_BTreeTrunk()
        {
            var dir = Path.Combine(_tempDir, $"btree_lazy_{Guid.NewGuid()}");

            // Pre-populate
            var setupTrunk = new BTreeTrunk<TestDocument>(dir);
            var setupTree = new Tree<TestDocument>(setupTrunk);
            for (int i = 0; i < DatasetSize; i++)
            {
                setupTree.Stash(CreateDocument(i));
            }
            setupTrunk.Dispose();

            // Benchmark: Lazy load (initialization deferred until first access)
            var sw = Stopwatch.StartNew();

            var trunk = new BTreeTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            // No data accessed yet - lazy initialization
            sw.Stop();

            trunk.Dispose();

            // Expected: <5ms (no data loaded)
        }

        [Benchmark]
        public void EagerInitialization_BTreeTrunk()
        {
            var dir = Path.Combine(_tempDir, $"btree_eager_{Guid.NewGuid()}");

            var setupTrunk = new BTreeTrunk<TestDocument>(dir);
            var setupTree = new Tree<TestDocument>(setupTrunk);
            for (int i = 0; i < DatasetSize; i++)
            {
                setupTree.Stash(CreateDocument(i));
            }
            setupTrunk.Dispose();

            // Benchmark: Eager load (force all data to load immediately)
            var sw = Stopwatch.StartNew();

            var trunk = new BTreeTrunk<TestDocument>(dir);
            var tree = new Tree<TestDocument>(trunk);

            // Force eager load
            var count = tree.NutCount;
            var all = tree.Nuts.ToList();

            sw.Stop();

            trunk.Dispose();

            // Expected: 100 docs ~15ms, 1K docs ~80ms, 10K docs ~500ms
        }

        // ===== Recovery After Unclean Shutdown =====

        [Benchmark]
        public void UncleanShutdown_Recovery_BTreeTrunk()
        {
            var dir = Path.Combine(_tempDir, $"btree_unclean_{Guid.NewGuid()}");

            // Simulate unclean shutdown (no Dispose)
            {
                var trunk = new BTreeTrunk<TestDocument>(dir);
                var tree = new Tree<TestDocument>(trunk);
                for (int i = 0; i < DatasetSize; i++)
                {
                    tree.Stash(CreateDocument(i));
                }
                // No Dispose() - simulates crash
            }

            // Benchmark: Recovery from unclean shutdown
            var sw = Stopwatch.StartNew();

            var recoveryTrunk = new BTreeTrunk<TestDocument>(dir);
            var recoveryTree = new Tree<TestDocument>(recoveryTrunk);

            var count = recoveryTree.NutCount;

            sw.Stop();

            recoveryTrunk.Dispose();

            // Expected: Similar to clean startup (memory-mapped files are durable)
        }

        [Benchmark]
        public void UncleanShutdown_Recovery_DocumentStoreTrunk()
        {
            var dir = Path.Combine(_tempDir, $"docstore_unclean_{Guid.NewGuid()}");

            // Simulate unclean shutdown
            {
                var trunk = new DocumentStoreTrunk<TestDocument>(dir);
                var tree = new Tree<TestDocument>(trunk);
                for (int i = 0; i < DatasetSize; i++)
                {
                    tree.Stash(CreateDocument(i));
                }
                // No Dispose() - simulates crash
            }

            var sw = Stopwatch.StartNew();

            var recoveryTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            var recoveryTree = new Tree<TestDocument>(recoveryTrunk);

            var count = recoveryTree.NutCount;

            sw.Stop();

            recoveryTrunk.Dispose();

            // Expected: Similar to clean startup (append-only log is crash-safe)
        }
    }

    /// <summary>
    /// Expected Lifecycle Performance Results:
    ///
    /// Cold Start (Empty Database):
    /// - MemoryTrunk: <1ms (instant)
    /// - BTreeTrunk: ~5-10ms (directory + file handles)
    /// - DocumentStoreTrunk: ~5-10ms (log file initialization)
    /// - FileTrunk: <5ms (directory creation)
    ///
    /// Cold Start (With Existing Data):
    /// - 100 docs:
    ///   - BTreeTrunk: ~10ms
    ///   - DocumentStoreTrunk: ~20ms (log replay)
    ///   - FileTrunk: ~30ms (directory scan)
    /// - 1K docs:
    ///   - BTreeTrunk: ~50ms
    ///   - DocumentStoreTrunk: ~150ms
    ///   - FileTrunk: ~200ms
    /// - 10K docs:
    ///   - BTreeTrunk: ~300ms
    ///   - DocumentStoreTrunk: ~1.5s
    ///   - FileTrunk: ~2s
    ///
    /// Shutdown / Disposal:
    /// - MemoryTrunk: Instant (no cleanup)
    /// - BTreeTrunk: <10ms (memory-mapped file flush)
    /// - DocumentStoreTrunk: <20ms (log flush)
    /// - FileTrunk: Instant (no buffering)
    ///
    /// First Write After Startup:
    /// - BTreeTrunk: ~5-10ms (lazy file creation)
    /// - DocumentStoreTrunk: ~10-20ms (log file creation + first append)
    /// - MemoryTrunk: <1ms (no overhead)
    ///
    /// Serverless Pattern (10 Rapid Start/Stop Cycles):
    /// - MemoryTrunk: <5ms (no overhead)
    /// - BTreeTrunk: ~50-100ms (startup + shutdown × 10)
    /// - DocumentStoreTrunk: ~100-200ms (log replay overhead × 10)
    ///
    /// Time to First Query (TTFQ):
    /// - Critical metric for user-facing applications
    /// - 100 docs:
    ///   - BTreeTrunk: ~15ms
    ///   - DocumentStoreTrunk: ~25ms
    ///   - MemoryTrunk: <1ms
    /// - 1K docs:
    ///   - BTreeTrunk: ~60ms
    ///   - DocumentStoreTrunk: ~160ms
    /// - 10K docs:
    ///   - BTreeTrunk: ~350ms
    ///   - DocumentStoreTrunk: ~1.6s
    ///
    /// Lazy vs Eager Initialization:
    /// - Lazy: <5ms startup, first query pays initialization cost
    /// - Eager: Full data load at startup, subsequent queries fast
    /// - Recommendation: Use lazy for serverless, eager for long-running services
    ///
    /// Unclean Shutdown Recovery:
    /// - BTreeTrunk: Same as clean startup (memory-mapped files durable)
    /// - DocumentStoreTrunk: Same as clean startup (append-only log crash-safe)
    /// - No data loss expected (both use OS-level durability guarantees)
    ///
    /// Optimization Recommendations:
    ///
    /// 1. Serverless Environments (AWS Lambda, Azure Functions):
    ///    - Use BTreeTrunk (fastest cold start)
    ///    - Keep dataset < 1K docs (TTFQ < 100ms)
    ///    - Consider MemoryTrunk for ephemeral data
    ///    - Pre-warm cache during initialization
    ///
    /// 2. Edge Computing (Cloudflare Workers, Vercel Edge):
    ///    - Use MemoryTrunk (instant startup)
    ///    - Sync to central DB periodically
    ///    - Keep dataset < 10K docs (memory limit)
    ///
    /// 3. Container Environments (Docker, Kubernetes):
    ///    - Use BTreeTrunk or DocumentStoreTrunk
    ///    - Accept 100-500ms startup cost
    ///    - Use persistent volumes for data
    ///
    /// 4. Mobile Apps (iOS, Android):
    ///    - Use BTreeTrunk (balanced cold start)
    ///    - Keep dataset < 10K docs
    ///    - Lazy initialization preferred
    ///
    /// 5. Desktop Applications:
    ///    - Any trunk type acceptable
    ///    - Eager initialization for better UX
    ///    - Cache warming on startup
    ///
    /// Key Insights:
    /// - BTreeTrunk: Best cold start for persistent storage (10-300ms)
    /// - DocumentStoreTrunk: Slower cold start due to log replay (20ms-1.5s)
    /// - MemoryTrunk: Instant startup, no durability
    /// - TTFQ is critical metric for user experience
    /// - Serverless applications must optimize for cold start
    /// - Consider lazy initialization for faster startup, eager for better first-query latency
    /// </summary>
}
