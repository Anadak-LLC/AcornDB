# 🌰 Welcome to AcornDB

**A distributed, embeddable, reactive object database for .NET.**
Local-first persistence with mesh sync, LRU cache eviction, TTL enforcement, pluggable storage backends, and zero configuration.

> 🐿️ Built for developers who'd rather ship products than manage infrastructure.

---

## What is AcornDB?

AcornDB is a local-first, embeddable document database that makes persistence **simple and fun**. Instead of drowning in YAML configs and cloud bills, you work with **Trees** and **Nuts**. Instead of complex sync protocols, you **Shake** your trees. And when conflicts happen? Your nuts **Squabble** it out.

But beneath the whimsy lies serious engineering:

✅ **Zero-configuration** - Stash data without setup
✅ **Live sync** - Real-time sync across devices and processes
✅ **Conflict resolution** - Pluggable judges for handling conflicts
✅ **Time-travel** - Full version history with Git and DocumentStore
✅ **Pluggable storage** - File, memory, Git, cloud, or SQL backends
✅ **Reactive events** - Subscribe to changes with real-time notifications
✅ **LRU cache** - Automatic eviction with configurable limits
✅ **TTL enforcement** - Auto-cleanup of expired items
✅ **Encryption & compression** - AES encryption + Gzip/Brotli

---

## Why AcornDB?

Most apps don't need Cosmos DB, Kafka, or a $400/month cloud bill to store 5MB of JSON.

**You need:**
- Fast, local-first persistence
- Simple per-tenant or per-user storage
- Offline support + sync that actually works
- Zero configuration, zero ceremony

**Perfect for:**
- Desktop & mobile apps
- IoT & edge devices
- CLI tools & utilities
- Serverless & edge workloads
- Single-user SaaS apps

---

## Quick Example

```csharp
using AcornDB;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }
}

// Zero config - just works!
var tree = new Acorn<User>().Sprout();

// Auto-ID detection
tree.Stash(new User { Name = "Alice" });

// LINQ queries
var users = tree.GetAll().Where(u => u.Name.StartsWith("A")).ToList();

// Subscribe to changes
tree.Subscribe(user => Console.WriteLine($"Changed: {user.Name}"));
```

---

## 🎯 What's New in v0.4

### Git as a Database
Every `Stash()` creates a Git commit. Use Git tools to inspect your database:

```csharp
var tree = new Acorn<User>()
    .WithGitStorage("./my_db", autoPush: true)
    .Sprout();

tree.Stash(new User { Name = "Alice" });
// ✅ Git commit: "Stash: alice at 2025-10-07 10:30:45"

// Time-travel through history
var history = tree.GetHistory("alice");
```

### Nursery System
Dynamic trunk discovery at runtime:

```csharp
// Browse available storage
Console.WriteLine(Nursery.GetCatalog());

// Grow from config (no hardcoded dependencies!)
var tree = new Acorn<User>()
    .WithTrunkFromNursery("git", new() { { "repoPath", "./data" } })
    .Sprout();
```

### Cloud & RDBMS Storage
Separate packages for cloud and database backends:

```csharp
// S3 storage
dotnet add package AcornDB.Persistence.Cloud
var s3Tree = new Acorn<User>()
    .WithS3Storage(accessKey, secretKey, bucketName)
    .Sprout();

// PostgreSQL
dotnet add package AcornDB.Persistence.RDBMS
var pgTree = new Acorn<User>()
    .WithPostgreSQL("Host=localhost;Database=acorn")
    .Sprout();
```

---

## 📚 Documentation Guide

### Getting Started
- **[[Getting Started]]** - Install, configure, and stash your first nut
- **[[Concepts]]** - Core concepts: Tree, Nut, Trunk, Branch, Grove, Nursery

