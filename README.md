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

Most apps don't need Cosmos DB, Kafka, or a $400/month cloud bill to store 5MB of JSON.

**You need:**
- ✅ Fast, local-first persistence
- ✅ Simple per-tenant or per-user storage
- ✅ Offline support + sync that actually works
- ✅ Zero configuration, zero ceremony

**Perfect for:**
- Desktop & mobile apps
- IoT & edge devices
- CLI tools & utilities
- Serverless & edge workloads
- Single-user SaaS apps

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
| **Acorn** | Factory registry for discovering and creating trunks |

**[Read More: Core Concepts →](wiki/Concepts.md)**

---

## ⚡ Quick Start

### 30-Second Example

```csharp
using AcornDB;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }
}

// Create a tree (defaults to file storage, zero config!)
var tree = new Tree<User>();

// Or use the fluent builder pattern via Acorn
tree = new Acorn<User>().WithCompression().Sprout();

// Stash (auto-detects ID from property)
tree.Stash(new User { Name = "Alice" });

// Crack (retrieve)
var alice = tree.Crack("alice-id");

// Query with LINQ
var adults = tree.Nuts.Where(u => u.Age >= 18).ToList();
```

### Use Git for Storage

Every `Stash()` creates a Git commit. Use Git tools to inspect your database:

```csharp
using AcornDB;
using AcornDB.Storage;

var tree = new Acorn<User>()
    .WithGitTrunk(repoPath: "./my_db", autoPush: true)
    .Sprout();

tree.Stash(new User { Id = "alice", Name = "Alice" });
// ✅ Git commit created: "Stash: alice at 2025-10-07 10:30:45"

// Time-travel through history
var history = tree.GetHistory("alice"); // All previous versions
```

```bash
cd my_db
git log --oneline
# f4e8a91 Stash: alice at 2025-10-07 10:30:45
# c2d1b3a Stash: bob at 2025-10-07 10:25:12
```

**[Read More: GitHub Trunk Guide →](wiki/GITHUB_TRUNK_DEMO.md)**

### Dynamic Storage with Nursery

Discover and grow storage backends at runtime:

```csharp
// Browse available storage types
Console.WriteLine(Nursery.GetCatalog());

// Grow trunk from config (no hardcoded dependencies!)
var tree = new Acorn<User>()
    .WithTrunk("git", new()
    {
        { "repoPath", "./my_repo" },
        { "authorName", "Alice" }
    })
    .Sprout();

// Change storage backend via environment variable
var storageType = Environment.GetEnvironmentVariable("STORAGE") ?? "file";
var tree = new Acorn<User>().WithTrunk(storageType).Sprout();
```

**[Read More: Nursery Guide →](NURSERY_GUIDE.md)**

### Real-Time Sync

```csharp
// In-process sync (no HTTP server needed!)
var tree1 = new Acorn<User>().Sprout();
var tree2 = new Acorn<User>().InMemory().Sprout();

tree1.Entangle(tree2); // Direct tree-to-tree sync

tree1.Stash(new User { Name = "Bob" });
// ✅ Automatically synced to tree2!

// HTTP sync with TreeBark server
var branch = new Branch("http://localhost:5000");
grove.Oversee<User>(branch); // Auto-syncs on every change
```

**[Read More: Data Sync Guide →](wiki/Data-Sync.md)**

---

## 🎯 Features

### ✅ Implemented (v0.4)

| Feature | Description |
|---------|-------------|
| **🌰 Core API** | `Stash()`, `Crack()`, `Toss()` - squirrel-style CRUD |
| **🎯 Auto-ID Detection** | Automatic ID extraction from `Id` or `Key` properties |
| **🔁 Reactive Events** | `Subscribe()` for real-time change notifications |
| **🪢 In-Process Sync** | Direct tree-to-tree sync without HTTP |
| **🌐 HTTP Sync** | TreeBark server for distributed sync |
| **🛡️ Versioned Nuts** | Timestamps, TTL, conflict detection built-in |
| **⚖️ Conflict Resolution** | Pluggable `IConflictJudge<T>` (timestamp, version, custom) |
| **🧠 LRU Cache** | Automatic eviction with configurable limits |
| **⏰ TTL Enforcement** | Auto-cleanup of expired items |
| **🌲 Grove Management** | Multi-tree orchestration and sync |
| **📊 AcornVisualizer** | Web UI for browsing groves and nuts |
| **🐿️ Git Storage** | GitHubTrunk - every stash is a Git commit! |
| **🌱 Nursery System** | Dynamic trunk discovery and factory pattern |
| **☁️ Cloud Storage** | S3, Azure Blob (via `AcornDB.Persistence.Cloud`) |
| **💾 RDBMS Storage** | SQLite, SQL Server, PostgreSQL, MySQL (via `AcornDB.Persistence.RDBMS`) |
| **🔐 Encryption** | AES encryption with password or custom provider |
| **🗜️ Compression** | Gzip/Brotli compression for storage optimization |
| **📈 LINQ Support** | `GetAll()` returns `IEnumerable<T>` for LINQ queries |
| **📜 Full History** | `GetHistory(id)` for version history (Git & DocumentStore trunks) |

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

// Git storage
var gitTree = new Acorn<User>()
    .WithGitTrunk("./my_repo", authorName: "Alice")
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
    .WithTrunk("git", new() { { "repoPath", "./data" } })
    .Sprout();
```

### Cloud & RDBMS Extensions

```csharp
using AcornDB.Persistence.Cloud;
using AcornDB.Persistence.RDBMS;

// S3 storage
var s3Tree = new Acorn<User>()
    .WithS3Trunk(accessKey, secretKey, bucketName, region: "us-east-1")
    .Sprout();

// Azure Blob
var azureTree = new Acorn<User>()
    .WithAzureBlobTrunk(connectionString, containerName)
    .Sprout();

// SQLite
var sqliteTree = new Acorn<User>()
    .WithSqliteTrunk("Data Source=mydb.db")
    .Sprout();

// PostgreSQL
var pgTree = new Acorn<User>()
    .WithPostgreSQLTrunk("Host=localhost;Database=acorn")
    .Sprout();
```

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
    .WithGitTrunk("./audit_log", authorName: "System")
    .Sprout();

auditLog.Stash(new AuditEntry { Action = "Login", User = "alice" });
// Git commit created with full history!
```

**[More Examples: Demo Project →](AcornDB.Demo/)**
**[Live Sync Demo →](SyncDemo/)**

---

## 🎨 Canopy - Web UI

Explore your Grove with an interactive dashboard:

```bash
cd Canopy
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
| `AcornDB.Sync` | TreeBark - HTTP sync server |
| `AcornDB.Canopy` | Web UI dashboard |
| `AcornDB.Demo` | Example applications |
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

We welcome contributions! Check out:
- [Roadmap](AcornDB_Consolidated_Roadmap.md) for planned features
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
- Commercial use requires a separate license from Anadak LLC. The cost of which will be proportionate to the degree of good you're doing.
  Contact **[licensing@anadakcorp.com](mailto:licensing@anadakcorp.com)** for details.

© 2025 Anadak LLC. All rights reserved.
