
# 🌰 AcornDB

![AcornDB logo](https://raw.githubusercontent.com/Anadak-LLC/AcornDB/main/cdf1927f-1efd-4e54-8772-45476d5e6819.png)

**AcornDB** is a lightweight, reactive, embedded database for .NET — for devs who’d rather ship products than pay $400/month to store 5MB of JSON.

> 🐿️ Nutty by design. Practical by necessity.

---

## 🚀 Why AcornDB Exists

Most apps don’t need Cosmos DB, Kafka, or Redis.

They need:
- Fast, local-first persistence
- Simple per-tenant or per-user storage
- Offline support + syncing that doesn’t make you cry

**AcornDB is for:**
- Desktop apps  
- IoT devices  
- Mobile backends  
- CLI tools  
- Serverless & edge workloads  
- And yes — *you* with the single-user SaaS that stores 10KB per user

---

## 🍁 Core Concepts

| Term             | What It Means                                                |
|------------------|--------------------------------------------------------------|
| `Tree<T>`        | A local document collection — your "embedded table"          |
| `Nut<T>`         | An object wrapped with metadata (TTL, version, timestamp)    |
| `ITrunk<T>`      | Storage abstraction: File, Memory, Azure Blob, or versioned  |
| `Branch`         | A connection to a remote Tree via HTTP                       |
| `Tangle`         | A live sync session between two Trees                        |
| `Grove`          | A set of Trees managed + synced together                     |
| `Canopy`         | (Internal) sync orchestrator living inside the Grove         |
| `Stash/Crack/Toss` | Insert, read, and delete objects — squirrel-style verbs    |
| `Shake()`        | Manual sync trigger                                          |

---

## 🔧 Features

| Feature                          | Description |
|----------------------------------|-------------|
| 🌰 `Stash`, `Crack`, `Toss`       | Drop-in persistence with zero boilerplate |
| 🎯 **Auto-ID Detection**          | Stash without explicit IDs - automatically uses `Id` or `Key` properties |
| 🛡️ `Nut<T>`                       | Versioned, timestamped, TTL-wrapped records |
| 🔁 `Branch`, `Tangle`, `Grove`    | Live sync between Trees, across machines |
| 🪢 `Entangle<T>()`                | Sync trees via HTTP or **in-process** without a server |
| 🎩 `Oversee<T>()`                 | One-liner to monitor remote branches |
| ⚖️ `Squabble()` + Judge           | Built-in conflict resolution with custom override |
| 🧠 `INutment<TKey>`               | Typed ID interface for strongly keyed documents |
| 🧹 `SmushNow()`                   | Manual compaction of log-based storage |
| 🛰️ `ExportChanges()` / `ImportChanges()` | Manual sync if you're old-school |
| 🌲 `Grove.Plant<T>()`             | Auto-creates and registers a `Tree<T>` |
| 🔐 Totem-based auth (coming)      | Because why not woodland-themed security? |

---

## 🧪 Getting Started

```bash
# Coming soon to NuGet:
dotnet add package AcornDB
```

### Quick Example

```csharp
// Define your model with an Id property
public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }
}

// Create a Tree (defaults to FileTrunk - no config needed!)
var tree = new Tree<User>();

// Stash without explicit ID (auto-detected from Id property)
tree.Stash(new User { Name = "Squirrelius Maximus" });

// Crack it back
var user = tree.Crack("...");
```

### With Sync

```csharp
// Set up syncing with a Grove
var grove = new Grove();
grove.Plant(tree);
grove.Oversee<User>(new Branch("http://localhost:5000")); // auto-sync!

tree.Shake(); // optionally force a sync
```

---

## ✨ What Makes AcornDB Simple

### 🎯 Auto-ID Detection
No more specifying IDs twice. If your model has an `Id` or `Key` property, AcornDB finds it automatically:

```csharp
public class Task
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; }
}

var tree = new Tree<Task>();
tree.Stash(new Task { Title = "Learn AcornDB" }); // ID auto-detected!
```

### 🪵 Optional Trunk (Defaults to FileTrunk)
Skip the boilerplate. Trees default to file-based storage:

```csharp
// Before
var tree = new Tree<User>(new FileTrunk<User>("data/User"));

// Now
var tree = new Tree<User>(); // Automatically uses FileTrunk!
```

### 🪢 In-Process Sync
Connect trees directly without HTTP:

```csharp
var tree1 = new Tree<User>();
var tree2 = new Tree<User>(new MemoryTrunk<User>());

tree1.Entangle(tree2); // No server needed!
```

### 📁 Shared Storage Sync
Multiple processes? Just point to the same directory:

```csharp
// Process 1
var tree1 = new Tree<User>(new FileTrunk<User>("shared/data"));
tree1.Stash(new User { Id = "alice", Name = "Alice" });

// Process 2
var tree2 = new Tree<User>(new FileTrunk<User>("shared/data"));
var alice = tree2.Crack("alice"); // Already there!
```

---

## 🗄️ Storage Abstraction (Trunks)

AcornDB uses **Trunks** to abstract storage — swap your backend without touching your Tree code.

### Available Trunks

| Trunk                  | Use Case                              | History | Sync |
|------------------------|---------------------------------------|---------|------|
| `FileTrunk<T>`         | Simple file-based storage            | ❌      | ✅   |
| `MemoryTrunk<T>`       | Fast in-memory (great for tests)     | ❌      | ✅   |
| `DocumentStoreTrunk<T>`| **Full versioning & time-travel**    | ✅      | ✅   |
| `AzureTrunk<T>`        | Azure Blob Storage                   | ❌      | ✅   |

### Examples

```csharp
// 📁 FileTrunk (DEFAULT): Simple, no history
var fileTree = new Tree<User>(); // Defaults to FileTrunk!
fileTree.Stash(new User { Id = "alice", Name = "Alice" }); // Auto-ID

// Or explicit path
var customTree = new Tree<User>(new FileTrunk<User>("data/users"));

// 💾 MemoryTrunk: Fast, non-durable
var memTree = new Tree<User>(new MemoryTrunk<User>());
memTree.Stash(new User { Id = "bob", Name = "Bob" });

// 📚 DocumentStoreTrunk: Full history & versioning
var docTree = new Tree<User>(new DocumentStoreTrunk<User>("data/versioned"));
docTree.Stash(new User { Id = "charlie", Name = "Charlie v1" });
docTree.Stash(new User { Id = "charlie", Name = "Charlie v2" });

var history = docTree.GetHistory("charlie"); // Get previous versions!
// Returns: 1 previous version ("Charlie v1")

// 🔄 Export/Import between trunks
var sourceTrunk = new FileTrunk<User>("data/source");
var targetTrunk = new AzureTrunk<User>("connection-string");

targetTrunk.ImportChanges(sourceTrunk.ExportChanges()); // Migrate!
```

### Time-Travel with DocumentStoreTrunk

```csharp
var tree = new Tree<Product>(new DocumentStoreTrunk<Product>("data/products"));

tree.Stash(new Product { Id = "widget", Name = "Widget v1.0" });
tree.Stash(new Product { Id = "widget", Name = "Widget v2.0" });
tree.Stash(new Product { Id = "widget", Name = "Widget v3.0" });

var current = tree.Crack("widget");     // "Widget v3.0"
var history = tree.GetHistory("widget"); // ["Widget v1.0", "Widget v2.0"]

// All changes stored in append-only log: data/products/changes.log
```

### NotSupportedException Pattern

Trunks that don't support history throw `NotSupportedException`:

```csharp
var memTree = new Tree<User>(new MemoryTrunk<User>());
try {
    var history = memTree.GetHistory("user1");
} catch (NotSupportedException) {
    Console.WriteLine("MemoryTrunk doesn't support history!");
}
```

### Feature Detection with ITrunkCapabilities

Check trunk features **without exceptions**:

```csharp
var trunk = new MemoryTrunk<User>();
var caps = trunk.GetCapabilities();

Console.WriteLine($"Trunk: {caps.TrunkType}");
Console.WriteLine($"History: {caps.SupportsHistory}");
Console.WriteLine($"Sync: {caps.SupportsSync}");
Console.WriteLine($"Durable: {caps.IsDurable}");
Console.WriteLine($"Async: {caps.SupportsAsync}");

// Use extension methods for quick checks
if (trunk.CanGetHistory())
{
    var history = trunk.GetHistory("user1");
}
else
{
    Console.WriteLine("This trunk doesn't support history");
}
```

**Capability Matrix:**

| Trunk              | History | Sync | Durable | Async |
|--------------------|---------|------|---------|-------|
| FileTrunk          | ❌      | ✅   | ✅      | ❌    |
| MemoryTrunk        | ❌      | ✅   | ❌      | ❌    |
| DocumentStoreTrunk | ✅      | ✅   | ✅      | ❌    |
| AzureTrunk         | ❌      | ✅   | ✅      | ✅    |

---

## 🌐 Sync with TreeBark

**TreeBark** is the HTTP sync server for AcornDB - it exposes Trees over REST endpoints.

### Quick Start Sync Server

```csharp
// Server side (AcornSyncServer project)
var grove = new Grove();
grove.Plant(new Tree<User>(new FileTrunk<User>("data/users")));
grove.Plant(new Tree<Product>(new FileTrunk<Product>("data/products")));

// Run with: dotnet run --project AcornSyncServer
// TreeBark starts on http://localhost:5000
```

### TreeBark REST API

| Endpoint                        | Method | Description                    |
|---------------------------------|--------|--------------------------------|
| `/`                             | GET    | Health check + API docs        |
| `/bark/{treeName}/stash`        | POST   | Stash a nut to remote tree     |
| `/bark/{treeName}/crack/{id}`   | GET    | Crack a nut from remote tree   |
| `/bark/{treeName}/toss/{id}`    | DELETE | Toss a nut from remote tree    |
| `/bark/{treeName}/export`       | GET    | Export all nuts from tree      |
| `/bark/{treeName}/import`       | POST   | Import nuts into tree          |

### Client-Side Sync with Branch

```csharp
// Client side - connect to remote TreeBark server
var localTree = new Tree<User>(new MemoryTrunk<User>());
var branch = new Branch("http://localhost:5000/bark/User");

// Manual push with auto-ID
var alice = new User { Id = "alice", Name = "Alice" };
localTree.Stash(alice);
branch.TryPush(alice.Id, localTree.Crack(alice.Id));

// Manual pull
await branch.ShakeAsync(localTree); // Pulls all remote changes

// Auto-sync with Tangle
var grove = new Grove();
grove.Plant(localTree);
grove.Entangle<User>(branch, "sync-session-1"); // Auto-syncs on every stash!
```

### Full Sync Example

**Server** (`dotnet run --project AcornSyncServer`):
```csharp
var grove = new Grove();
grove.Plant(new Tree<User>(new DocumentStoreTrunk<User>("data/users")));
// TreeBark running on http://localhost:5000
```

**Client 1** (Desktop App):
```csharp
var tree1 = new Tree<User>(new FileTrunk<User>("client1/users"));
var branch = new Branch("http://localhost:5000");

var alice = new User { Id = "alice", Name = "Alice" };
tree1.Stash(alice); // Auto-ID detection
branch.TryPush(alice.Id, tree1.Crack(alice.Id)); // Syncs to server
```

**Client 2** (Mobile App):
```csharp
var tree2 = new Tree<User>(new MemoryTrunk<User>());
var branch = new Branch("http://localhost:5000");

await branch.ShakeAsync(tree2); // Pulls "alice" from server!
var alice = tree2.Crack("alice"); // "Alice" is now local
```

---

## 🌰 AcornDB Visualizer - Web UI

Explore your Grove with an interactive web dashboard!

```bash
cd AcornVisualizer
dotnet run
# Open browser to http://localhost:5100
```

**Features:**
- 📊 **Live Dashboard** - Real-time stats on trees, nuts, and operations
- 🌳 **Tree Explorer** - Browse all trees with detailed metadata
- 📈 **Graph View** - Interactive circular node visualization
- 🔍 **Nut Inspector** - View payloads, timestamps, and history
- ⚙️ **Trunk Info** - See capabilities (history, sync, durable, async)
- 🔄 **Auto-Refresh** - Updates every 5 seconds

**Perfect for:**
- Local development and debugging
- Visual demos and presentations
- Understanding your grove structure
- Monitoring nut operations

See `AcornVisualizer/README.md` for full documentation.

---

## 🌲 Same-Host Sync (No Server Required!)

For processes on the same host, AcornDB offers **three simple sync strategies**:

### ✅ Option 1: Shared FileTrunk (Simplest!)

**Just point both trees to the same directory:**

```csharp
// Process 1
var tree1 = new Tree<User>(new FileTrunk<User>("shared/users"));
tree1.Stash(new User { Id = "alice", Name = "Alice" });

// Process 2
var tree2 = new Tree<User>(new FileTrunk<User>("shared/users"));
var alice = tree2.Crack("alice"); // ✅ Automatically synced!
```

**Zero setup. Zero config. Just works.**

---

### 🪢 Option 2: In-Process Tree Entanglement

**Sync two trees directly without HTTP:**

```csharp
var tree1 = new Tree<User>();
var tree2 = new Tree<User>(new MemoryTrunk<User>());

tree1.Entangle(tree2); // Direct tree-to-tree sync!

tree1.Stash(new User { Id = "bob", Name = "Bob" });
// Automatically synced to tree2 via InProcessBranch
```

**Perfect for in-memory scenarios or testing.**

---

### 📂 Option 3: File System Sync Hub

**For more complex multi-process scenarios:**

```csharp
// Process 1
var tree1 = new Tree<User>(new DocumentStoreTrunk<User>("data/process1/users"));
var syncHub = new FileSystemSyncHub<User>("data/sync-hub");

tree1.Stash(new User { Id = "alice", Name = "Alice" });
syncHub.PublishChanges("process1", tree1.ExportChanges());

// Process 2
var tree2 = new Tree<User>(new DocumentStoreTrunk<User>("data/process2/users"));
var changes = syncHub.PullChanges("process2");
foreach (var nut in changes)
{
    tree2.Stash(nut.Payload);
}
```

### Try the Demo

```bash
# Terminal 1
cd SyncDemo
run-demo.cmd 1

# Terminal 2
cd SyncDemo
run-demo.cmd 2
```

Watch changes sync between processes in real-time via the file system!

### When to Use Each Sync Strategy

| Scenario | Recommended Approach |
|----------|---------------------|
| Same host, multiple processes | 🟢 Shared FileTrunk |
| Same process, different trees | 🟢 In-Process Entanglement |
| Different hosts | 🟢 TreeBark HTTP |
| Desktop apps with multiple instances | 🟢 Shared FileTrunk |
| Mobile to cloud | 🟢 TreeBark HTTP |
| Distributed systems | 🟢 TreeBark HTTP |
| CLI tools | 🟢 Shared FileTrunk |
| Complex multi-process with separate storage | 🟢 File System Sync Hub |

---

## 🧱 Project Structure

| Folder             | Purpose                                      |
|--------------------|----------------------------------------------|
| `AcornDB`          | Core engine (Tree, Nut, Trunk, Tangle)       |
| `AcornSyncServer`  | **TreeBark**: HTTP sync server (REST API)    |
| `AcornVisualizer`  | **Web UI**: Interactive grove dashboard      |
| `AcornDB.Canopy`   | SignalR hub + visualizations                 |
| `AcornDB.Demo`     | Examples showcasing all features             |
| `SyncDemo`         | **Live multi-client sync demonstration**     |
| `AcornDB.Test`     | xUnit tests (**26 passing**)                 |

---

## 🧙 What's Coming

- 🔐 **Auth**: Totems, ForageRights, Critters, and BarkCodes
- 📡 **Mesh sync**: Peer-to-peer Tangle networks
- 📦 **NuGet & CLI**: Install and create projects with `acorn new`
- 🔁 **AutoRecovery**: Offline-first sync queue with resilience
- 🧪 **Playgrounds**: Sample apps, code snippets, and demos
- 🎨 **Visualizer Enhancements**: Real-time updates, diff viewer, dark mode  

---



---

## 🌲 The Acorn Ethos

> 🐿️ Serious software. Zero seriousness.

AcornDB was born out of frustration with bloated infra, soulless APIs, and naming things like `DataClientServiceManagerFactoryFactory`.

So we built something better — not just in function, but in **vibe**.

**We believe:**

- Developers deserve **fun**.
- Tools should make you **smile**, not sigh.
- Syncing JSON should not require Kubernetes and a degree in wizardry.
- **"Toss the nut and shake the tree"** should be valid engineering advice.

If you’ve ever rage-quit YAML, yelled at Terraform, or cried syncing offline-first apps —  
welcome. You’ve found your grove.

🌰 *Stash boldly. Crack with confidence. And never, ever apologize for getting a little squirrelly.*


## 🦦 Built with acorns and sarcasm

We’re tired of YAML. Tired of cloud bills. Tired of `DataServiceFactoryClientFactoryFactory`.

So we built AcornDB.

If you fork this, star it, or build something fun — send us your weirdest squirrel pun.

---

## 🐿️ Stay nutty.
