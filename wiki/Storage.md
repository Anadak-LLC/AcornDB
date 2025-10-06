# 🗄️ Storage & Trunks

AcornDB uses **Trunks** to abstract storage. Swap your backend without changing Tree code.

## ITrunk\<T\> Interface

```csharp
public interface ITrunk<T>
{
    // Core persistence operations
    void Save(string id, NutShell<T> shell);
    NutShell<T>? Load(string id);
    void Delete(string id);
    IEnumerable<NutShell<T>> LoadAll();

    // Optional: History support (time-travel)
    IReadOnlyList<NutShell<T>> GetHistory(string id);

    // Optional: Sync/Export support
    IEnumerable<NutShell<T>> ExportChanges();
    void ImportChanges(IEnumerable<NutShell<T>> incoming);
}
```

---

## 📁 FileTrunk\<T\>

Simple file-based storage with one file per nut.

### Characteristics

| Feature | Support |
|---------|---------|
| History | ❌ No |
| Sync | ✅ Yes |
| Durable | ✅ Yes |
| Async | ❌ No |

### Usage

```csharp
var trunk = new FileTrunk<User>("data/users");
var tree = new Tree<User>(trunk);

tree.Stash("alice", new User { Name = "Alice" });
// Creates: data/users/alice.json
```

### File Structure

```
data/users/
├── alice.json
├── bob.json
└── charlie.json
```

Each file contains a serialized `NutShell<T>`:

```json
{
  "Id": "alice",
  "Payload": {
    "Name": "Alice",
    "Email": "alice@woodland.io"
  },
  "Timestamp": "2025-10-06T12:00:00Z",
  "Version": 1
}
```

### Pros
- ✅ Simple to debug (human-readable files)
- ✅ Durable (survives restarts)
- ✅ Works with version control (Git-friendly)

### Cons
- ❌ No history/versioning
- ❌ Not optimized for large datasets
- ❌ No async support

---

## 💾 MemoryTrunk\<T\>

In-memory storage for fast, ephemeral data.

### Characteristics

| Feature | Support |
|---------|---------|
| History | ❌ No |
| Sync | ✅ Yes |
| Durable | ❌ No (lost on restart) |
| Async | ❌ No |

### Usage

```csharp
var trunk = new MemoryTrunk<User>();
var tree = new Tree<User>(trunk);

tree.Stash("alice", new User { Name = "Alice" });
// Stored in: Dictionary<string, NutShell<User>>
```

### Pros
- ✅ Blazing fast (no I/O)
- ✅ Perfect for tests
- ✅ No file system dependencies

### Cons
- ❌ Non-durable (data lost on restart)
- ❌ No history support
- ❌ Limited by RAM

---

## 📚 DocumentStoreTrunk\<T\>

Full versioning and time-travel with append-only change log.

### Characteristics

| Feature | Support |
|---------|---------|
| History | ✅ Yes |
| Sync | ✅ Yes |
| Durable | ✅ Yes |
| Async | ❌ No |

### Usage

```csharp
var trunk = new DocumentStoreTrunk<User>("data/users");
var tree = new Tree<User>(trunk);

tree.Stash("alice", new User { Name = "Alice v1" });
tree.Stash("alice", new User { Name = "Alice v2" });
tree.Stash("alice", new User { Name = "Alice v3" });

var current = tree.Crack("alice"); // "Alice v3"
var history = tree.GetHistory("alice"); // ["Alice v1", "Alice v2"]
```

### File Structure

```
data/users/
├── snapshot.json         # Latest state of all nuts
└── changes.log          # Append-only change log
```

**snapshot.json:**
```json
{
  "alice": {
    "Id": "alice",
    "Payload": { "Name": "Alice v3" },
    "Timestamp": "2025-10-06T12:00:00Z",
    "Version": 3
  }
}
```

