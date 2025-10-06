# 🍁 Core Concepts

AcornDB uses woodland-themed metaphors for standard database concepts. This guide explains what each metaphor means and how it maps to traditional database terminology.

## The Woodland Lexicon

| AcornDB Term | Traditional Term | What It Is |
|--------------|------------------|------------|
| `Tree<T>` | Collection/Table | A container for documents of type T |
| `Nut<T>` | Document wrapper | An object wrapped with metadata (ID, timestamp, version, TTL) |
| `ITrunk<T>` | Storage backend | Persistence abstraction (file, memory, blob, versioned) |
| `Branch` | Remote connection | HTTP connection to a remote Tree |
| `Tangle` | Sync session | A live sync relationship between two Trees |
| `Grove` | Database/Cluster | A collection of Trees managed together |
| `Canopy` | Orchestrator | Internal sync coordinator (hidden from users) |
| `Stash` | Insert/Upsert | Store a nut in the tree |
| `Crack` | Read/Get | Retrieve a nut from the tree |
| `Toss` | Delete | Remove a nut from the tree |
| `Shake` | Sync | Force synchronization with remote branches |
| `Squabble` | Conflict | When two versions of the same nut compete |
| `Judge` | Conflict resolver | Logic that decides which nut wins a squabble |
| `Entangle` | Connect | Link a Tree to a remote Branch for syncing |
| `Oversee` | Auto-monitor | Automatically sync with a remote branch |
| `Smush` | Compact | Manual log compaction for DocumentStoreTrunk |

---

## 🌳 Tree\<T\>

The **Tree** is the heart of AcornDB — it's your collection/table equivalent.

### What it does:
- Stores documents (nuts) of type `T`
- Manages an in-memory cache + persistent storage
- Handles sync with remote branches
- Resolves conflicts via squabbles
- Tracks statistics (stashes, tosses, squabbles)

### Example:
```csharp
var userTree = new Tree<User>(new FileTrunk<User>("data/users"));
userTree.Stash("alice", new User { Name = "Alice" });
var alice = userTree.Crack("alice");
userTree.Toss("alice");
```

### Key Properties:
- `NutCount` - Number of nuts currently in the tree
- `GetNutStats()` - Returns statistics about tree operations

---

## 🥜 Nut\<T\>

A **Nut** wraps your data with metadata. It's like an envelope around your object.

### Structure:
```csharp
public class Nut<T>
{
    public string Id { get; set; }
    public T Payload { get; set; }  // Your actual data
    public DateTime Timestamp { get; set; }
    public DateTime? ExpiresAt { get; set; }  // Optional TTL
    public int Version { get; set; }
}
```

### Aliases:
- `Payload` = `Value` (both refer to the same data)
- **Note:** `NutShell<T>` still works for backwards compatibility but is deprecated

### Why it exists:
- **Versioning** - Track document versions over time
- **Timestamps** - Resolve conflicts with "last write wins"
- **TTL** - Auto-expire documents (future feature)
- **Sync metadata** - Carry version info across branches

### Example:
```csharp
var nut = new Nut<User>
{
    Id = "user-123",
    Payload = new User { Name = "Bob" },
    Timestamp = DateTime.UtcNow,
    Version = 1
};

// With auto-ID detection
tree.Stash(nut.Payload); // Uses nut.Payload.Id

// Or explicit
tree.Stash("user-123", nut.Payload);
```

---

## 🪵 ITrunk\<T\>

The **Trunk** is the storage abstraction layer. It's like an ORM, but nutty.

### Available Trunks:

#### 1. **FileTrunk\<T\>**
- Simple file-based storage
- One file per nut
- No history support
- Durable, no async

```csharp
var trunk = new FileTrunk<User>("data/users");
```

#### 2. **MemoryTrunk\<T\>**
- In-memory only (fast, non-durable)
- Great for tests
- No history support
- Not persistent

```csharp
var trunk = new MemoryTrunk<User>();
```

#### 3. **DocumentStoreTrunk\<T\>**
- Full versioning and time-travel
- Append-only change log
- History support via `GetHistory(id)`
- Compaction via `SmushNow()`

```csharp
var trunk = new DocumentStoreTrunk<User>("data/users");
var history = trunk.GetHistory("alice"); // Get previous versions
```

#### 4. **AzureTrunk\<T\>**
- Azure Blob Storage backend
- Durable, async support
- No history support
- Good for cloud scenarios

```csharp
var trunk = new AzureTrunk<User>("connection-string");
```

