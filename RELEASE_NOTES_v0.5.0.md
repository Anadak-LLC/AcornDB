# AcornDB v0.5.0 Release Notes

**Release Date**: October 2025

---

## Release Focus: Production Resilience & Enterprise Storage

Version 0.5.0 transforms AcornDB from a developer-friendly local database into a **production-ready, enterprise-capable data platform** with advanced resilience patterns, cloud-native storage backends, and industrial-strength reliability features.

---

## Major Features

### 1. **IRoot Pipeline Architecture** (BREAKING CHANGE)

**Revolutionary new extension point for trunk-level transformations**

The Root pipeline provides a **composable, ordered processing chain** for byte-level transformations during persistence operations. Think "middleware for your database."

#### What is a Root?

Roots are byte-level processors that intercept data as it flows between the Tree cache and Trunk storage:

```csharp
public interface IRoot
{
    string Name { get; }
    int Sequence { get; }
    byte[] OnStash(byte[] data, RootProcessingContext context);
    byte[] OnCrack(byte[] data, RootProcessingContext context);
}
```

#### Built-In Roots

- **CompressionRoot** - Gzip/Brotli/LZ4 compression
- **EncryptionRoot** - AES-256 encryption with key rotation
- **PolicyEnforcementRoot** - Runtime policy validation

#### Usage Example

```csharp
var tree = new Acorn<User>()
    .WithTrunk(trunk)
    .Sprout();

// Add roots in sequence order
tree.Trunk.AddRoot(new EncryptionRoot("my-secret-key", sequence: 10));
tree.Trunk.AddRoot(new CompressionRoot(CompressionMethod.Gzip, sequence: 20));

// Data flow: Tree → Encryption → Compression → Storage
//            Storage → Decompression → Decryption → Tree
```

#### Why Roots?

**Before (v0.4)**: Decorator pattern with manual wrapping
```csharp
var trunk = new CompressedTrunk<User>(
    new EncryptedTrunk<User>(
        new FileTrunk<User>()
    )
);
```

**After (v0.5)**: Declarative pipeline
```csharp
var trunk = new FileTrunk<User>();
trunk.AddRoot(new EncryptionRoot(...));
trunk.AddRoot(new CompressionRoot(...));
```

**Benefits**:
- ✅ **Runtime composition** - Add/remove roots dynamically
- ✅ **Ordered execution** - Sequence property controls processing order
- ✅ **Trunk-agnostic** - Works with ANY trunk implementation
- ✅ **Zero allocations** - Byte array pooling for performance

---

### 2. **Resilient Storage Patterns**

#### ResilientTrunk - Fault-Tolerant Operations

Enterprise-grade retry logic with exponential backoff and circuit breaker:

```csharp
var resilient = new ResilientTrunk<User>(
    innerTrunk,
    new ResilienceOptions
    {
        MaxRetries = 3,
        InitialDelay = TimeSpan.FromMilliseconds(100),
        BackoffMultiplier = 2.0,
        CircuitBreakerThreshold = 5,
        CircuitBreakerResetAfter = TimeSpan.FromMinutes(1),
        OnRetry = (attempt, ex) => Log.Warn($"Retry {attempt}: {ex.Message}")
    }
);
```

**Features**:
- Exponential backoff with jitter
- Circuit breaker pattern (prevents cascading failures)
- Configurable retry policies
- Metrics and telemetry hooks
- Graceful degradation

#### NearFarTrunk - Hybrid Storage

Multi-tier storage with automatic promotion/demotion:

```csharp
var nearFar = new NearFarTrunk<User>(
    near: new MemoryTrunk<User>(),      // Fast tier
    far: new SqliteTrunk<User>("db.db"), // Durable tier
    new NearFarOptions
    {
        WriteMode = WriteMode.NearThenFar,     // Write to both
        ReadMode = ReadMode.NearWithFarFallback, // Read from near, fallback to far
        PromoteOnRead = true                    // Warm near cache on far reads
    }
);
```

**Use Cases**:
- **Read-heavy workloads**: Memory cache + SQL backing
- **Write buffering**: Memory + async flush to cloud
- **Cost optimization**: Hot data in-memory, cold data in S3

#### CachedTrunk - Intelligent Caching

LRU cache with TTL and size limits:

