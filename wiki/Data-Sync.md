# 🔄 Data Sync

AcornDB provides multiple sync strategies for keeping Trees in sync across devices, processes, and servers.

## Sync Architecture Overview

```
┌─────────────┐         ┌─────────────┐         ┌─────────────┐
│  Client 1   │         │  TreeBark   │         │  Client 2   │
│   (Tree)    │◄────────┤   Server    ├────────►│   (Tree)    │
└─────────────┘         └─────────────┘         └─────────────┘
      ▲                        ▲                        ▲
      │                        │                        │
   Branch                   Grove                    Branch
   Tangle                   Canopy                   Tangle
```

---

## 🌉 Branches - Remote Connections

A **Branch** represents an HTTP connection to a remote Tree (typically via TreeBark server).

### Creating a Branch

```csharp
using AcornDB.Sync;

var branch = new Branch("http://sync-server:5000");
```

### Manual Push

```csharp
var user = new User { Name = "Alice" };
var nut = new Nut<User>
{
    Id = "alice",
    Payload = user,
    Timestamp = DateTime.UtcNow
};

branch.TryPush("alice", nut);
// Pushes to: http://sync-server:5000/bark/user/stash
```

### Manual Pull (Shake)

```csharp
await branch.ShakeAsync(localTree);
// Pulls all nuts from: http://sync-server:5000/bark/user/export
```

---

## 🪢 Tangles - Live Sync Sessions

A **Tangle** creates a persistent sync relationship between a local Tree and a remote Branch.

### Creating a Tangle

```csharp
var localTree = new Tree<User>(new FileTrunk<User>("data/users"));
var remoteBranch = new Branch("http://sync-server:5000");

var tangle = new Tangle<User>(localTree, remoteBranch, "sync-session-1");
```

### Auto-Sync on Stash

Once a Tangle is registered, stashing automatically pushes to the remote:

```csharp
localTree.Stash("bob", new User { Name = "Bob" });
// Automatically pushes "bob" to the remote branch via tangle
```

### Manual Sync All

```csharp
tangle.PushAll(localTree); // Pushes all nuts to remote
```

---

## 🌲 Grove Entanglement

A **Grove** manages entanglements across multiple Trees.

### Entangle a Single Tree

```csharp
var grove = new Grove();
grove.Plant(new Tree<User>(new FileTrunk<User>("data/users")));

var branch = new Branch("http://sync-server:5000");
grove.Entangle<User>(branch, "user-sync");
```

### Oversee (Auto-Monitor)

**Oversee** is a one-liner for entangling + monitoring:

```csharp
grove.Oversee<User>(new Branch("http://sync-server:5000"), "user-sync");
// Equivalent to: grove.Entangle<User>(branch, "user-sync")
```

### Shake All Trees

```csharp
grove.ShakeAll(); // Syncs all tangles in the grove
```

---

## 🛰️ TreeBark Server

**TreeBark** is the HTTP sync server that exposes Trees over REST.

### Running TreeBark

```bash
cd AcornSyncServer
dotnet run
# Server starts on http://localhost:5000
```

### Server Setup

```csharp
// Program.cs in AcornSyncServer
var grove = new Grove();
grove.Plant(new Tree<User>(new FileTrunk<User>("data/users")));
grove.Plant(new Tree<Product>(new DocumentStoreTrunk<Product>("data/products")));

// TreeBark endpoints auto-register from the grove
```

### TreeBark REST API

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Health check + API docs |
| `/bark/{treeName}/stash` | POST | Stash a nut to remote tree |
| `/bark/{treeName}/crack/{id}` | GET | Crack a nut from remote tree |
| `/bark/{treeName}/toss/{id}` | DELETE | Toss a nut from remote tree |
| `/bark/{treeName}/export` | GET | Export all nuts from tree |
| `/bark/{treeName}/import` | POST | Import nuts into tree |

### Client-Server Example

**Server:**

```csharp
var grove = new Grove();
grove.Plant(new Tree<User>(new DocumentStoreTrunk<User>("data/users")));
// TreeBark running on http://localhost:5000
```

**Client 1:**

```csharp
var tree1 = new Tree<User>(new FileTrunk<User>("client1/users"));
var branch = new Branch("http://localhost:5000");

tree1.Stash("alice", new User { Name = "Alice" });
branch.TryPush("alice", tree1.Crack("alice")); // Syncs to server
```

**Client 2:**

```csharp
var tree2 = new Tree<User>(new MemoryTrunk<User>());
var branch = new Branch("http://localhost:5000");

await branch.ShakeAsync(tree2); // Pulls "alice" from server
var alice = tree2.Crack("alice"); // Now available locally
```