### ITrunk Interface:
```csharp
public interface ITrunk<T>
{
    void Save(string id, NutShell<T> shell);
    NutShell<T>? Load(string id);
    void Delete(string id);
    IEnumerable<NutShell<T>> LoadAll();

    // Optional - may throw NotSupportedException
    IReadOnlyList<NutShell<T>> GetHistory(string id);
    IEnumerable<NutShell<T>> ExportChanges();
    void ImportChanges(IEnumerable<NutShell<T>> incoming);
}
```

---

## 🌉 Branch

A **Branch** represents a remote HTTP connection to another Tree (usually via TreeBark server).

### What it does:
- Pushes nuts to a remote endpoint
- Pulls nuts from a remote endpoint
- Wraps HTTP client for REST sync

### Example:
```csharp
var branch = new Branch("http://sync-server:5000");
branch.TryPush("alice", aliceShell);
await branch.ShakeAsync(localTree); // Pull all remote changes
```

### TreeBark Endpoints:
- `/bark/{treeName}/stash` - Push a nut
- `/bark/{treeName}/crack/{id}` - Get a nut
- `/bark/{treeName}/export` - Export all nuts
- `/bark/{treeName}/import` - Import nuts

---

## 🪢 Tangle

A **Tangle** is a live sync session between a local Tree and a remote Branch.

### What it does:
- Automatically pushes changes when you stash
- Tracks sync statistics (pushes, pulls, last sync time)
- Registered on the Tree via `RegisterTangle()`

### Example:
```csharp
var tangle = new Tangle<User>(localTree, remoteBranch, "sync-session-1");
localTree.Stash("bob", new User { Name = "Bob" }); // Auto-pushed to branch
```

### Properties:
- `PushUpdate(key, item)` - Manually push a nut
- `PushDelete(key)` - Push a delete (not fully implemented)
- `PushAll(tree)` - Push all nuts from a tree

---

## 🌲 Grove

A **Grove** is a collection of Trees managed together. Think of it as your database instance.

### What it does:
- Plants multiple Trees of different types
- Manages entanglements (Tangles) across Trees
- Provides grove-wide statistics
- Enables type-safe Tree retrieval

### Example:
```csharp
var grove = new Grove();
grove.Plant(new Tree<User>(new FileTrunk<User>("data/users")));
grove.Plant(new Tree<Product>(new FileTrunk<Product>("data/products")));

var userTree = grove.GetTree<User>();
```

### Key Methods:
- `Plant<T>(tree)` - Add a Tree to the grove
- `GetTree<T>()` - Retrieve a Tree by type
- `Entangle<T>(branch, id)` - Create a Tangle for a Tree
- `Oversee<T>(branch, id)` - Auto-monitor a branch
- `ShakeAll()` - Sync all tangles

---

## 🍃 Verbs (Operations)

### Stash
Store a nut in the tree (insert/upsert).

```csharp
tree.Stash("key", new User { Name = "Alice" });
```

### Crack
Retrieve a nut from the tree (read/get).

```csharp
var user = tree.Crack("key");
```

### Toss
Remove a nut from the tree (delete).

```csharp
tree.Toss("key");
```

### Shake
Force synchronization with remote branches.

```csharp
tree.Shake(); // Pushes local changes to all branches
```

### Squabble
Resolve a conflict between local and incoming nuts.

```csharp
tree.Squabble("key", incomingShell); // Timestamp comparison wins
```

### Entangle
Connect a Tree to a remote Branch for syncing.

```csharp
tree.Entangle(branch);
```

### Oversee
Auto-monitor a branch (entangle + auto-sync).

```csharp
grove.Oversee<User>(branch, "sync-id");
```

---

## 📊 Stats & Monitoring

Every Tree tracks statistics:

```csharp
var stats = tree.GetNutStats();
// stats.TotalStashed
// stats.TotalTossed
// stats.SquabblesResolved
// stats.SmushesPerformed
// stats.ActiveTangles
```

Groves provide aggregate stats:

```csharp
var groveStats = grove.GetNutStats();
// groveStats.TotalTrees
// groveStats.TotalStashed
// groveStats.TotalSquabbles
```

---

## 🧭 Navigation

- **Next:** [[Getting Started]] - Install and build your first Tree
- **Advanced:** [[Data Sync]] - Entangling, Branches, and mesh networks
- **Internals:** [[Storage]] - Deep dive into Trunk implementations

🌰 *Now that you speak squirrel, let's get coding!*
