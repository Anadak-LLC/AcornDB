# 🚀 Getting Started

Get up and running with AcornDB in under 5 minutes.

## Installation

### Via NuGet (Coming Soon)

```bash
dotnet add package AcornDB
```

### From Source

```bash
git clone https://github.com/Anadak-LLC/AcornDB.git
cd AcornDB
dotnet build
```

Add a project reference:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/AcornDB/AcornDB.csproj" />
</ItemGroup>
```

---

## Your First Tree

### 1. Create a Model

```csharp
public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString(); // Auto-detected!
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**Pro tip:** Add an `Id` or `Key` property for auto-ID detection!

### 2. Initialize a Tree

```csharp
using AcornDB;

// Simplest possible - defaults to FileTrunk
var userTree = new Tree<User>();

// Or specify a custom path
var userTree = new Tree<User>(new FileTrunk<User>("data/users"));
```

**Note:** Trunk parameter is **optional** and defaults to `FileTrunk<T>()`.

### 3. Stash a Nut (Insert)

```csharp
var alice = new User
{
    Name = "Alice",
    Email = "alice@woodland.io"
};

// Auto-detects ID from alice.Id property
userTree.Stash(alice); // 🎉 No explicit ID needed!

// Or use explicit ID if preferred
userTree.Stash("alice", alice);
```

### 4. Crack a Nut (Read)

```csharp
var retrievedUser = userTree.Crack("alice");
Console.WriteLine($"Found: {retrievedUser.Name}");
// Output: Found: Alice
```

### 5. Toss a Nut (Delete)

```csharp
userTree.Toss("alice");
// Console: Nut 'alice' tossed from data/users
```

---

## Storage Options

AcornDB uses **Trunks** to abstract storage. Choose the right trunk for your use case:

### File-Based (Simple)

```csharp
var trunk = new FileTrunk<User>("data/users");
var tree = new Tree<User>(trunk);
```

- ✅ Durable (survives restarts)
- ❌ No history support
- 📁 One file per nut

### In-Memory (Fast)

```csharp
var trunk = new MemoryTrunk<User>();
var tree = new Tree<User>(trunk);
```

- ✅ Blazing fast
- ❌ Non-durable (lost on restart)
- 🧪 Perfect for tests

### DocumentStore (Versioned)

```csharp
var trunk = new DocumentStoreTrunk<User>("data/users");
var tree = new Tree<User>(trunk);
```

- ✅ Full history and time-travel
- ✅ Append-only change log
- 🕰️ Call `tree.GetHistory("id")` for versions

### Azure Blob (Cloud)

```csharp
var trunk = new AzureTrunk<User>("BlobEndpoint=https://...");
var tree = new Tree<User>(trunk);
```

- ✅ Cloud-backed
- ✅ Async support
- ☁️ Good for distributed systems

---

## Working with a Grove

A **Grove** manages multiple Trees together.

### 1. Create a Grove

```csharp
using AcornDB.Models;

var grove = new Grove();
```

### 2. Plant Trees

```csharp
grove.Plant(new Tree<User>(new FileTrunk<User>("data/users")));
grove.Plant(new Tree<Product>(new FileTrunk<Product>("data/products")));
```

### 3. Retrieve a Tree

```csharp
var userTree = grove.GetTree<User>();
userTree.Stash("bob", new User { Name = "Bob" });
```

### 4. Get Grove Stats

```csharp
var stats = grove.GetNutStats();
Console.WriteLine($"Total trees: {stats.TotalTrees}");
Console.WriteLine($"Total nuts stashed: {stats.TotalStashed}");
```

---

## Basic Operations

### Checking if a Nut Exists

```csharp
var user = tree.Crack("alice");
if (user != null)
{
    Console.WriteLine("Alice exists!");
}
```

### Listing All Nuts

```csharp
var allNuts = tree.ExportChanges();
foreach (var shell in allNuts)
{
    Console.WriteLine($"{shell.Id}: {shell.Payload.Name}");
}
```

### Counting Nuts

```csharp
Console.WriteLine($"Tree has {tree.NutCount} nuts");
```

---

## Time-Travel with History

Only available with **DocumentStoreTrunk**.

### 1. Stash Multiple Versions

```csharp
var trunk = new DocumentStoreTrunk<User>("data/users");
var tree = new Tree<User>(trunk);

tree.Stash("charlie", new User { Name = "Charlie v1" });
tree.Stash("charlie", new User { Name = "Charlie v2" });
tree.Stash("charlie", new User { Name = "Charlie v3" });
```

### 2. Get Current Version

```csharp
var current = tree.Crack("charlie");
Console.WriteLine(current.Name); // "Charlie v3"
```

### 3. Access History

```csharp
var history = tree.GetHistory("charlie");
foreach (var version in history)
{
    Console.WriteLine($"Version from {version.Timestamp}: {version.Payload.Name}");
}
// Output:
// Version from ...: Charlie v1
// Version from ...: Charlie v2
```

### 4. Undo Last Change

```csharp
tree.UndoSquabble("charlie"); // Reverts to Charlie v2
```

---

## Feature Detection

Check trunk capabilities before using advanced features:

```csharp
var trunk = new MemoryTrunk<User>();
var caps = trunk.GetCapabilities();

if (caps.SupportsHistory)
{
    var history = tree.GetHistory("alice");
}
else
{
    Console.WriteLine("This trunk doesn't support history");
}
```

**Capability Matrix:**

| Trunk | History | Sync | Durable | Async |
|-------|---------|------|---------|-------|
| FileTrunk | ❌ | ✅ | ✅ | ❌ |
| MemoryTrunk | ❌ | ✅ | ❌ | ❌ |
| DocumentStoreTrunk | ✅ | ✅ | ✅ | ❌ |
| AzureTrunk | ❌ | ✅ | ✅ | ✅ |

---

## Using INutment for Typed IDs

The `INutment<TKey>` interface provides strongly-typed IDs.

### 1. Implement INutment

```csharp
public class User : INutment<string>
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }
}
```

### 2. Stash Without Explicit Key

```csharp
var user = new User { Name = "Dave" };
tree.Stash(user.Id, user); // ID from the object itself
```

---

## Running the Demo

AcornDB includes a demo project showcasing all features:

```bash
cd AcornDB.Demo
dotnet run
```

The demo covers:
- Basic Tree operations (stash, crack, toss)
- Grove management
- Storage options
- Sync scenarios
- Conflict resolution

---

## Next Steps

You now know how to:
- ✅ Create and manage Trees
- ✅ Choose the right Trunk
- ✅ Work with Groves
- ✅ Use versioned storage

**What's Next?**

- [[Data Sync]] - Set up remote syncing with Branches and Tangles
- [[Events]] - Subscribe to change notifications
- [[Storage]] - Deep dive into Trunk implementations
- [[Conflict Resolution]] - Handle squabbles like a pro

🌰 *You're ready to start stashing! Now go build something nutty.*