---

## 🌐 Same-Host Sync Strategies

For processes on the same host, you have **two simple options**:

### Option 1: Shared FileTrunk (Simplest ✅)

**Just point both trees to the same directory:**

```csharp
// Process 1
var tree1 = new Tree<User>(new FileTrunk<User>("shared/users"));
tree1.Stash(new User { Id = "alice", Name = "Alice" });

// Process 2
var tree2 = new Tree<User>(new FileTrunk<User>("shared/users"));
var alice = tree2.Crack("alice"); // ✅ Automatically synced!
```

**Pros:** Zero setup, no server needed, works immediately
**Cons:** Both processes need access to the same filesystem

---

### Option 2: In-Process Tree Entanglement

**Sync two trees directly without HTTP:**

```csharp
var tree1 = new Tree<User>();
var tree2 = new Tree<User>(new MemoryTrunk<User>());

tree1.Entangle(tree2); // 🪢 Direct sync

tree1.Stash(new User { Id = "bob", Name = "Bob" });
// Automatically pushed to tree2 via InProcessBranch
```

**Pros:** Real-time sync, works for in-memory scenarios
**Cons:** Only works within same process

---

## 🍃 Manual Shake

Force sync on-demand:

```csharp
tree.Shake(); // Pushes all local changes to connected branches
```

Under the hood, `Shake()`:
1. Calls `_trunk.ExportChanges()` to get all local nuts
2. Iterates through all registered branches
3. Calls `branch.TryPush(id, shell)` for each nut

---

## 📤 Export/Import

For manual sync or migration scenarios:

### Export Changes

```csharp
var changes = tree.ExportChanges();
foreach (var nut in changes)
{
    Console.WriteLine($"{nut.Id}: {nut.Payload}");
}
```

### Import Changes

```csharp
var sourceTrunk = new FileTrunk<User>("data/source");
var targetTrunk = new AzureTrunk<User>("connection-string");

var changes = sourceTrunk.ExportChanges();
targetTrunk.ImportChanges(changes);
```

---

## 🕸️ Sync Strategies Comparison

| Strategy | Use Case | Pros | Cons |
|----------|----------|------|------|
| **Shared FileTrunk** | Same-host processes | Simplest, no setup | Requires shared filesystem |
| **In-Process Entangle** | Same-process trees | Real-time, no network | Same-process only |
| **Branch + Manual Push** | On-demand sync | Full control | Requires manual calls |
| **Tangle (Auto-sync)** | Real-time remote sync | Automatic | Network overhead |
| **Grove.Oversee** | Multi-tree auto-sync | One-liner setup | Less granular control |
| **Export/Import** | Migrations, backups | Simple, portable | Manual, not real-time |

---

## 🧭 When to Use What

### Use **Shared FileTrunk** when:
- Multiple processes on the same host
- Both processes can access the same directory
- You want the simplest possible sync (zero setup)
- Building CLI tools with shared state

### Use **In-Process Entanglement** when:
- Syncing trees within the same process
- Working with in-memory scenarios
- You need real-time tree-to-tree sync without HTTP

### Use **Branch + TryPush** when:
- You want manual control over sync timing
- Network is unreliable (batch sync when available)
- Implementing custom sync logic

### Use **Tangle** when:
- You need real-time sync
- Auto-push on every stash is desired
- Building chat apps, collaborative tools, etc.

### Use **Grove.Oversee** when:
- Managing multiple Trees with the same sync endpoint
- You want auto-monitoring with minimal code
- Building multi-tree applications

### Use **Export/Import** when:
- Migrating between storage backends
- Creating backups
- Batch data transfers
- Offline sync scenarios

---

## 🔧 Advanced: Sync Statistics

Track sync activity with Tangle stats:

```csharp
var stats = grove.GetTangleStats();
foreach (var stat in stats)
{
    Console.WriteLine($"Tree: {stat.TreeType}");
    Console.WriteLine($"Remote: {stat.RemoteAddress}");
    Console.WriteLine($"Pushes: {stat.TotalPushes}");
    Console.WriteLine($"Pulls: {stat.TotalPulls}");
    Console.WriteLine($"Last Sync: {stat.LastSyncTime}");
}
```

---

## 🧭 Navigation

- **Previous:** [[Getting Started]] - Basic Tree operations
- **Next:** [[Conflict Resolution]] - Handle squabbles and resolve conflicts
- **Related:** [[Cluster & Mesh]] - UDP discovery and mesh networking

🌰 *Your nuts are now traveling the forest at light speed!*