**changes.log:**
```
{"Id":"alice","Payload":{"Name":"Alice v1"},"Timestamp":"...","Version":1}
{"Id":"alice","Payload":{"Name":"Alice v2"},"Timestamp":"...","Version":2}
{"Id":"alice","Payload":{"Name":"Alice v3"},"Timestamp":"...","Version":3}
```

### Time-Travel

```csharp
var history = tree.GetHistory("alice");
foreach (var version in history)
{
    Console.WriteLine($"{version.Version}: {version.Payload.Name}");
}
// Output:
// 1: Alice v1
// 2: Alice v2
```

### Compaction (Smush)

The change log grows over time. Use `SmushNow()` to compact:

```csharp
trunk.SmushNow(); // Compacts change log, keeps only latest versions
```

### Pros
- ✅ Full versioning and time-travel
- ✅ Append-only log (safe for concurrent writes)
- ✅ Undo support via `UndoSquabble()`

### Cons
- ❌ Change log grows unbounded (requires manual compaction)
- ❌ Slower than FileTrunk (due to logging)

---

## ☁️ AzureTrunk\<T\>

Azure Blob Storage backend for cloud scenarios.

### Characteristics

| Feature | Support |
|---------|---------|
| History | ❌ No |
| Sync | ✅ Yes |
| Durable | ✅ Yes |
| Async | ✅ Yes |

### Usage

```csharp
var connectionString = "DefaultEndpointsProtocol=https;...";
var trunk = new AzureTrunk<User>(connectionString);
var tree = new Tree<User>(trunk);

tree.Stash("alice", new User { Name = "Alice" });
// Uploads to: Azure Blob Storage
```

### Blob Structure

```
Container: acorndb
├── users/alice.json
├── users/bob.json
└── products/widget.json
```

### Pros
- ✅ Cloud-backed (highly durable)
- ✅ Async support
- ✅ Scalable storage

### Cons
- ❌ Requires Azure account
- ❌ Network latency
- ❌ No history support

---

## 🔍 Feature Detection with ITrunkCapabilities

Check trunk features **without exceptions**:

```csharp
var trunk = new MemoryTrunk<User>();
var caps = trunk.GetCapabilities();

Console.WriteLine($"Trunk: {caps.TrunkType}");       // "Memory"
Console.WriteLine($"History: {caps.SupportsHistory}"); // False
Console.WriteLine($"Sync: {caps.SupportsSync}");       // True
Console.WriteLine($"Durable: {caps.IsDurable}");       // False
Console.WriteLine($"Async: {caps.SupportsAsync}");     // False
```

### Extension Methods

```csharp
if (trunk.CanGetHistory())
{
    var history = trunk.GetHistory("alice");
}
else
{
    Console.WriteLine("This trunk doesn't support history");
}
```

### Capability Matrix

| Trunk | History | Sync | Durable | Async |
|-------|---------|------|---------|-------|
| **FileTrunk** | ❌ | ✅ | ✅ | ❌ |
| **MemoryTrunk** | ❌ | ✅ | ❌ | ❌ |
| **DocumentStoreTrunk** | ✅ | ✅ | ✅ | ❌ |
| **AzureTrunk** | ❌ | ✅ | ✅ | ✅ |

---

## 🔄 Export/Import Between Trunks

Migrate data between storage backends:

### Example: File → Azure

```csharp
var sourceTrunk = new FileTrunk<User>("data/users");
var targetTrunk = new AzureTrunk<User>("connection-string");

var changes = sourceTrunk.ExportChanges();
targetTrunk.ImportChanges(changes);

Console.WriteLine("Migration complete!");
```

### Example: Memory → DocumentStore

```csharp
var memoryTrunk = new MemoryTrunk<User>();
var docTrunk = new DocumentStoreTrunk<User>("data/users");

// Populate memory trunk
var memoryTree = new Tree<User>(memoryTrunk);
memoryTree.Stash("alice", new User { Name = "Alice" });
memoryTree.Stash("bob", new User { Name = "Bob" });

// Export and import
var data = memoryTrunk.ExportChanges();
docTrunk.ImportChanges(data);

// Now data is persisted with history
var docTree = new Tree<User>(docTrunk);
Console.WriteLine(docTree.NutCount); // 2
```

