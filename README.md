
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
| `NutShell<T>`    | An object wrapped with metadata (TTL, version, timestamp)    |
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
| 🛡️ `NutShell<T>`                  | Versioned, timestamped, TTL-wrapped records |
| 🔁 `Branch`, `Tangle`, `Grove`    | Live sync between Trees, across machines |
| 🪢 `Entangle<T>()`                | Automatically starts syncing on stash/toss |
| 🎩 `Oversee<T>()`                 | One-liner to monitor remote branches |
| ⚖️ `Squabble()` + Judge          | Built-in conflict resolution with custom override |
| 🧠 `INutment<TKey>`               | Typed ID interface for strongly keyed documents |
| 🧹 `SmushNow()`                   | Manual compaction of log-based storage |
| 🛰️ `ExportChanges()` / `ImportChanges()` | Manual sync if you’re old-school |
| 🌲 `Grove.Plant<T>()`             | Auto-creates and registers a `Tree<T>` |
| 🔐 Totem-based auth (coming)      | Because why not woodland-themed security? |

---

## 🧪 Getting Started

```bash
# Coming soon to NuGet:
dotnet add package AcornDB
```

```csharp
// Create a Tree and stash some data
var tree = new Tree<User>();
tree.Stash("abc", new User { Name = "Squirrelius Maximus" });

// Set up syncing with a Grove
var grove = new Grove();
grove.Plant(tree);
grove.Oversee<User>(new Branch("http://localhost:5000")); // auto-sync!

tree.Shake(); // optionally force a sync
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
// 📁 FileTrunk: Simple, no history
var fileTree = new Tree<User>(new FileTrunk<User>("data/users"));
fileTree.Stash("alice", new User("Alice"));

// 💾 MemoryTrunk: Fast, non-durable
var memTree = new Tree<User>(new MemoryTrunk<User>());
memTree.Stash("bob", new User("Bob"));

// 📚 DocumentStoreTrunk: Full history & versioning
var docTree = new Tree<User>(new DocumentStoreTrunk<User>("data/versioned"));
docTree.Stash("charlie", new User("Charlie v1"));
docTree.Stash("charlie", new User("Charlie v2"));

var history = docTree.GetHistory("charlie"); // Get previous versions!
// Returns: 1 previous version ("Charlie v1")

// 🔄 Export/Import between trunks
var sourceTrunk = new FileTrunk<User>("data/source");
var targetTrunk = new AzureTrunk<User>("connection-string");

targetTrunk.ImportChanges(sourceTrunk.ExportChanges()); // Migrate!
```

### Time-Travel with DocumentStoreTrunk

```csharp
var trunk = new DocumentStoreTrunk<Product>("data/products");
var tree = new Tree<Product>(trunk);

tree.Stash("widget", new Product("Widget v1.0"));
tree.Stash("widget", new Product("Widget v2.0"));
tree.Stash("widget", new Product("Widget v3.0"));

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

// Manual push
localTree.Stash("alice", new User("Alice"));
branch.TryPush("alice", localTree.Crack("alice"));

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

tree1.Stash("alice", new User("Alice"));
branch.TryPush("alice", tree1.Crack("alice")); // Syncs to server
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

## 🌲 P2P File System Sync (Same Host)

For same-host multi-process scenarios, AcornDB supports **file system-based peer-to-peer sync** without needing a server!

### How It Works

Instead of HTTP, processes sync via a shared directory:

```
Process 1 (data/process1) ──┐
                            ├──► Sync Hub (data/sync-hub)
Process 2 (data/process2) ──┘
```

Each process:
- Maintains its own local `DocumentStoreTrunk`
- Exports changes to the shared sync hub
- Imports changes from other processes
- Resolves conflicts via timestamp comparison

### Example: Two Processes on Same Host

**Process 1:**
```csharp
var localTree = new Tree<User>(new DocumentStoreTrunk<User>("data/process1/users"));
var syncHub = new FileSystemSyncHub<User>("data/sync-hub");

localTree.Stash("alice", new User { Name = "Alice" });

// Export to hub
syncHub.PublishChanges("process1", localTree.ExportChanges());
```

**Process 2:**
```csharp
var localTree = new Tree<User>(new DocumentStoreTrunk<User>("data/process2/users"));
var syncHub = new FileSystemSyncHub<User>("data/sync-hub");

// Import from hub
var changes = syncHub.PullChanges("process2");
foreach (var shell in changes)
{
    localTree.Stash(shell.Id, shell.Payload);
}

// Process 2 now has Alice!
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

### When to Use File-Based vs HTTP Sync

| Scenario | Recommended Approach |
|----------|---------------------|
| Same host, multiple processes | 🟢 File-based P2P |
| Different hosts | 🟢 TreeBark HTTP |
| Desktop apps with multiple instances | 🟢 File-based P2P |
| Mobile to cloud | 🟢 TreeBark HTTP |
| Distributed systems | 🟢 TreeBark HTTP |
| CLI tools | 🟢 File-based P2P |

---

## 🧱 Project Structure

| Folder             | Purpose                                      |
|--------------------|----------------------------------------------|
| `AcornDB`          | Core engine (Tree, NutShell, Trunk, Tangle)  |
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