```csharp
var cached = new CachedTrunk<User>(
    backingStore: new SqliteTrunk<User>("users.db"),
    cache: new MemoryTrunk<User>(),
    new CacheOptions
    {
        MaxCacheSize = 10_000,
        DefaultTtl = TimeSpan.FromMinutes(5),
        WarmCacheOnLoadAll = true,
        EvictionStrategy = EvictionStrategy.LRU
    }
);
```

---

### 3. **Enterprise Storage Backends**

#### New Trunk Implementations

**Cloud Storage (AcornDB.Persistence.Cloud)**:
- ✅ **AzureTrunk** - Azure Blob Storage with container-per-type
- ✅ **AzureTableTrunk** - Azure Table Storage (NoSQL)
- ✅ **DynamoDbTrunk** - AWS DynamoDB with partition key optimization
- ✅ **CloudTrunk** - Generic S3-compatible storage (AWS, MinIO, Wasabi)

**RDBMS (AcornDB.Persistence.RDBMS)** - Now with native indexing:
- ✅ **SqliteTrunk** - SQLite with JSON extraction indexes
- ✅ **MySqlTrunk** - MySQL with write batching
- ✅ **PostgreSqlTrunk** - PostgreSQL with JSONB support
- ✅ **SqlServerTrunk** - SQL Server with MERGE optimization

**Data Lake (AcornDB.Persistence.DataLake)** - NEW:
- ✅ **ParquetTrunk** - Apache Parquet columnar storage
- ✅ **TieredTrunk** - Hot/cold data tiering

#### BTreeTrunk - High-Performance Indexed Storage

Memory-mapped B+Tree implementation with O(log n) lookups:

```csharp
var btree = new BTreeTrunk<User>("./data", new BTreeOptions
{
    Order = 128,              // B+Tree order (fan-out)
    EnableWal = true,         // Write-ahead logging
    CacheSize = 1000,         // Page cache size
    AutoCompact = true        // Background compaction
});
```

**Performance**: 100K ops/sec (vs 10K ops/sec for FileTrunk)

---

### 4. **Policy Enforcement System**

Runtime data governance with the `PolicyEnforcementRoot`:

```csharp
var policyEngine = new LocalPolicyEngine();

// TTL enforcement
policyEngine.RegisterRule(new TtlPolicyRule());

// Tag-based access control
policyEngine.RegisterRule(new TagAccessPolicyRule(
    requiredTags: new[] { "approved", "compliant" }
));

trunk.AddRoot(new PolicyEnforcementRoot(policyEngine, sequence: 5));
```

**Capabilities**:
- **TTL enforcement** - Reject stale data
- **Tag-based access** - Require tags on documents
- **Custom rules** - Implement `IPolicyRule` interface
- **Audit logging** - Track policy violations

---

### 5. **Enhanced Branch & Sync**

#### Lifecycle Management

Full control over sync lifecycle:

```csharp
var branch = tree.Branch(remoteTree);

// Lifecycle states: Created → Connected → Syncing → Idle → Disconnected
branch.OnStateChanged += (state) => Console.WriteLine($"State: {state}");

branch.Connect();    // Establish connection
branch.StartSync();  // Begin automatic sync
branch.Pause();      // Pause sync (retain connection)
branch.Resume();     // Resume sync
branch.Disconnect(); // Clean shutdown
```

#### Sync Modes

Fine-grained sync direction control:

```csharp
branch.SetSyncMode(SyncMode.PushOnly);       // One-way to remote
branch.SetSyncMode(SyncMode.PullOnly);       // One-way from remote
branch.SetSyncMode(SyncMode.Bidirectional);  // Two-way (default)
```

#### Delete Propagation

Deletes now sync across branches (was missing in v0.4):

```csharp
tree.Toss("user-123");  // Automatically pushes delete to all branches
```

#### Enhanced Branch Types

- **AuditBranch** - Logs all sync operations
- **MetricsBranch** - Collects performance metrics
- **InProcessBranch** - Zero-copy in-process sync (50x faster)

---

### 6. **Metrics & Observability**

#### MetricsCollector - Production Telemetry

```csharp
var metrics = new MetricsCollector();
metrics.StartCollecting(tree, intervalMs: 1000);

// Expose Prometheus endpoint
var server = new MetricsServer(metrics, port: 9090);
server.Start();
```

**Metrics Collected**:
- Stash/Crack/Toss rates (ops/sec)
- Cache hit/miss ratios
- Sync throughput (nuts/sec)
- Conflict resolution stats
- Storage size trends

#### Real-Time Dashboard