---

## 🛠️ Building a Custom Trunk

Implement `ITrunk<T>` for custom storage backends.

### Example: SQLite Trunk

```csharp
public class SqliteTrunk<T> : ITrunk<T>
{
    private readonly string _connectionString;

    public SqliteTrunk(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void Save(string id, NutShell<T> shell)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var json = JsonSerializer.Serialize(shell);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO nuts (id, data)
            VALUES (@id, @data)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@data", json);
        cmd.ExecuteNonQuery();
    }

    public NutShell<T>? Load(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT data FROM nuts WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var result = cmd.ExecuteScalar() as string;
        return result != null
            ? JsonSerializer.Deserialize<NutShell<T>>(result)
            : null;
    }

    public void Delete(string id) { /* ... */ }
    public IEnumerable<NutShell<T>> LoadAll() { /* ... */ }
    public IReadOnlyList<NutShell<T>> GetHistory(string id)
    {
        throw new NotSupportedException("SQLite trunk doesn't support history");
    }
    public IEnumerable<NutShell<T>> ExportChanges() { /* ... */ }
    public void ImportChanges(IEnumerable<NutShell<T>> incoming) { /* ... */ }
}
```

### Usage

```csharp
var trunk = new SqliteTrunk<User>("Data Source=acorn.db");
var tree = new Tree<User>(trunk);

tree.Stash("alice", new User { Name = "Alice" });
```

---

## 🧪 Testing with Different Trunks

### Parameterized Tests

```csharp
public static IEnumerable<object[]> TrunkTypes()
{
    yield return new object[] { new FileTrunk<User>("test-data") };
    yield return new object[] { new MemoryTrunk<User>() };
    yield return new object[] { new DocumentStoreTrunk<User>("test-data") };
}

[Theory]
[MemberData(nameof(TrunkTypes))]
public void Test_Stash_Works_WithAllTrunks(ITrunk<User> trunk)
{
    var tree = new Tree<User>(trunk);
    tree.Stash("alice", new User { Name = "Alice" });

    var result = tree.Crack("alice");
    Assert.Equal("Alice", result.Name);
}
```

---

## 🚨 NotSupportedException Pattern

Trunks that don't support a feature throw `NotSupportedException`:

```csharp
var memTrunk = new MemoryTrunk<User>();
var tree = new Tree<User>(memTrunk);

try
{
    var history = tree.GetHistory("alice");
}
catch (NotSupportedException)
{
    Console.WriteLine("MemoryTrunk doesn't support history!");
}
```

**Better:** Use capability detection instead:

```csharp
if (trunk.CanGetHistory())
{
    var history = tree.GetHistory("alice");
}
```

---

## 🧭 Choosing the Right Trunk

| Use Case | Recommended Trunk |
|----------|------------------|
| Development/Testing | `MemoryTrunk` |
| Simple persistence | `FileTrunk` |
| Versioning/Audit logs | `DocumentStoreTrunk` |
| Cloud storage | `AzureTrunk` |
| Production with history | `DocumentStoreTrunk` |
| High performance | `MemoryTrunk` + periodic export |
| Custom backend (SQL, Redis) | Build custom trunk |

---

## 🔮 Future Trunks

**Coming Soon:**

- **RedisTrunk** - Distributed cache backend
- **S3Trunk** - AWS S3 storage
- **PostgresTrunk** - PostgreSQL with JSONB
- **LiteDBTrunk** - Embedded NoSQL database
- **HybridTrunk** - Memory + File fallback

---

## 🧭 Navigation

- **Previous:** [[Conflict Resolution]] - Squabbles and custom judges
- **Next:** [[Cluster & Mesh]] - Multi-grove forests and mesh networking
- **Related:** [[Getting Started]] - Basic storage setup

🌰 *Choose your trunk wisely, for it holds the nuts of your kingdom!*
