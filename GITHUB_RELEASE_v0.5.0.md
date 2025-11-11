# 🌰 AcornDB v0.5.0 - Production Resilience & Enterprise Storage

**Released**: October 2025

Transform your local database into a production-ready, enterprise-capable data platform with advanced resilience patterns and cloud-native storage.

---

## 🚀 What's New

### IRoot Pipeline Architecture
Revolutionary middleware system for database transformations. Compose encryption, compression, and policy enforcement as a declarative pipeline:

```csharp
var trunk = new FileTrunk<User>();
trunk.AddRoot(new EncryptionRoot("secret-key", sequence: 10));
trunk.AddRoot(new CompressionRoot(CompressionMethod.Gzip, sequence: 20));
```

### Resilient Storage Patterns
- **ResilientTrunk** - Exponential backoff, circuit breaker, automatic retries
- **NearFarTrunk** - Multi-tier hot/cold storage (memory + SQL)
- **CachedTrunk** - Intelligent LRU caching with TTL

### Enterprise Storage Backends
**Cloud** (NEW):
- Azure Blob Storage (`AzureTrunk`)
- Azure Table Storage (`AzureTableTrunk`)
- AWS DynamoDB (`DynamoDbTrunk`)
- S3-compatible storage (`CloudTrunk`)

**RDBMS** (Enhanced):
- SQLite, MySQL, PostgreSQL, SQL Server
- Native JSON indexing, write batching, JSONB support

**Data Lake** (NEW):
- Apache Parquet columnar storage (`ParquetTrunk`)
- Hot/cold tiering (`TieredTrunk`)

**High Performance**:
- **BTreeTrunk** - 8x faster than FileTrunk (100K ops/sec)

### Enhanced Sync & Branch
- Lifecycle management (Connect/Pause/Resume/Disconnect)
- Sync modes (Push/Pull/Bidirectional)
- Delete propagation across branches
- In-process sync: **50x faster** (1M nuts/sec)

### API Consistency
Renamed ITrunk methods for tree semantics consistency:
- `Save()` → `Stash()` ✅ Backward compatible
- `Load()` → `Crack()` ✅ Backward compatible
- `Delete()` → `Toss()` ✅ Backward compatible

Old methods still work with deprecation warnings.

---

## 📊 By The Numbers

- **142 files changed** (+28K lines)
- **295 tests** (100% passing)
- **19 trunk implementations** (up from 5)
- **13 benchmark suites** with interactive dashboard
- **7 sample applications** (production-ready)

### Performance 🔥
| Operation | v0.4 | v0.5 | Speedup |
|-----------|------|------|---------|
| BTree Stash | 12K/s | 98K/s | **8.2x** |
| BTree Crack | 15K/s | 125K/s | **8.3x** |
| SQLite Batch | 5K/s | 45K/s | **9x** |
| In-Process Sync | 20K/s | 1M/s | **50x** |
| Indexed Query | 2K/s | 18K/s | **9x** |

---

## 🔧 Breaking Changes

### 1. IRoot Pipeline (Recommended)
**Old** decorator pattern:
```csharp
var trunk = new CompressedTrunk<T>(new EncryptedTrunk<T>(new FileTrunk<T>()));
```

**New** root pipeline:
```csharp
var trunk = new FileTrunk<T>();
trunk.AddRoot(new EncryptionRoot(...));
trunk.AddRoot(new CompressionRoot(...));
```

### 2. ITrunk Method Names
Update calls to use new names (old methods deprecated):
```csharp
trunk.Stash(id, nut);  // was Save()
trunk.Crack(id);       // was Load()
trunk.Toss(id);        // was Delete()
```

---

## 📦 Installation

```bash
# Core library
dotnet add package AcornDB --version 0.5.0

# Optional: Cloud storage
dotnet add package AcornDB.Persistence.Cloud --version 0.5.0

# Optional: RDBMS backends
dotnet add package AcornDB.Persistence.RDBMS --version 0.5.0

# Optional: Data lake
dotnet add package AcornDB.Persistence.DataLake --version 0.5.0
```

---

## 🎯 Quick Examples

### Resilient Cloud Storage
```csharp
var azureTrunk = new AzureTrunk<User>("connectionString", "users");
var resilient = new ResilientTrunk<User>(azureTrunk, new ResilienceOptions
{
    MaxRetries = 3,
    CircuitBreakerThreshold = 5
});

var tree = new Acorn<User>().WithTrunk(resilient).Sprout();
```

### Multi-Tier Storage
```csharp
var nearFar = new NearFarTrunk<User>(
    near: new MemoryTrunk<User>(),      // Fast cache
    far: new SqliteTrunk<User>("db.db") // Durable storage
);
```

### Policy Enforcement
```csharp
var policy = new LocalPolicyEngine();
policy.RegisterRule(new TtlPolicyRule());

trunk.AddRoot(new PolicyEnforcementRoot(policy));
```

---

## 🐛 Bug Fixes

- Fixed delete sync propagation across branches
- Fixed circular sync prevention
- Fixed concurrent cache access
- Fixed branch lifecycle state transitions
- Fixed trunk flush timing on disposal

---

## 📚 Documentation

- [Full Release Notes](RELEASE_NOTES_v0.5.0.md)
- [Production Features Guide](AcornDB.Demo/PRODUCTION_FEATURES.md)
- [Cloud Storage Guide](wiki/CLOUD_STORAGE_GUIDE.md)
- [Sample Applications](AcornDB.SampleApps/Samples/)

---

## 🔮 What's Next (v0.6)

**Developer Experience Focus**:
- `WithId()` API for composite keys
- `[PrimaryKey]` / `[Indexed]` attributes
- Better LINQ integration
- F# type providers
- GraphQL support

**Enterprise Features**:
- Multi-tenancy
- BarkCodes authentication
- RBAC (Critters)
- Fine-grained permissions (ForageRights)

---

## 🙏 Acknowledgments

Thanks to the .NET community for feedback, BenchmarkDotNet team for tooling, and everyone who contributed ideas and bug reports.

---

**Full Changelog**: https://github.com/Anadak-LLC/AcornDB/compare/v0.4.0...v0.5.0

🌰 **Let's go nuts!** 🚀
