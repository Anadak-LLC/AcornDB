using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using System.Text;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for real-world usage patterns.
    /// These scenarios help users recognize their own use cases.
    ///
    /// TRUNK SELECTION RATIONALE:
    /// - Session Store: MemoryTrunk (ephemeral, speed critical)
    /// - Metrics Collection: BTreeTrunk (persistent, write-heavy)
    /// - Event Sourcing: DocumentStoreTrunk (versioning/history required)
    /// - Document Editor: DocumentStoreTrunk (versioning required)
    /// - IoT Sensors: BTreeTrunk (persistent, high throughput)
    /// - Mobile Offline Sync: BTreeTrunk (persistent, sync required)
    /// - Analytics: BTreeTrunk (persistent, scan performance)
    /// - Cache Store: MemoryTrunk (ephemeral by design)
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 5)]
    public class RealWorldWorkloadBenchmarks
    {
        private Tree<UserSession>? _sessionTree;
        private Tree<MetricEvent>? _metricsTree;
        private Tree<EventLogEntry>? _eventLogTree;
        private Tree<Document>? _documentTree;
        private Tree<SensorReading>? _sensorTree;
        private Tree<CachedData>? _cacheTree;

        // ===== Data Models =====

        public class UserSession
        {
            public string Id { get; set; } = string.Empty;
            public string UserId { get; set; } = string.Empty;
            public string SessionToken { get; set; } = string.Empty;
            public DateTime Created { get; set; }
            public DateTime LastAccess { get; set; }
            public Dictionary<string, string> Data { get; set; } = new();
        }

        public class MetricEvent
        {
            public string Id { get; set; } = string.Empty;
            public string EventType { get; set; } = string.Empty;
            public double Value { get; set; }
            public DateTime Timestamp { get; set; }
            public Dictionary<string, string> Tags { get; set; } = new();
        }

        public class EventLogEntry
        {
            public string Id { get; set; } = string.Empty;
            public string Action { get; set; } = string.Empty;
            public string Payload { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
            public int Sequence { get; set; }
        }

        public class Document
        {
            public string Id { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public string Author { get; set; } = string.Empty;
            public DateTime Modified { get; set; }
            public int Version { get; set; }
        }

        public class SensorReading
        {
            public string Id { get; set; } = string.Empty;
            public string SensorId { get; set; } = string.Empty;
            public double Temperature { get; set; }
            public double Humidity { get; set; }
            public double Pressure { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class CachedData
        {
            public string Id { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty;
            public byte[] Value { get; set; } = Array.Empty<byte>();
            public DateTime Expiry { get; set; }
        }

        [Params(1_000, 10_000)]
        public int OperationCount;

        private string _tempDir = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"acorndb_workloads_{Guid.NewGuid()}");
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

        private Tree<T> CreateMemoryTree<T>() where T : class
        {
            var tree = new Tree<T>(new MemoryTrunk<T>());
            tree.TtlEnforcementEnabled = false;
            tree.CacheEvictionEnabled = false;
            return tree;
        }

        private Tree<T> CreateBTreeTree<T>(string subdirName) where T : class
        {
            var dir = Path.Combine(_tempDir, subdirName);
            Directory.CreateDirectory(dir);
            var trunk = new BTreeTrunk<T>(dir);
            var tree = new Tree<T>(trunk);
            tree.TtlEnforcementEnabled = false;
            tree.CacheEvictionEnabled = false;
            return tree;
        }

        private Tree<T> CreateDocumentStoreTree<T>(string subdirName) where T : class
        {
            var dir = Path.Combine(_tempDir, subdirName);
            Directory.CreateDirectory(dir);
            var trunk = new DocumentStoreTrunk<T>(dir);
            var tree = new Tree<T>(trunk);
            tree.TtlEnforcementEnabled = false;
            tree.CacheEvictionEnabled = false;
            return tree;
        }

        // ===== Scenario 1: Session Store (Read-Heavy 90/10) =====

        [Benchmark]
        public void SessionStore_ReadHeavy_90Read_10Write()
        {
            // MemoryTrunk: Sessions are ephemeral, speed is critical
            _sessionTree = CreateMemoryTree<UserSession>();

            // Pre-populate sessions
            for (int i = 0; i < 1000; i++)
            {
                _sessionTree.Stash(new UserSession
                {
                    Id = $"session-{i}",
                    UserId = $"user-{i % 100}",
                    SessionToken = Guid.NewGuid().ToString(),
                    Created = DateTime.UtcNow,
                    LastAccess = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        ["ip"] = "192.168.1.1",
                        ["userAgent"] = "Mozilla/5.0"
                    }
                });
            }

            // Simulate session validation workload (90% reads, 10% writes)
            var random = new Random(42);
            for (int i = 0; i < OperationCount; i++)
            {
                if (random.NextDouble() < 0.9)
                {
                    // Read (90%) - Session validation
                    var sessionId = $"session-{random.Next(0, 1000)}";
                    var session = _sessionTree.Crack(sessionId);

                    // Simulate session check
                    if (session != null && session.LastAccess < DateTime.UtcNow.AddMinutes(-30))
                    {
                        // Session expired (would be handled in real scenario)
                    }
                }
                else
                {
                    // Write (10%) - Session update or new session
                    _sessionTree.Stash(new UserSession
                    {
                        Id = $"session-{random.Next(0, 1000)}",
                        UserId = $"user-{random.Next(0, 100)}",
                        SessionToken = Guid.NewGuid().ToString(),
                        Created = DateTime.UtcNow,
                        LastAccess = DateTime.UtcNow
                    });
                }
            }
        }

        // ===== Scenario 2: Metrics Collection (Write-Heavy 80/20) =====

        [Benchmark]
        public void MetricsCollection_WriteHeavy_80Write_20Read()
        {
            // BTreeTrunk: Metrics need persistence, write-heavy workload
            _metricsTree = CreateBTreeTree<MetricEvent>($"metrics_{Guid.NewGuid():N}");

            // Simulate high-volume metrics ingestion
            var random = new Random(42);
            for (int i = 0; i < OperationCount; i++)
            {
                if (random.NextDouble() < 0.8)
                {
                    // Write (80%) - New metric event
                    _metricsTree.Stash(new MetricEvent
                    {
                        Id = Guid.NewGuid().ToString(),
                        EventType = random.Next(0, 10) switch
                        {
                            0 => "http_request",
                            1 => "database_query",
                            2 => "cache_hit",
                            3 => "cache_miss",
                            4 => "error",
                            _ => "custom_event"
                        },
                        Value = random.NextDouble() * 1000,
                        Timestamp = DateTime.UtcNow,
                        Tags = new Dictionary<string, string>
                        {
                            ["service"] = $"service-{random.Next(0, 5)}",
                            ["environment"] = "production"
                        }
                    });
                }
                else
                {
                    // Read (20%) - Query recent metrics (aggregation simulation)
                    var recentMetrics = _metricsTree.Nuts
                        .Where(m => m.Timestamp > DateTime.UtcNow.AddMinutes(-5))
                        .Take(100)
                        .ToList();
                }
            }
        }

        // ===== Scenario 3: Event Sourcing (Append-Only Sequential) =====

        [Benchmark]
        public void EventSourcing_AppendOnly_Sequential()
        {
            var dir = Path.Combine(_tempDir, $"eventsourcing_{Guid.NewGuid()}");
            var docStoreTrunk = new DocumentStoreTrunk<EventLogEntry>(dir);
            _eventLogTree = new Tree<EventLogEntry>(docStoreTrunk);
            _eventLogTree.TtlEnforcementEnabled = false;

            // Simulate event sourcing pattern (strict sequential append)
            for (int i = 0; i < OperationCount; i++)
            {
                _eventLogTree.Stash(new EventLogEntry
                {
                    Id = $"event-{i}",
                    Action = (i % 5) switch
                    {
                        0 => "UserCreated",
                        1 => "OrderPlaced",
                        2 => "PaymentProcessed",
                        3 => "ItemShipped",
                        _ => "OrderCompleted"
                    },
                    Payload = $"{{\"orderId\": \"{Guid.NewGuid()}\", \"amount\": {100 + i}}}",
                    Timestamp = DateTime.UtcNow,
                    Sequence = i
                });
            }

            // Event replay (read all in sequence)
            var allEvents = _eventLogTree.Nuts.OrderBy(e => e.Sequence).ToList();

            docStoreTrunk.Dispose();
        }

        // ===== Scenario 4: Document Editor (Frequent Updates Same Doc) =====

        [Benchmark]
        public void DocumentEditor_FrequentUpdates_SameDoc()
        {
            var dir = Path.Combine(_tempDir, $"doceditor_{Guid.NewGuid()}");
            var docStoreTrunk = new DocumentStoreTrunk<Document>(dir);
            _documentTree = new Tree<Document>(docStoreTrunk);
            _documentTree.TtlEnforcementEnabled = false;

            // Simulate collaborative document editing
            var documentId = "shared-doc-1";
            var baseContent = GenerateLongText(5000);

            // Initial document
            _documentTree.Stash(new Document
            {
                Id = documentId,
                Title = "Shared Document",
                Content = baseContent,
                Author = "user1",
                Modified = DateTime.UtcNow,
                Version = 1
            });

            // Frequent updates (autosave every few seconds)
            for (int i = 0; i < Math.Min(100, OperationCount); i++)
            {
                var doc = _documentTree.Crack(documentId);
                if (doc != null)
                {
                    _documentTree.Stash(new Document
                    {
                        Id = documentId,
                        Title = doc.Title,
                        Content = doc.Content + $"\nEdit {i} at {DateTime.UtcNow}",
                        Author = doc.Author,
                        Modified = DateTime.UtcNow,
                        Version = doc.Version + 1
                    });
                }
            }

            // Check history (versioning feature)
            var history = _documentTree.GetHistory(documentId);

            docStoreTrunk.Dispose();
        }

        // ===== Scenario 5: IoT Sensor Data (High Volume Small Docs) =====

        [Benchmark]
        public void IoTSensor_HighVolume_SmallDocs()
        {
            // BTreeTrunk: IoT data needs persistence, high write throughput
            _sensorTree = CreateBTreeTree<SensorReading>($"iot_{Guid.NewGuid():N}");

            // Simulate 10 sensors sending data every second
            var random = new Random(42);
            for (int i = 0; i < OperationCount; i++)
            {
                _sensorTree.Stash(new SensorReading
                {
                    Id = Guid.NewGuid().ToString(),
                    SensorId = $"sensor-{i % 10}",
                    Temperature = 20 + random.NextDouble() * 15,
                    Humidity = 40 + random.NextDouble() * 30,
                    Pressure = 1000 + random.NextDouble() * 50,
                    Timestamp = DateTime.UtcNow
                });
            }

            // Query recent readings from specific sensor
            var sensor5Readings = _sensorTree.Nuts
                .Where(r => r.SensorId == "sensor-5")
                .OrderByDescending(r => r.Timestamp)
                .Take(100)
                .ToList();
        }

        // ===== Scenario 6: Mobile App Offline Sync (Bulk Load) =====

        [Benchmark]
        public void MobileApp_OfflineSync_BulkLoad()
        {
            // BTreeTrunk: Mobile apps need persistent storage for offline-first
            var syncTree = CreateBTreeTree<CachedData>($"mobile_{Guid.NewGuid():N}");

            // Simulate app coming online and syncing cached changes
            var bulkData = new List<CachedData>();

            // Generate bulk data while "offline"
            for (int i = 0; i < OperationCount; i++)
            {
                bulkData.Add(new CachedData
                {
                    Id = $"cache-{i}",
                    Key = $"key-{i}",
                    Value = GenerateRandomBytes(512),
                    Expiry = DateTime.UtcNow.AddHours(1)
                });
            }

            // Bulk sync when online (batch write)
            foreach (var item in bulkData)
            {
                syncTree.Stash(item);
            }

            // Verify all synced
            if (syncTree.NutCount != OperationCount)
            {
                throw new Exception($"Sync incomplete: {syncTree.NutCount}/{OperationCount}");
            }
        }

        // ===== Scenario 7: Analytics Full Scan + Aggregation =====

        [Benchmark]
        public void Analytics_FullScan_Aggregation()
        {
            // BTreeTrunk: Analytics data needs persistence for querying
            _metricsTree = CreateBTreeTree<MetricEvent>($"analytics_{Guid.NewGuid():N}");

            // Pre-populate with analytics data
            var random = new Random(42);
            for (int i = 0; i < OperationCount; i++)
            {
                _metricsTree.Stash(new MetricEvent
                {
                    Id = $"metric-{i}",
                    EventType = i % 5 == 0 ? "error" : "info",
                    Value = random.NextDouble() * 100,
                    Timestamp = DateTime.UtcNow.AddMinutes(-random.Next(0, 1440)), // Last 24 hours
                    Tags = new Dictionary<string, string>
                    {
                        ["service"] = $"service-{random.Next(0, 5)}"
                    }
                });
            }

            // Analytical queries
            var errorCount = _metricsTree.Nuts.Count(m => m.EventType == "error");
            var avgValue = _metricsTree.Nuts.Average(m => m.Value);
            var last24h = _metricsTree.Nuts
                .Where(m => m.Timestamp > DateTime.UtcNow.AddHours(-24))
                .GroupBy(m => m.Tags.GetValueOrDefault("service", "unknown"))
                .Select(g => new { Service = g.Key, Count = g.Count() })
                .ToList();
        }

        // ===== Scenario 8: Cache Store (High Read, TTL Expiry) =====

        [Benchmark]
        public void CacheStore_HighRead_WithTTL()
        {
            // MemoryTrunk: Caches are ephemeral by design, speed is critical
            _cacheTree = CreateMemoryTree<CachedData>();
            _cacheTree.TtlEnforcementEnabled = true; // Enable TTL for this scenario

            // Pre-populate cache
            var random = new Random(42);
            for (int i = 0; i < 1000; i++)
            {
                _cacheTree.Stash(new CachedData
                {
                    Id = $"cache-{i}",
                    Key = $"key-{i}",
                    Value = GenerateRandomBytes(1024),
                    Expiry = DateTime.UtcNow.AddMinutes(random.Next(1, 60))
                });
            }

            // High read workload with occasional writes
            for (int i = 0; i < OperationCount; i++)
            {
                if (random.NextDouble() < 0.95)
                {
                    // Read (95%)
                    var key = $"cache-{random.Next(0, 1000)}";
                    var cached = _cacheTree.Crack(key);
                }
                else
                {
                    // Write (5%) - Cache refresh
                    _cacheTree.Stash(new CachedData
                    {
                        Id = $"cache-{random.Next(0, 1000)}",
                        Key = $"key-{random.Next(0, 1000)}",
                        Value = GenerateRandomBytes(1024),
                        Expiry = DateTime.UtcNow.AddMinutes(30)
                    });
                }
            }
        }

        // ===== Helper Methods =====

        private static string GenerateLongText(int length)
        {
            var sb = new StringBuilder();
            var random = new Random(42);
            var words = new[] { "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog" };

            while (sb.Length < length)
            {
                sb.Append(words[random.Next(words.Length)]);
                sb.Append(' ');
            }

            return sb.ToString().Substring(0, length);
        }

        private static byte[] GenerateRandomBytes(int size)
        {
            var bytes = new byte[size];
            var random = new Random(42);
            random.NextBytes(bytes);
            return bytes;
        }
    }

    /// <summary>
    /// Real-World Scenario Performance Expectations:
    ///
    /// Session Store (90/10 Read/Write):
    /// - 10K ops: ~15-30ms
    /// - Ideal for: Web session management, JWT validation
    /// - Key metric: Read latency < 0.1ms (cache hit)
    ///
    /// Metrics Collection (80/20 Write/Read):
    /// - 10K writes: ~40-80ms
    /// - Ideal for: Application telemetry, logging
    /// - Key metric: Write throughput > 100K events/sec
    ///
    /// Event Sourcing (Append-Only):
    /// - 10K sequential writes: ~50-100ms
    /// - Ideal for: Audit logs, CQRS/ES patterns
    /// - Key metric: Sequential write throughput, durability
    ///
    /// Document Editor (Frequent Updates):
    /// - 100 updates: ~10-20ms
    /// - Ideal for: Collaborative editing, autosave
    /// - Key metric: Version history retrieval speed
    ///
    /// IoT Sensors (High Volume):
    /// - 10K small writes: ~20-40ms
    /// - Ideal for: Edge computing, sensor data ingestion
    /// - Key metric: Throughput for small documents
    ///
    /// Mobile Sync (Bulk Load):
    /// - 10K bulk writes: ~60-120ms
    /// - Ideal for: Offline-first mobile apps
    /// - Key metric: Batch write efficiency
    ///
    /// Analytics (Full Scan):
    /// - 10K scan + aggregate: ~100-200ms
    /// - Ideal for: Dashboard queries, reporting
    /// - Key metric: LINQ query performance
    ///
    /// Cache Store (High Read + TTL):
    /// - 10K mixed ops: ~15-30ms
    /// - Ideal for: Distributed cache, CDN origin
    /// - Key metric: Read hit rate, TTL cleanup efficiency
    /// </summary>
}