Enhanced Canopy dashboard with live metrics, interactive graph, and grove visualization.

---

### 7. **API Consistency: Tree Semantics Everywhere**

**BREAKING CHANGE**: Renamed ITrunk methods for consistency with Tree API:

| Old Method | New Method | Backward Compatible |
|-----------|-----------|---------------------|
| `Save()`    | `Stash()`   | ✅ (obsolete wrapper) |
| `Load()`    | `Crack()`   | ✅ (obsolete wrapper) |
| `Delete()`  | `Toss()`    | ✅ (obsolete wrapper) |
| `LoadAll()` | `CrackAll()`| ✅ (obsolete wrapper) |

**Why?**: Consistent "tree semantics" across all layers (Tree, ITrunk, IRoot).

**Migration**: Old methods still work but show compiler warnings. Update at your convenience.

---

### 8. **Comprehensive Sample Applications**

**7 production-ready sample apps** in `AcornDB.SampleApps`:

1. **TodoListApp.cs** - Offline-first todo app with sync
2. **BlogApp.cs** - Multi-author blogging platform
3. **CollaborativeNotesApp.cs** - Real-time collaborative editor
4. **ECommerceApp.cs** - Product catalog with inventory sync
5. **MetricsMonitoringApp.cs** - IoT sensor data collector
6. **ResilientCacheApp.cs** - Distributed cache with failover
7. **RootPipelineDemo.cs** - Encryption + compression demo

Each demonstrates:
- Real-world use case
- Best practices
- Performance optimization
- Error handling

---

### 9. **Benchmark Suite Expansion**

**13 comprehensive benchmark suites** (up from 4):

- BasicOperationsBenchmarks
- CompetitiveBenchmarks (vs LiteDB, SQLite)
- CacheEffectivenessBenchmarks
- ConcurrencyBenchmarks
- DeltaSyncBenchmarks
- DurabilityBenchmarks
- LifecycleBenchmarks
- QueryPerformanceBenchmarks
- RealWorldWorkloadBenchmarks
- RootPipelineBenchmarks
- ScalabilityBenchmarks
- SyncProtocolBenchmarks
- TrunkPerformanceBenchmarks

**Interactive dashboard**: `BenchmarkDashboard.html` for visual analysis.

---

## Statistics

### Code Changes
- **Files changed**: 142
- **Lines added**: 28,027
- **Lines removed**: 1,241
- **Net growth**: +26,786 lines

### Test Coverage
- **Total tests**: 295 (up from 101)
- **Pass rate**: 100%
- **New test files**: 8
  - BranchExtensibilityTests.cs
  - DeleteSyncTests.cs
  - DeltaSyncTests.cs
  - LifecycleManagementTests.cs
  - PolicyEnforcementRootTests.cs
  - ProductionFeaturesTests.cs
  - RootPipelineTests.cs
  - SyncModesTests.cs

### Storage Backends
- **Total trunk implementations**: 19 (up from 5)
- **Cloud providers**: 4 (Azure, AWS, S3-compatible)
- **RDBMS engines**: 4 (SQLite, MySQL, PostgreSQL, SQL Server)
- **Specialized trunks**: 7 (BTree, Parquet, Tiered, Cached, Resilient, etc.)

---

## Breaking Changes

### 1. IRoot Pipeline Refactor

**Old pattern** (decorator-based):
```csharp
var trunk = new CompressedTrunk<User>(
    new EncryptedTrunk<User>(
        new FileTrunk<User>()
    )
);
```

**New pattern** (root-based):
```csharp
var trunk = new FileTrunk<User>();
trunk.AddRoot(new EncryptionRoot(...));
trunk.AddRoot(new CompressionRoot(...));
```

**Migration**: Update your trunk composition to use roots. Decorator trunks still work but are deprecated.

### 2. ITrunk Method Renaming

Update calls from `Save/Load/Delete` to `Stash/Crack/Toss`:

```csharp
// Old
trunk.Save("id", nut);
var nut = trunk.Load("id");
trunk.Delete("id");

// New
trunk.Stash("id", nut);
var nut = trunk.Crack("id");
trunk.Toss("id");
```

**Note**: Old methods still work (marked with `[Obsolete]`).

### 3. ITrunkCapabilities Expanded

New properties added to `ITrunkCapabilities`:
- `SupportsNativeIndexes`
- `SupportsFullTextSearch`
- `SupportsComputedIndexes`

