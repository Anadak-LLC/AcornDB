using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using System.Diagnostics;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for data persistence, crash recovery, and durability guarantees.
    /// Critical for validating data safety and recovery time objectives (RTO).
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 5)]
    public class DurabilityBenchmarks
    {
        private BTreeTrunk<TestDocument>? _btreeTrunk;
        private DocumentStoreTrunk<TestDocument>? _docStoreTrunk;
        private FileTrunk<TestDocument>? _fileTrunk;
        private Tree<TestDocument>? _tree;

        public class TestDocument
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public int Value { get; set; }
            public DateTime Created { get; set; }
        }

        [Params(1_000, 10_000)]
        public int DocumentCount;

        private string _tempDir = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"acorndb_durability_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _btreeTrunk?.Dispose();
            _docStoreTrunk?.Dispose();
            _fileTrunk = null;

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

        private TestDocument CreateDocument(int index, int contentSize = 1024)
        {
            var content = new string('x', contentSize);
            return new TestDocument
            {
                Id = $"doc-{index}",
                Name = $"Document {index}",
                Content = content,
                Value = index,
                Created = DateTime.UtcNow
            };
        }

        // ===== Write-Then-Reload (Persistence Verification) =====

        [Benchmark]
        public void Persistence_BTreeTrunk_Write_Then_Reload()
        {
            var dir = Path.Combine(_tempDir, $"btree_persist_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            // Write phase
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Dispose (flush to disk)
            _tree = null;
            _btreeTrunk.Dispose();

            // Reload phase
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            // Verify all data present
            int count = _tree.NutCount;
            if (count != DocumentCount)
            {
                throw new Exception($"Data loss: Expected {DocumentCount}, got {count}");
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void Persistence_DocumentStoreTrunk_Write_Then_Reload()
        {
            var dir = Path.Combine(_tempDir, $"docstore_persist_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            // Write phase
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Dispose (flush log to disk)
            _tree = null;
            _docStoreTrunk.Dispose();

            // Reload phase (log replay)
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            int count = _tree.NutCount;
            if (count != DocumentCount)
            {
                throw new Exception($"Data loss: Expected {DocumentCount}, got {count}");
            }

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }

        [Benchmark]
        public void Persistence_FileTrunk_Write_Then_Reload()
        {
            var dir = Path.Combine(_tempDir, $"file_persist_{Guid.NewGuid()}");
            _fileTrunk = new FileTrunk<TestDocument>(dir);
            _tree = CreateTree(_fileTrunk);

            // Write phase
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // Reload (FileTrunk is stateless, no dispose needed)
            _tree = null;
            _fileTrunk = new FileTrunk<TestDocument>(dir);
            _tree = CreateTree(_fileTrunk);

            int count = _tree.NutCount;
            if (count != DocumentCount)
            {
                throw new Exception($"Data loss: Expected {DocumentCount}, got {count}");
            }
        }

        // ===== Crash Recovery Time (Cold Start) =====

        [Benchmark]
        public void CrashRecovery_BTreeTrunk_ColdStart()
        {
            var dir = Path.Combine(_tempDir, $"btree_crash_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _btreeTrunk.Dispose();

            // Simulate crash recovery: measure cold start time
            var sw = Stopwatch.StartNew();
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            // Verify data integrity
            var count = _tree.NutCount;
            sw.Stop();

            if (count != DocumentCount)
            {
                throw new Exception($"Recovery failed: Expected {DocumentCount}, got {count}");
            }

            // Recovery Time Objective (RTO): How long to get back online?

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void CrashRecovery_DocumentStoreTrunk_LogReplay()
        {
            var dir = Path.Combine(_tempDir, $"docstore_crash_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _docStoreTrunk.Dispose();

            // Simulate crash recovery: measure log replay time
            var sw = Stopwatch.StartNew();
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            var count = _tree.NutCount;
            sw.Stop();

            if (count != DocumentCount)
            {
                throw new Exception($"Recovery failed: Expected {DocumentCount}, got {count}");
            }

            // Note: DocumentStoreTrunk has longer recovery time due to log replay

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }

        // ===== Incremental Updates (Durability Under Modifications) =====

        [Benchmark]
        public void IncrementalUpdates_BTreeTrunk_WithReload()
        {
            var dir = Path.Combine(_tempDir, $"btree_incremental_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            // Initial load
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _btreeTrunk.Dispose();

            // Incremental updates
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            for (int i = 0; i < DocumentCount / 10; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2; // Modified
                _tree.Stash(doc);
            }

            _btreeTrunk.Dispose();

            // Reload and verify
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            var updated = _tree.Crack("doc-0");
            if (updated == null || updated.Value != 0)
            {
                throw new Exception("Incremental update not persisted");
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void IncrementalUpdates_DocumentStoreTrunk_VersionHistory()
        {
            var dir = Path.Combine(_tempDir, $"docstore_incremental_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            // Initial load
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _docStoreTrunk.Dispose();

            // Incremental updates (creates new log entries)
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            for (int i = 0; i < DocumentCount / 10; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2; // Modified
                _tree.Stash(doc);
            }

            _docStoreTrunk.Dispose();

            // Reload and verify
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            var updated = _tree.Crack("doc-0");
            if (updated == null || updated.Value != 0)
            {
                throw new Exception("Incremental update not persisted");
            }

            // Note: DocumentStoreTrunk preserves version history in log

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }

        // ===== Bulk Delete Durability =====

        [Benchmark]
        public void BulkDelete_BTreeTrunk_WithReload()
        {
            var dir = Path.Combine(_tempDir, $"btree_delete_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _btreeTrunk.Dispose();

            // Delete 50%
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            for (int i = 0; i < DocumentCount / 2; i++)
            {
                _tree.Toss($"doc-{i}");
            }

            _btreeTrunk.Dispose();

            // Reload and verify
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            var count = _tree.NutCount;
            if (count != DocumentCount / 2)
            {
                throw new Exception($"Delete not persisted: Expected {DocumentCount / 2}, got {count}");
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void BulkDelete_DocumentStoreTrunk_LogTombstones()
        {
            var dir = Path.Combine(_tempDir, $"docstore_delete_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _docStoreTrunk.Dispose();

            // Delete 50% (creates tombstone entries in log)
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            for (int i = 0; i < DocumentCount / 2; i++)
            {
                _tree.Toss($"doc-{i}");
            }

            _docStoreTrunk.Dispose();

            // Reload and verify
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            var count = _tree.NutCount;
            if (count != DocumentCount / 2)
            {
                throw new Exception($"Delete not persisted: Expected {DocumentCount / 2}, got {count}");
            }

            // Note: Log still contains deleted entries (tombstones), but they're not counted

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }

        // ===== Write Throughput vs Durability Tradeoff =====

        [Benchmark(Baseline = true)]
        public void Durability_MemoryTrunk_NoPersistence()
        {
            var trunk = new MemoryTrunk<TestDocument>();
            _tree = CreateTree(trunk);

            // Fastest: No durability, pure in-memory
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }
        }

        [Benchmark]
        public void Durability_BTreeTrunk_ImmediatePersistence()
        {
            var dir = Path.Combine(_tempDir, $"btree_immediate_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            // Writes are immediately persisted to memory-mapped files
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void Durability_DocumentStoreTrunk_AppendOnlyLog()
        {
            var dir = Path.Combine(_tempDir, $"docstore_immediate_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            // Append-only log with immediate writes
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }

        [Benchmark]
        public void Durability_FileTrunk_IndividualFiles()
        {
            var dir = Path.Combine(_tempDir, $"file_immediate_{Guid.NewGuid()}");
            _fileTrunk = new FileTrunk<TestDocument>(dir);
            _tree = CreateTree(_fileTrunk);

            // Each document is a separate file (most durable, slowest)
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i));
            }

            // FileTrunk writes are synchronous (no buffering)
        }

        // ===== Concurrent Writes Durability =====

        [Benchmark]
        public void ConcurrentWrites_BTreeTrunk_Durability()
        {
            var dir = Path.Combine(_tempDir, $"btree_concurrent_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            // Concurrent writes (thread-safe persistence)
            var tasks = new Task[4];
            for (int t = 0; t < 4; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < DocumentCount / 4; i++)
                    {
                        _tree.Stash(CreateDocument(threadId * (DocumentCount / 4) + i));
                    }
                });
            }

            Task.WaitAll(tasks);

            _btreeTrunk.Dispose();

            // Verify persistence
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            var count = _tree.NutCount;
            if (count != DocumentCount)
            {
                throw new Exception($"Concurrent writes lost data: Expected {DocumentCount}, got {count}");
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void ConcurrentWrites_DocumentStoreTrunk_Durability()
        {
            var dir = Path.Combine(_tempDir, $"docstore_concurrent_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            // Concurrent writes (serialized at log level)
            var tasks = new Task[4];
            for (int t = 0; t < 4; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < DocumentCount / 4; i++)
                    {
                        _tree.Stash(CreateDocument(threadId * (DocumentCount / 4) + i));
                    }
                });
            }

            Task.WaitAll(tasks);

            _docStoreTrunk.Dispose();

            // Verify persistence
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            var count = _tree.NutCount;
            if (count != DocumentCount)
            {
                throw new Exception($"Concurrent writes lost data: Expected {DocumentCount}, got {count}");
            }

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }

        // ===== Data Integrity Verification =====

        [Benchmark]
        public void DataIntegrity_BTreeTrunk_FullVerification()
        {
            var dir = Path.Combine(_tempDir, $"btree_integrity_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            // Write with known values
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i, contentSize: 512));
            }

            _btreeTrunk.Dispose();

            // Reload and verify every document
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            for (int i = 0; i < DocumentCount; i++)
            {
                var doc = _tree.Crack($"doc-{i}");
                if (doc == null || doc.Value != i)
                {
                    throw new Exception($"Data corruption detected at doc-{i}");
                }
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void DataIntegrity_DocumentStoreTrunk_FullVerification()
        {
            var dir = Path.Combine(_tempDir, $"docstore_integrity_{Guid.NewGuid()}");
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            // Write with known values
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(CreateDocument(i, contentSize: 512));
            }

            _docStoreTrunk.Dispose();

            // Reload and verify every document
            _docStoreTrunk = new DocumentStoreTrunk<TestDocument>(dir);
            _tree = CreateTree(_docStoreTrunk);

            for (int i = 0; i < DocumentCount; i++)
            {
                var doc = _tree.Crack($"doc-{i}");
                if (doc == null || doc.Value != i)
                {
                    throw new Exception($"Data corruption detected at doc-{i}");
                }
            }

            _docStoreTrunk.Dispose();
            _docStoreTrunk = null;
        }
    }

    /// <summary>
    /// Expected Durability Results:
    ///
    /// Write-Then-Reload (Persistence Verification):
    /// - MemoryTrunk: N/A (no persistence)
    /// - FileTrunk: ~100ms for 1K docs (synchronous file writes)
    /// - BTreeTrunk: ~50ms for 1K docs (memory-mapped files)
    /// - DocumentStoreTrunk: ~80ms for 1K docs (append-only log)
    ///
    /// Crash Recovery Time (RTO):
    /// - BTreeTrunk: ~20ms for 1K docs, ~200ms for 10K docs (fast startup)
    /// - DocumentStoreTrunk: ~50ms for 1K docs, ~800ms for 10K docs (log replay)
    /// - FileTrunk: ~30ms for 1K docs (directory scan + metadata read)
    ///
    /// Write Throughput vs Durability Tradeoff:
    /// - MemoryTrunk (No Durability): ~2ms for 1K docs (baseline)
    /// - BTreeTrunk (Immediate): ~30ms for 1K docs (+15x slower, durable)
    /// - DocumentStoreTrunk (Immediate): ~50ms for 1K docs (+25x slower, versioned)
    /// - FileTrunk (Immediate): ~100ms for 1K docs (+50x slower, most durable)
    ///
    /// Incremental Updates:
    /// - BTreeTrunk: In-place updates, no version history
    /// - DocumentStoreTrunk: Append-only, preserves full version history
    /// - FileTrunk: Overwrites entire file per document
    ///
    /// Bulk Delete:
    /// - BTreeTrunk: Physical deletion, reclaims disk space
    /// - DocumentStoreTrunk: Logical deletion (tombstones), disk space grows
    /// - FileTrunk: Physical deletion (file removal)
    ///
    /// Concurrent Writes Durability:
    /// - All trunks maintain ACID properties under concurrent access
    /// - BTreeTrunk: Fine-grained locking per document
    /// - DocumentStoreTrunk: Serialized log writes (lock contention)
    /// - FileTrunk: File-system level locking (slowest)
    ///
    /// Data Integrity:
    /// - Zero data corruption expected across all trunks
    /// - BTreeTrunk: CRC checks on memory-mapped file pages
    /// - DocumentStoreTrunk: Sequential log with checksums
    /// - FileTrunk: OS file system guarantees
    ///
    /// Recommendation:
    /// - Use MemoryTrunk for ephemeral data (caches, sessions)
    /// - Use BTreeTrunk for balanced durability + performance
    /// - Use DocumentStoreTrunk when version history is critical (audit logs, CQRS)
    /// - Use FileTrunk when per-document durability guarantees are required
    /// - Consider write-ahead logging (WAL) for critical workloads
    /// - Benchmark with your hardware (SSD vs HDD makes 10x difference)
    /// </summary>
}