### Storage
- **[[Storage]]** - FileTrunk, MemoryTrunk, DocumentStoreTrunk, GitHubTrunk
- **[[CLOUD_STORAGE_GUIDE]]** - S3, Azure Blob, and cloud storage setup
- **[[NURSERY_GUIDE]]** - Dynamic trunk discovery and factory pattern
- **[[GITHUB_TRUNK_DEMO]]** - Git-as-database guide with full examples

### Sync & Distribution
- **[[Data Sync]]** - In-process sync, HTTP sync, and mesh networks
- **[[Cluster & Mesh]]** - Multi-grove forests and distributed patterns

### Advanced Features
- **[[Events]]** - Reactive subscriptions and change notifications
- **[[Conflict Resolution]]** - Squabbles, judges, and resolution strategies
- **[[COMPRESSION_FEATURE]]** - Gzip/Brotli compression for storage optimization
- **[[Dashboard]]** - AcornVisualizer web UI for grove management

### Reference
- **[[CHANGELOG]]** - Version history and release notes

---

## 🌲 Core Concepts at a Glance

| Term | Description |
|------|-------------|
| **Tree&lt;T&gt;** | A collection of documents (like a table) |
| **Nut&lt;T&gt;** | A document with metadata (timestamp, version, TTL) |
| **Trunk** | Storage backend (file, memory, Git, cloud, SQL) |
| **Branch** | Connection to a remote Tree via HTTP |
| **Tangle** | Live sync session between two Trees |
| **Grove** | Container managing multiple Trees with unified sync |
| **Nursery** | Factory registry for discovering and creating trunks |
| **Acorn&lt;T&gt;** | Fluent builder for configuring Trees |

**Deep dive: [[Concepts]]**

---

## 🚀 Quick Navigation

### New Users
1. Start with **[[Getting Started]]**
2. Learn **[[Concepts]]**
3. Explore **[[Storage]]** options

### Building Sync
1. Read **[[Data Sync]]**
2. Check **[[Conflict Resolution]]**
3. Review **[[Cluster & Mesh]]**

### Advanced Features
1. Try **[[GITHUB_TRUNK_DEMO]]**
2. Set up **[[CLOUD_STORAGE_GUIDE]]**
3. Use the **[[Dashboard]]**

---

## 🎯 Storage Options

| Trunk | Package | Durable | History | Use Case |
|-------|---------|---------|---------|----------|
| FileTrunk | Core | ✅ | ❌ | Simple file storage (default) |
| MemoryTrunk | Core | ❌ | ❌ | Fast in-memory (testing) |
| DocumentStoreTrunk | Core | ✅ | ✅ | Versioning & time-travel |
| **GitHubTrunk** | Core | ✅ | ✅ | **Git-as-database (NEW!)** |
| AzureTrunk | Cloud | ✅ | ❌ | Azure Blob Storage |
| S3Trunk | Cloud | ✅ | ❌ | AWS S3, MinIO, Spaces |
| SqliteTrunk | RDBMS | ✅ | ❌ | SQLite database |
| SqlServerTrunk | RDBMS | ✅ | ❌ | Microsoft SQL Server |
| PostgreSqlTrunk | RDBMS | ✅ | ❌ | PostgreSQL |
| MySqlTrunk | RDBMS | ✅ | ❌ | MySQL/MariaDB |

**Learn more: [[Storage]]**

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
- API names should be memorable

If you've ever rage-quit YAML or cried syncing offline-first apps — **welcome home**. 🌳

---

## Need Help?

- 📖 **Start here:** [[Getting Started]]
- 🧠 **Learn concepts:** [[Concepts]]
- 🔄 **Build sync:** [[Data Sync]]
- 🐛 **Report bugs:** [GitHub Issues](https://github.com/Anadak-LLC/AcornDB/issues)
- 💬 **Join discussion:** [GitHub Discussions](https://github.com/Anadak-LLC/AcornDB/discussions)

---

🌰 *Stash boldly. Crack with confidence. And never, ever apologize for getting a little squirrelly.*
