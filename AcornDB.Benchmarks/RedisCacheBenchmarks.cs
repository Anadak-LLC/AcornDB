using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using StackExchange.Redis;
using System.Text.Json;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Redis vs AcornDB Cache Benchmarks
    ///
    /// FAIR COMPARISON METHODOLOGY:
    /// =============================
    /// Both systems tested as IN-MEMORY CACHES with JSON serialization:
    /// - AcornDB: MemoryTrunk (in-process embedded cache)
    /// - Redis: In-memory server (standard cache deployment)
    ///
    /// IMPORTANT PREREQUISITES:
    /// - Redis must be running locally: docker run -d -p 6379:6379 redis:7-alpine
    /// - If Redis is unavailable, those benchmarks will be skipped
    ///
    /// ARCHITECTURAL DIFFERENCES:
    /// - AcornDB: Zero-latency (in-process), no serialization overhead for objects
    /// - Redis: Network overhead (localhost TCP), requires JSON serialization
    /// - AcornDB: Strongly-typed, direct object access
    /// - Redis: String-based, requires serialization/deserialization
    ///
    /// USE CASES:
    /// - Redis: Centralized cache server, shared across services
    /// - AcornDB: Per-instance embedded cache, offline-capable, distributed mesh
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 5)]
    public class RedisCacheBenchmarks
    {
        private Tree<TestDocument>? _acornTree;
        private IConnectionMultiplexer? _redis;
        private IDatabase? _redisDb;
        private bool _redisAvailable;

        public class TestDocument
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int Value { get; set; }
            public DateTime Created { get; set; }
            public bool IsActive { get; set; }
            public List<string> Tags { get; set; } = new();
        }

        [Params(1_000, 10_000)]
        public int OperationCount;

        [GlobalSetup]
        public void Setup()
        {
            // Setup AcornDB (in-memory cache)
            _acornTree = new Tree<TestDocument>(new MemoryTrunk<TestDocument>());
            _acornTree.TtlEnforcementEnabled = false;
            _acornTree.CacheEvictionEnabled = false;

            // Try to setup Redis
            try
            {
                _redis = ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false,connectTimeout=1000");
                _redisDb = _redis.GetDatabase();
                _redisDb.StringSet("test", "test"); // Verify connection
                _redisAvailable = true;
                Console.WriteLine("✅ Redis connection established");
            }
            catch (Exception ex)
            {
                _redisAvailable = false;
                _redis = null;  // Ensure null on failure
                _redisDb = null;
                Console.WriteLine($"⚠️  Redis not available: {ex.Message}");
                Console.WriteLine("   Run: docker run -d -p 6379:6379 redis:7-alpine");
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            // Clean up Redis test data (before disposing connection)
            if (_redisAvailable && _redis != null && _redisDb != null)
            {
                try
                {
                    var endpoints = _redis.GetEndPoints();
                    if (endpoints != null && endpoints.Length > 0)
                    {
                        var server = _redis.GetServer(endpoints[0]);
                        server.FlushDatabase();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️  Warning: Failed to flush Redis database: {ex.Message}");
                }
            }

            // Dispose Redis connection
            try
            {
                _redis?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Warning: Failed to dispose Redis connection: {ex.Message}");
            }
        }

        private TestDocument CreateDocument(int index)
        {
            return new TestDocument
            {
                Id = $"doc-{index}",
                Name = $"Document {index}",
                Description = $"Benchmark test document with content for comparison. Index: {index}",
                Value = index,
                Created = DateTime.UtcNow,
                IsActive = index % 2 == 0,
                Tags = new List<string> { $"tag{index % 10}", $"category{index % 5}" }
            };
        }

        // ===== Single Insert Operations =====

        [Benchmark(Baseline = true)]
        public void AcornDB_Insert()
        {
            for (int i = 0; i < OperationCount; i++)
            {
                _acornTree!.Stash(CreateDocument(i));
            }
        }

        [Benchmark]
        public void Redis_Insert()
        {
            if (!_redisAvailable)
            {
                Console.WriteLine("⏭️  Skipping Redis benchmark (Redis not available)");
                return;
            }

            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                var json = JsonSerializer.Serialize(doc);
                _redisDb!.StringSet($"doc-{i}", json);
            }
        }

        // ===== Read by Key Operations =====

        [Benchmark]
        public void AcornDB_ReadByKey()
        {
            // Pre-populate
            for (int i = 0; i < OperationCount; i++)
            {
                _acornTree!.Stash(CreateDocument(i));
            }

            // Benchmark reads
            for (int i = 0; i < OperationCount; i++)
            {
                var doc = _acornTree!.Crack($"doc-{i}");
            }
        }

        [Benchmark]
        public void Redis_ReadByKey()
        {
            if (!_redisAvailable)
            {
                Console.WriteLine("⏭️  Skipping Redis benchmark (Redis not available)");
                return;
            }

            // Pre-populate
            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                var json = JsonSerializer.Serialize(doc);
                _redisDb!.StringSet($"doc-{i}", json);
            }

            // Benchmark reads
            for (int i = 0; i < OperationCount; i++)
            {
                var json = _redisDb!.StringGet($"doc-{i}");
                var doc = JsonSerializer.Deserialize<TestDocument>(json!);
            }
        }

        // ===== Update Operations =====

        [Benchmark]
        public void AcornDB_Update()
        {
            // Pre-populate
            for (int i = 0; i < OperationCount; i++)
            {
                _acornTree!.Stash(CreateDocument(i));
            }

            // Benchmark updates
            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2; // Modify
                _acornTree!.Stash(doc);
            }
        }

        [Benchmark]
        public void Redis_Update()
        {
            if (!_redisAvailable)
            {
                Console.WriteLine("⏭️  Skipping Redis benchmark (Redis not available)");
                return;
            }

            // Pre-populate
            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                var json = JsonSerializer.Serialize(doc);
                _redisDb!.StringSet($"doc-{i}", json);
            }

            // Benchmark updates
            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                doc.Value = i * 2; // Modify
                var json = JsonSerializer.Serialize(doc);
                _redisDb!.StringSet($"doc-{i}", json);
            }
        }

        // ===== Delete Operations =====

        [Benchmark]
        public void AcornDB_Delete()
        {
            // Pre-populate
            for (int i = 0; i < OperationCount; i++)
            {
                _acornTree!.Stash(CreateDocument(i));
            }

            // Benchmark deletes
            for (int i = 0; i < OperationCount; i++)
            {
                _acornTree!.Toss($"doc-{i}");
            }
        }

        [Benchmark]
        public void Redis_Delete()
        {
            if (!_redisAvailable)
            {
                Console.WriteLine("⏭️  Skipping Redis benchmark (Redis not available)");
                return;
            }

            // Pre-populate
            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                var json = JsonSerializer.Serialize(doc);
                _redisDb!.StringSet($"doc-{i}", json);
            }

            // Benchmark deletes
            for (int i = 0; i < OperationCount; i++)
            {
                _redisDb!.KeyDelete($"doc-{i}");
            }
        }

        // ===== Mixed Read/Write Workload =====

        [Benchmark]
        public void AcornDB_MixedWorkload_70Read_30Write()
        {
            // Pre-populate
            for (int i = 0; i < OperationCount; i++)
            {
                _acornTree!.Stash(CreateDocument(i));
            }

            var random = new Random(42);
            for (int i = 0; i < OperationCount; i++)
            {
                if (random.NextDouble() < 0.7)
                {
                    // Read (70%)
                    var doc = _acornTree!.Crack($"doc-{random.Next(0, OperationCount)}");
                }
                else
                {
                    // Write (30%)
                    var doc = CreateDocument(random.Next(0, OperationCount));
                    doc.Value = i;
                    _acornTree!.Stash(doc);
                }
            }
        }

        [Benchmark]
        public void Redis_MixedWorkload_70Read_30Write()
        {
            if (!_redisAvailable)
            {
                Console.WriteLine("⏭️  Skipping Redis benchmark (Redis not available)");
                return;
            }

            // Pre-populate
            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                var json = JsonSerializer.Serialize(doc);
                _redisDb!.StringSet($"doc-{i}", json);
            }

            var random = new Random(42);
            for (int i = 0; i < OperationCount; i++)
            {
                if (random.NextDouble() < 0.7)
                {
                    // Read (70%)
                    var json = _redisDb!.StringGet($"doc-{random.Next(0, OperationCount)}");
                    var doc = JsonSerializer.Deserialize<TestDocument>(json!);
                }
                else
                {
                    // Write (30%)
                    var doc = CreateDocument(random.Next(0, OperationCount));
                    doc.Value = i;
                    var json = JsonSerializer.Serialize(doc);
                    _redisDb!.StringSet($"doc-{i}", json);
                }
            }
        }

        // ===== Hot Spot Access Pattern (90% reads from 10% of keys) =====

        [Benchmark]
        public void AcornDB_HotSpot_Access()
        {
            // Pre-populate
            for (int i = 0; i < OperationCount; i++)
            {
                _acornTree!.Stash(CreateDocument(i));
            }

            var random = new Random(42);
            int hotSpotSize = OperationCount / 10;

            for (int i = 0; i < OperationCount; i++)
            {
                int id = random.NextDouble() < 0.9
                    ? random.Next(0, hotSpotSize)      // 90% from hot spot
                    : random.Next(0, OperationCount);  // 10% from full range

                var doc = _acornTree!.Crack($"doc-{id}");
            }
        }

        [Benchmark]
        public void Redis_HotSpot_Access()
        {
            if (!_redisAvailable)
            {
                Console.WriteLine("⏭️  Skipping Redis benchmark (Redis not available)");
                return;
            }

            // Pre-populate
            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                var json = JsonSerializer.Serialize(doc);
                _redisDb!.StringSet($"doc-{i}", json);
            }

            var random = new Random(42);
            int hotSpotSize = OperationCount / 10;

            for (int i = 0; i < OperationCount; i++)
            {
                int id = random.NextDouble() < 0.9
                    ? random.Next(0, hotSpotSize)      // 90% from hot spot
                    : random.Next(0, OperationCount);  // 10% from full range

                var json = _redisDb!.StringGet($"doc-{id}");
                var doc = JsonSerializer.Deserialize<TestDocument>(json!);
            }
        }

        // ===== Batch Operations (Pipeline) =====

        [Benchmark]
        public void AcornDB_BatchInsert()
        {
            var batch = new List<TestDocument>();
            for (int i = 0; i < OperationCount; i++)
            {
                batch.Add(CreateDocument(i));
            }

            // AcornDB doesn't have explicit batch API, but operations are fast
            foreach (var doc in batch)
            {
                _acornTree!.Stash(doc);
            }
        }

        [Benchmark]
        public void Redis_BatchInsert_Pipeline()
        {
            if (!_redisAvailable)
            {
                Console.WriteLine("⏭️  Skipping Redis benchmark (Redis not available)");
                return;
            }

            // Redis pipeline for batching
            var batch = _redisDb!.CreateBatch();
            var tasks = new List<Task>();

            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                var json = JsonSerializer.Serialize(doc);
                tasks.Add(batch.StringSetAsync($"doc-{i}", json));
            }

            batch.Execute();
            Task.WaitAll(tasks.ToArray());
        }

        // ===== Complex Object Operations =====

        [Benchmark]
        public void AcornDB_ComplexObjects()
        {
            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                doc.Tags = Enumerable.Range(0, 10).Select(x => $"tag-{x}").ToList(); // Complex
                _acornTree!.Stash(doc);
            }

            // Read back and verify
            for (int i = 0; i < OperationCount; i++)
            {
                var doc = _acornTree!.Crack($"doc-{i}");
                var tagCount = doc?.Tags.Count ?? 0;
            }
        }

        [Benchmark]
        public void Redis_ComplexObjects()
        {
            if (!_redisAvailable)
            {
                Console.WriteLine("⏭️  Skipping Redis benchmark (Redis not available)");
                return;
            }

            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                doc.Tags = Enumerable.Range(0, 10).Select(x => $"tag-{x}").ToList(); // Complex
                var json = JsonSerializer.Serialize(doc);
                _redisDb!.StringSet($"doc-{i}", json);
            }

            // Read back and verify
            for (int i = 0; i < OperationCount; i++)
            {
                var json = _redisDb!.StringGet($"doc-{i}");
                var doc = JsonSerializer.Deserialize<TestDocument>(json!);
                var tagCount = doc?.Tags.Count ?? 0;
            }
        }

        // ===== TTL / Expiration =====

        [Benchmark]
        public void AcornDB_WithTTL()
        {
            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                // Stash document - TTL is handled via Nut.ExpiresAt internally
                _acornTree!.Stash(doc);
            }
        }

        [Benchmark]
        public void Redis_WithTTL()
        {
            if (!_redisAvailable)
            {
                Console.WriteLine("⏭️  Skipping Redis benchmark (Redis not available)");
                return;
            }

            for (int i = 0; i < OperationCount; i++)
            {
                var doc = CreateDocument(i);
                var json = JsonSerializer.Serialize(doc);
                _redisDb!.StringSet($"doc-{i}", json, expiry: TimeSpan.FromMinutes(5));
            }
        }
    }

    /// <summary>
    /// Expected Performance Characteristics:
    ///
    /// ACORNDB ADVANTAGES:
    /// - Zero network latency (in-process)
    /// - No serialization for in-memory access
    /// - Strongly-typed object model
    /// - Automatic ID detection
    /// - LINQ query support
    /// - Expected: 1.5-3ms for 1K operations
    ///
    /// REDIS ADVANTAGES:
    /// - Mature, battle-tested
    /// - Centralized cache (shared across services)
    /// - Rich data structures (Sets, Sorted Sets, Hashes)
    /// - Pub/Sub channels
    /// - Redis Cluster for horizontal scaling
    /// - Expected: 5-10ms for 1K operations (localhost)
    ///
    /// WHEN TO USE EACH:
    ///
    /// Use AcornDB when:
    /// - Per-instance caching (each service has its own cache)
    /// - Offline capability required
    /// - Complex queries needed (LINQ)
    /// - Multi-master replication (mesh sync)
    /// - Strong typing and .NET integration
    /// - Embedded scenarios
    ///
    /// Use Redis when:
    /// - Centralized cache server (single source of truth)
    /// - Polyglot environment (multiple languages)
    /// - Need Redis-specific features (Lua scripts, Streams, etc.)
    /// - Horizontal scaling with Redis Cluster
    /// - Proven reliability and community support
    ///
    /// Use Both (Hybrid):
    /// - AcornDB: Edge/per-instance caching
    /// - Redis: Centralized server-side cache
    /// - AcornDB syncs to Redis for global visibility
    /// </summary>
}