**Migration**: Ensure custom trunk implementations return appropriate values.

---

## Bug Fixes

- ✅ **Fixed delete sync propagation** - Deletes now propagate across branches (was TODO)
- ✅ **Fixed circular sync prevention** - Loop guard prevents infinite sync loops
- ✅ **Fixed concurrent cache access** - Thread-safe cache operations
- ✅ **Fixed branch lifecycle states** - Proper state machine transitions
- ✅ **Fixed trunk flush timing** - Batch writes flush correctly on disposal

---

## Developer Experience Improvements

### 1. Fluent Acorn Builder Enhancements

```csharp
var tree = new Acorn<User>()
    .WithStoragePath("./data")
    .WithCache(maxSize: 5000)
    .WithConflictResolution(new CustomJudge())
    .WithIndex(u => u.Email)
    .WithIndex(u => new { u.Department, u.Age }) // Composite index
    .Sprout();
```

### 2. Extension Methods for Common Patterns

```csharp
using AcornDB.Storage;

// Resilience
var resilient = trunk.WithResilience(retries: 3);

// Caching
var cached = trunk.WithCache(maxSize: 1000);

// Compression
var compressed = trunk.WithCompression();
```

### 3. Enhanced CLI

```bash
# New commands
acorn migrate ./data.db --to sqlite --output ./migrated.db
acorn inspect ./data.db --show-capabilities
acorn benchmark --trunk sqlite --operations 100000
```

---

## 📚 Documentation Updates

- ✅ **Updated README.md** with v0.5 features
- ✅ **New PRODUCTION_FEATURES.md** guide
- ✅ **Cloud Storage Guide** (wiki)
- ✅ **Data Lake Guide** with Parquet examples
- ✅ **Policy Enforcement Guide**
- ✅ **Resilience Patterns Guide**

---

## Performance Improvements

### Benchmark Results (M4 MacBook Pro)

| Operation | v0.4 | v0.5 | Improvement |
|-----------|------|------|-------------|
| Stash (BTree) | 12K ops/sec | 98K ops/sec | **8.2x faster** |
| Crack (BTree) | 15K ops/sec | 125K ops/sec | **8.3x faster** |
| Batch Write (SQLite) | 5K ops/sec | 45K ops/sec | **9x faster** |
| Sync (In-Process) | 20K nuts/sec | 1M nuts/sec | **50x faster** |
| Query (Indexed) | 2K ops/sec | 18K ops/sec | **9x faster** |

### Memory Efficiency

- **Root pipeline**: Zero allocations with byte array pooling
- **BTree**: 60% memory reduction vs FileTrunk
- **Cache**: LRU eviction prevents unbounded growth

---

## What's Next (v0.6 Roadmap)

### Developer Experience Focus

- **WithId() API** - Composite keys, attribute-based configuration
- **[PrimaryKey] / [Indexed] attributes** - Convention-based indexing
- **Query builder improvements** - Better LINQ integration
- **Type providers** - F# integration
- **GraphQL support** - Query trees via GraphQL

### Enterprise Features

- **Multi-tenancy** - Per-tenant data isolation
- **BarkCodes authentication** - Token-based auth for sync
- **Critters RBAC** - Role-based access control
- **ForageRights** - Fine-grained permissions
- **Encryption at rest** - Trunk-level encryption by default

---

## Installation

### NuGet Packages

```bash
# Core library
dotnet add package AcornDB --version 0.5.0

# Cloud storage
dotnet add package AcornDB.Persistence.Cloud --version 0.5.0

# RDBMS backends
dotnet add package AcornDB.Persistence.RDBMS --version 0.5.0

# Data lake
dotnet add package AcornDB.Persistence.DataLake --version 0.5.0
```

### From Source

```bash
git clone https://github.com/Anadak-LLC/AcornDB.git
cd AcornDB
git checkout features/propagation_enhancements
dotnet build
dotnet test
```

---

## Acknowledgments

This release represents a **massive leap forward** for AcornDB. Special thanks to:

- BenchmarkDotNet team for performance tooling
- Everyone who reported issues and suggested features

---

## Support & Feedback

- **GitHub Issues**: https://github.com/Anadak-LLC/AcornDB/issues
- **Discussions**: https://github.com/Anadak-LLC/AcornDB/discussions
- **Wiki**: https://github.com/Anadak-LLC/AcornDB/wiki

---

## Let's Go Nuts!

— The AcornDB Team
