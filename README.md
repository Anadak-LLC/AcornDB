# 🌰 AcornDB

![AcornDB logo](https://raw.githubusercontent.com/Anadak-LLC/AcornDB/main/cdf1927f-1efd-4e54-8772-45476d5e6819.png)

**A distributed, embeddable, reactive object database for .NET.**
Local-first persistence with mesh sync, LRU cache eviction, TTL enforcement, pluggable storage backends, and zero configuration.

> 🐿️ Built for developers who'd rather ship products than manage infrastructure.

```bash
dotnet add package AcornDB
dotnet add package AcornDB.Persistence.Cloud    # Optional: S3, Azure Blob
dotnet add package AcornDB.Persistence.RDBMS    # Optional: SQLite, SQL Server, PostgreSQL, MySQL
```

---

## 🚀 Why AcornDB?

**Most apps don't need Cosmos DB, Kafka, or a $400/month cloud bill to store 5MB of JSON.**

AcornDB is a **local-first**, **zero-config** object database that gives you:
- 🎯 **Zero Configuration** - `new Acorn<T>().Sprout()` and you're done
- 🔌 **Swappable Storage** - File, memory, Git, SQL, cloud - same API, different trunk
- ⚡ **Blazing Fast** - In-memory cache + high-performance memory-mapped storage
- 🔄 **Sync That Works** - In-process, HTTP, or mesh sync with automatic conflict resolution
- 🎨 **Fluent Queries** - `tree.Query().Where().OrderBy().Take()` - LINQ-style API
- 🌍 **Run Anywhere** - Desktop, mobile, IoT, serverless, edge, CLI tools

**What makes AcornDB different?**
1. **Trunk abstraction** - Swap storage backends without changing code. File → Git → S3 → SQL with one line.
2. **Nursery system** - Dynamic trunk discovery. Switch backends via environment variable, not recompilation.
3. **In-process sync** - No HTTP server needed. `tree1.Entangle(tree2)` and they stay in sync.
4. **Local-first by default** - Offline support isn't an afterthought, it's the foundation.
5. **Zero infrastructure** - No servers, no containers, no YAML. Just add a NuGet package.

**Perfect for:**
- 🖥️ Desktop & mobile apps (offline-first)
- 🤖 IoT & edge devices (low bandwidth, high resilience)
- 🔧 CLI tools & utilities (simple persistence)
- ⚡ Serverless & edge workloads (zero cold start overhead)
- 👤 Per-tenant SaaS apps (data isolation made easy)

---

## ⚡ Quick Start

### 30-Second Example

```csharp
using AcornDB;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }
    public int Age { get; set; }
}

// Create a tree (defaults to file storage, zero config!)
var tree = new Tree<User>();

// Or use the fluent builder pattern via Acorn
tree = new Acorn<User>().WithCompression().Sprout();

// Stash (auto-detects ID from property)
tree.Stash(new User { Name = "Alice", Age = 30 });

// Crack (retrieve)
var alice = tree.Crack("alice-id");

// Query with LINQ (using Nuts property)
var adults = tree.Nuts.Where(u => u.Age >= 18).ToList();

// Or use the fluent query API
var topUsers = tree.Query()
    .Where(u => u.Age >= 18)
    .Newest()
    .Take(10)
    .ToList();

// Subscribe to changes
tree.Subscribe(user => Console.WriteLine($"Changed: {user.Name}"));
```

### Swappable Storage - Same Code, Different Trunk

The **trunk abstraction** lets you swap storage backends without changing your code:

```csharp
using AcornDB;
using AcornDB.Storage;

// Start with file storage (default)
var tree = new Acorn<User>().Sprout();

// Need performance? Switch to BTreeTrunk
var tree = new Acorn<User>()
    .WithTrunk(new BTreeTrunk<User>("./data"))
    .Sprout();

// Need version history? Use Git as your database
var tree = new Acorn<User>()
    .WithGitStorage("./my_db", autoPush: true)
    .Sprout();
// ✅ Every Stash() creates a Git commit!

// Need cloud backup? Switch to S3
var tree = new Acorn<User>()
    .WithS3Storage(accessKey, secretKey, "my-bucket")
    .Sprout();

// Same API everywhere - just swap the trunk!
tree.Stash(new User { Name = "Alice" });
var alice = tree.Crack("alice-id");
```

**[More Storage Options →](wiki/Storage.md)** | **[Git Trunk Guide →](wiki/GITHUB_TRUNK_DEMO.md)** | **[Cloud Storage →](wiki/CLOUD_STORAGE_GUIDE.md)**

### Fluent Query API

Build powerful queries with a clean, LINQ-style syntax:

```csharp
using AcornDB;
using AcornDB.Query;

var tree = new Acorn<User>().Sprout();

// Fluent query API - simple and powerful
var topUsers = tree.Query()
    .Where(u => u.Age >= 18)
    .Where(u => u.IsActive)
    .OrderByDescending(u => u.Points)
    .Take(10)
    .ToList();

// Time-based queries
var recentActivity = tree.Query()
    .After(DateTime.Now.AddDays(-7))
    .Newest()
    .ToList();

// Or use standard LINQ on the Nuts property
var adults = tree.Nuts.Where(u => u.Age >= 18).ToList();

// Work with metadata using NutShells()
var withMetadata = tree.NutShells()
    .Where(nut => nut.Timestamp > DateTime.Now.AddHours(-1))
    .Select(nut => new { nut.Id, nut.Payload, nut.Timestamp });
```

### Dynamic Storage with Nursery

Discover and grow storage backends at runtime:

```csharp
// Browse available storage types
Console.WriteLine(Nursery.GetCatalog());

// Grow trunk from config (no hardcoded dependencies!)
var tree = new Acorn<User>()
    .WithTrunkFromNursery("git", new()
    {
        { "repoPath", "./my_repo" },
        { "authorName", "Alice" }
    })
    .Sprout();

// Change storage backend via environment variable
var storageType = Environment.GetEnvironmentVariable("STORAGE") ?? "file";
var tree = new Acorn<User>().WithTrunkFromNursery(storageType).Sprout();
```

**[Read More: Nursery Guide →](NURSERY_GUIDE.md)**

### In-Process Sync - No HTTP Required

AcornDB can sync trees **in the same process** without any network or HTTP server:

```csharp
// Create two trees
var tree1 = new Acorn<User>().Sprout();
var tree2 = new Acorn<User>().InMemory().Sprout();

// Entangle them - now they stay in sync automatically
tree1.Entangle(tree2);

tree1.Stash(new User { Name = "Bob", Age = 25 });
// ✅ Automatically synced to tree2 instantly!

var bob = tree2.Crack("bob-id"); // Bob is already there!

// Also supports HTTP sync when you need it
var branch = new Branch("http://localhost:5000");
grove.Oversee<User>(branch); // Auto-syncs to remote server
```

**Perfect for:** Multi-window apps, background workers, read replicas, local caching

**[Read More: Data Sync Guide →](wiki/Data-Sync.md)**

---

## 🎯 Features

### ✅ Implemented (v0.4)

| Feature | Description |
|---------|-------------|
| **🌰 Core API** | `Stash()`, `Crack()`, `Toss()` - squirrel-style CRUD |
| **🎯 Auto-ID Detection** | Automatic ID extraction from `Id` or `Key` properties |
| **🔌 Pluggable Storage** | Swap between file, memory, BTree, Git, SQL, cloud with one line |
| **🌱 Nursery System** | Dynamic trunk discovery and factory pattern |
| **🪢 In-Process Sync** | Direct tree-to-tree sync without HTTP server |
| **🌐 HTTP Sync** | TreeBark server for distributed sync |
| **⚖️ Conflict Resolution** | Pluggable `IConflictJudge<T>` (timestamp, version, custom) |
| **🔁 Reactive Events** | `Subscribe()` for real-time change notifications |
| **🧠 LRU Cache** | Automatic eviction with configurable limits |
| **⏰ TTL Enforcement** | Auto-cleanup of expired items |
| **🌲 Grove Management** | Multi-tree orchestration and sync |
| **📈 LINQ Support** | `Nuts` property returns `IEnumerable<T>` for LINQ queries |
| **🔍 Fluent Query API** | `tree.Query().Where().OrderBy().Take()` - powerful query builder |
| **⚡ BTreeTrunk** | High-performance memory-mapped storage with write batching |
| **🐿️ Git Storage** | GitHubTrunk - every stash is a Git commit for version history |
| **☁️ Cloud Storage** | S3, Azure Blob (via `AcornDB.Persistence.Cloud`) |
| **💾 RDBMS Storage** | SQLite, SQL Server, PostgreSQL, MySQL (via `AcornDB.Persistence.RDBMS`) |
| **🔐 Encryption** | AES encryption with password or custom provider |
| **🗜️ Compression** | Gzip/Brotli compression for storage optimization |
| **📊 AcornDB.Canopy** | Web UI for browsing groves and nuts |
| **📜 Full History** | `GetHistory(id)` for version history (Git & DocumentStore trunks) |

### ⚠️ Not Yet Implemented

The following features are advertised in the API but not yet fully implemented:

| Feature | Status | Planned Version | Details |
|---------|--------|----------------|---------|
| **Advanced Indexes** | API exists, throws `NotImplementedException` | v0.6.0 | See [IndexExtensions.cs](/AcornDB/Extensions/IndexExtensions.cs) marked `[Experimental]` |
| └─ Composite Indexes | `WithCompositeIndex()` | v0.6.0 (Phase 4.1) | Index on multiple properties |
| └─ Computed Indexes | `WithComputedIndex()` | v0.6.0 (Phase 4.2) | Index on computed expressions |
| └─ Full-Text Search | `WithTextIndex()` | v0.6.0 (Phase 4.3-4.4) | Text search with tokenization |
| └─ Time-Series Indexes | `WithTimeSeries()` | v0.6.0 (Phase 4.5) | Time-bucketing for temporal queries |
| └─ TTL Optimization | `WithTtl()` | v0.6.0 (Phase 4.6) | Automatic cleanup with indexes |
| **IRoot Support** | Partial implementation | v0.5.1 | 4 trunk types need migration to `TrunkBase<T>` |
| └─ DynamoDbTrunk | Stub implementation | v0.5.1 | IRoot pipeline not functional |
| └─ AzureTableTrunk | Stub implementation | v0.5.1 | IRoot pipeline not functional |
| └─ ParquetTrunk | Stub implementation | v0.5.1 | IRoot pipeline not functional |
| └─ TieredTrunk | Stub implementation | v0.5.1 | IRoot pipeline not functional |
| **Network Sync** | Stub (console only) | v0.7.0+ | See [AcornSyncServer/NOT_IMPLEMENTED.md](/AcornSyncServer/NOT_IMPLEMENTED.md) |
| └─ SyncEngine | Logs but doesn't sync | v0.7.0+ | Network transport not implemented |
| └─ Hardwood Server | Basic structure only | v0.7.0+ | REST endpoints not implemented |
| └─ Canopy Real-Time | SignalR stubs | v0.7.0+ | See [Canopy/NOT_IMPLEMENTED.md](/Canopy/NOT_IMPLEMENTED.md) |

**Note:** Only scalar indexes via `WithIndex()` are production-ready. All other index methods will throw `NotImplementedException` with a clear message indicating when they'll be available.

For more details, see [ARCHITECTURE_REVIEW_2025.md](/ARCHITECTURE_REVIEW_2025.md).

### ⚡ Performance

AcornDB is **fast**. Really fast.

**BTreeTrunk** uses memory-mapped files, write batching, and lock-free reads to deliver performance competitive with other embedded databases:

- 🚀 **Memory-mapped I/O** - Direct memory access for near-RAM speeds
- 📦 **Write batching** - Automatic buffering with configurable thresholds
- 🔓 **Lock-free reads** - Zero contention for read-heavy workloads
- 🗜️ **Binary serialization** - Minimal overhead for metadata

Run `dotnet run --project AcornDB.Benchmarks` to see benchmarks on your hardware.

**Typical performance:**
- **Inserts:** 100,000+ ops/sec (with batching)
- **Reads:** 500,000+ ops/sec (from memory-mapped cache)
- **Storage:** Efficient binary format with minimal overhead

### 🔜 Roadmap (Upcoming)

| Feature | Target | Description |
|---------|--------|-------------|
| **🔒 BarkCodes Auth** | v0.5 | Token-based authentication for sync |
| **🎭 Critters RBAC** | v0.5 | Role-based access control |
| **🌐 Mesh Sync** | v0.5 | Peer-to-peer multi-tree sync networks |
| **📦 CLI Tool** | v0.5 | `acorn new`, `acorn inspect`, `acorn migrate` |
| **🔄 Auto-Recovery** | v0.6 | Offline-first sync queue with retry |
| **📊 Prometheus Export** | v0.6 | OpenTelemetry metrics integration |
| **🎨 Dark Mode UI** | v0.6 | Canopy dashboard enhancements |

**[View Full Roadmap →](AcornDB_Consolidated_Roadmap.md)**

---

## 🗄️ Storage Backends (Trunks)

AcornDB uses **Trunks** to abstract storage. Swap backends without changing your code.

### Built-in Trunks

| Trunk | Package | Durable | History | Async | Use Case |
|-------|---------|---------|---------|-------|----------|
| `FileTrunk` | Core | ✅ | ❌ | ❌ | Simple file storage (default) |
| `MemoryTrunk` | Core | ❌ | ❌ | ❌ | Fast in-memory (testing) |
| `BTreeTrunk` | Core | ✅ | ❌ | ❌ | High-performance memory-mapped storage |
| `DocumentStoreTrunk` | Core | ✅ | ✅ | ❌ | Versioning & time-travel |
| `GitHubTrunk` | Core | ✅ | ✅ | ❌ | Git-as-database with commit history |
| `AzureTrunk` | Cloud | ✅ | ❌ | ✅ | Azure Blob Storage |
| `S3Trunk` | Cloud | ✅ | ❌ | ✅ | AWS S3, MinIO, DigitalOcean Spaces |
| `SqliteTrunk` | RDBMS | ✅ | ❌ | ❌ | SQLite database |
| `SqlServerTrunk` | RDBMS | ✅ | ❌ | ❌ | Microsoft SQL Server |
| `PostgreSqlTrunk` | RDBMS | ✅ | ❌ | ❌ | PostgreSQL |
| `MySqlTrunk` | RDBMS | ✅ | ❌ | ❌ | MySQL/MariaDB |

**[Read More: Storage Guide →](wiki/Storage.md)**
**[Cloud Storage Guide →](wiki/CLOUD_STORAGE_GUIDE.md)**
**[Nursery Guide →](NURSERY_GUIDE.md)**

### Using Fluent API

```csharp
using AcornDB;

// File storage (default)
var tree = new Acorn<User>().Sprout();

// High-performance BTree storage
var fastTree = new Acorn<User>()
    .WithTrunk(new BTreeTrunk<User>("./data"))
    .Sprout();

// Git storage
var gitTree = new Acorn<User>()
    .WithGitStorage("./my_repo", authorName: "Alice")
    .Sprout();

// With encryption + compression
var secureTree = new Acorn<User>()
    .WithEncryption("my-password")
    .WithCompression()
    .Sprout();

// LRU cache with limit
var cachedTree = new Acorn<User>()
    .WithLRUCache(maxSize: 1000)
    .Sprout();

// Via Nursery (dynamic)
var dynamicTree = new Acorn<User>()
    .WithTrunkFromNursery("git", new() { { "repoPath", "./data" } })
    .Sprout();
```

### Cloud & RDBMS Extensions

```csharp
using AcornDB.Persistence.Cloud;
using AcornDB.Persistence.RDBMS;

// S3 storage
var s3Tree = new Acorn<User>()
    .WithS3Storage(accessKey, secretKey, bucketName, region: "us-east-1")
    .Sprout();

// Azure Blob
var azureTree = new Acorn<User>()
    .WithAzureBlobStorage(connectionString, containerName)
    .Sprout();

// SQLite
var sqliteTree = new Acorn<User>()
    .WithSqlite("Data Source=mydb.db")
    .Sprout();

// PostgreSQL
var pgTree = new Acorn<User>()
    .WithPostgreSQL("Host=localhost;Database=acorn")
    .Sprout();
```

---

## 🌲 Core Concepts

| Term | Description |
|------|-------------|
| **Tree&lt;T&gt;** | A collection of documents (like a table) |
| **Nut&lt;T&gt;** | A document with metadata (timestamp, version, TTL) |
| **Trunk** | Storage backend abstraction (file, memory, Git, cloud, SQL) |
| **Branch** | Connection to a remote Tree via HTTP |
| **Tangle** | Live sync session between two Trees |
| **Grove** | Container managing multiple Trees with unified sync |
| **Nursery** | Factory registry for discovering and creating trunks |

**[Read More: Core Concepts →](wiki/Concepts.md)**

---

## 📚 Documentation

- **[Getting Started Guide](wiki/Getting-Started.md)** - Your first AcornDB app
- **[Core Concepts](wiki/Concepts.md)** - Understanding Trees, Nuts, and Trunks
- **[Storage Guide](wiki/Storage.md)** - Available trunk types and usage
- **[Data Sync Guide](wiki/Data-Sync.md)** - In-process, HTTP, and mesh sync
- **[Conflict Resolution](wiki/Conflict-Resolution.md)** - Handling sync conflicts
- **[Events & Reactivity](wiki/Events.md)** - Real-time change notifications
- **[GitHub Trunk Demo](wiki/GITHUB_TRUNK_DEMO.md)** - Git-as-database guide
- **[Nursery Guide](NURSERY_GUIDE.md)** - Dynamic trunk discovery
- **[Cloud Storage Guide](wiki/CLOUD_STORAGE_GUIDE.md)** - S3, Azure Blob setup
- **[Dashboard & Visualizer](wiki/Dashboard.md)** - Web UI for grove management
- **[Cluster & Mesh](wiki/Cluster-&-Mesh.md)** - Distributed sync patterns

---

## 🧪 Examples

```csharp
// Example 1: Local-first desktop app
var tree = new Acorn<Document>()
    .WithStoragePath("./user_data")
    .WithLRUCache(5000)
    .Sprout();

tree.Subscribe(doc => Console.WriteLine($"Changed: {doc.Title}"));

// Example 2: IoT edge device with cloud backup
var edgeTree = new Acorn<SensorReading>()
    .WithStoragePath("./local_cache")
    .Sprout();

var cloudBranch = new Branch("https://api.example.com/sync");
grove.Oversee<SensorReading>(cloudBranch); // Auto-syncs to cloud

// Example 3: Multi-tenant SaaS with per-tenant storage
string GetTenantPath(string tenantId) => $"./data/{tenantId}";

var tenantTree = new Acorn<Order>()
    .WithStoragePath(GetTenantPath(currentTenantId))
    .Sprout();

// Example 4: Git-based audit log
var auditLog = new Acorn<AuditEntry>()
    .WithGitStorage("./audit_log", authorName: "System")
    .Sprout();

auditLog.Stash(new AuditEntry { Action = "Login", User = "alice" });
// Git commit created with full history!
```

**[More Examples: Demo Project →](AcornDB.Demo/)**
**[Sample Apps: Interactive Tutorials →](AcornDB.SampleApps/)** - Todo, Blog, E-Commerce, and more with Spectre.Console UI
**[Live Sync Demo →](SyncDemo/)**

---

## 🎨 AcornDB.Canopy - Web UI

Explore your Grove with an interactive dashboard:

```bash
cd AcornDB.Canopy
dotnet run
# Open http://localhost:5100
```

**Features:**
- 📊 Real-time statistics
- 🌳 Tree explorer with metadata
- 📈 Interactive graph visualization
- 🔍 Nut inspector with history
- ⚙️ Trunk capabilities viewer

**[Read More: Dashboard Guide →](wiki/Dashboard.md)**

---

## 🧱 Project Structure

| Project | Purpose |
|---------|---------|
| `AcornDB` | Core library (Tree, Nut, Trunk, Sync) |
| `AcornDB.Persistence.Cloud` | S3, Azure Blob, cloud storage providers |
| `AcornDB.Persistence.RDBMS` | SQLite, SQL Server, PostgreSQL, MySQL |
| `AcornDB.Persistence.DataLake` | Data lake storage providers |
| `AcornSyncServer` | TreeBark - HTTP sync server |
| `AcornDB.Canopy` | Web UI dashboard (formerly AcornVisualizer) |
| `AcornDB.Cli` | Command-line interface tools |
| `AcornDB.SampleApps` | Interactive sample applications with Spectre.Console UI |
| `AcornDB.Demo` | Simple Functional Demos |
| `AcornDB.Test` | Test suite (100+ tests) |
| `AcornDB.Benchmarks` | Performance benchmarks |

---

## 🌰 The Acorn Philosophy

> 🐿️ **Serious software. Zero seriousness.**

We built AcornDB because we were tired of:
- Paying $$$ to store JSON
- Managing Kubernetes for simple persistence
- Writing `DataClientServiceManagerFactoryFactory`
- YAML-induced existential dread

**We believe:**
- Developers deserve tools that make them **smile**
- Syncing JSON shouldn't require a PhD
- Local-first is the future
- API names should be memorable (`Stash`, `Crack`, `Shake` > `Insert`, `Select`, `Synchronize`)

If you've ever rage-quit YAML or cried syncing offline-first apps — **welcome home**. 🌳

---

## 🤝 Contributing

We welcome contributions! Check out
- [Issues](https://github.com/Anadak-LLC/AcornDB/issues) for bugs and enhancements
- [Wiki](https://github.com/Anadak-LLC/AcornDB/wiki) for documentation

---

## 🐿️ Stay Nutty

Built with acorns and sarcasm by developers who've had enough.

⭐ **Star the repo** if AcornDB saved you from another cloud bill
🍴 **Fork it** if you want to get squirrelly
💬 **Share your weirdest squirrel pun** in the discussions


## 🧾 License

AcornDB is **source-available** software provided by [Anadak LLC](https://www.anadakcorp.com).

- Free for personal, educational, and non-commercial use under the  
  **[PolyForm Noncommercial License 1.0.0](./LICENSE)**  
- Commercial use requires a separate license from Anadak LLC.  
  Contact **[licensing@anadakcorp.com](mailto:licensing@anadakcorp.com)** for details.

© 2025 Anadak LLC. All rights reserved.