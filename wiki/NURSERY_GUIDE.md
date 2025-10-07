# 🌱 AcornDB Nursery - Where Trunks Grow

The **Nursery** is where you discover and grow different trunk types for AcornDB! Instead of hardcoding storage backends, you can:

- **Browse** available trunk types at runtime
- **Grow** trunks by name from configuration
- **Plant** custom trunk implementations
- **Discover** trunk capabilities before creation

---

## 🎯 Why Use the Nursery?

### Without Nursery (Hardcoded)
```csharp
// Tightly coupled to specific implementations
var tree = new Acorn<User>()
    .WithTrunk(new FileTrunk<User>("./data"))
    .Sprout();

// No way to dynamically choose trunk at runtime
// Can't query what trunks are available
// Difficult to add custom trunks via plugins
```

### With Nursery (Dynamic)
```csharp
// Grow trunk dynamically from configuration
var tree = new Acorn<User>()
    .WithTrunkFromNursery("file", new() { { "path", "./data" } })
    .Sprout();

// Browse the nursery catalog
Console.WriteLine(Nursery.GetCatalog());

// Plant custom trunks
Nursery.Plant(new MyCustomTrunkFactory());
```

---

## 📦 Built-in Trunks

The nursery comes pre-planted with these trunk types:

| Type ID  | Display Name           | Category | Durable | History | Async | Sync |
|----------|------------------------|----------|---------|---------|-------|------|
| `file`   | File Trunk             | Local    | ✅      | ❌      | ❌    | ✅   |
| `memory` | Memory Trunk           | Local    | ❌      | ❌      | ❌    | ✅   |
| `docstore` | Document Store Trunk | Local    | ✅      | ✅      | ❌    | ✅   |
| `git`    | GitHub Trunk           | Git      | ✅      | ✅      | ❌    | ✅   |
| `azure`  | Azure Trunk            | Cloud    | ✅      | ❌      | ✅    | ✅   |

---

## 🚀 Quick Start

### 1. Browse the Nursery Catalog

```csharp
using AcornDB.Storage;

// Get all trunks in the nursery
var trunks = Nursery.GetAllMetadata();

foreach (var trunk in trunks)
{
    Console.WriteLine($"\n[{trunk.Category}] {trunk.TypeId}");
    Console.WriteLine($"  Name: {trunk.DisplayName}");
    Console.WriteLine($"  Description: {trunk.Description}");
    Console.WriteLine($"  Durable: {trunk.Capabilities.IsDurable}");
    Console.WriteLine($"  History: {trunk.Capabilities.SupportsHistory}");
}

// Or get the formatted catalog
Console.WriteLine(Nursery.GetCatalog());
```

**Output:**
```
🌳 Nursery Catalog - Available Trunk Types:

[Local] file - File Trunk
  Stores nuts as JSON files in a local folder. Simple and human-readable.
  Durable: True, History: False, Sync: True, Async: False

[Local] memory - Memory Trunk
  In-memory storage for testing. Non-durable, fast, no history.
  Durable: False, History: False, Sync: True, Async: False

[Local] docstore - Document Store Trunk
  Full-featured trunk with append-only logging, versioning, and time-travel.
  Durable: True, History: True, Sync: True, Async: False

[Git] git - GitHub Trunk
  Git-based storage where every Stash() creates a commit.
  Durable: True, History: True, Sync: True, Async: False

[Cloud] azure - Azure Trunk
  Azure Blob Storage trunk for cloud persistence.
  Durable: True, History: False, Sync: True, Async: True
```

### 2. Grow Trunk from Nursery

```csharp
using AcornDB;
using AcornDB.Storage;

public class User
{
    public string Id { get; set; }
    public string Name { get; set; }
}

// Grow a file trunk from the nursery
var tree = new Acorn<User>()
    .WithTrunkFromNursery("file", new Dictionary<string, object>
    {
        { "path", "./my_users" }
    })
    .Sprout();

// Use it!
tree.Stash(new User { Id = "alice", Name = "Alice" });
var alice = tree.Crack("alice");
```

### 3. Configuration-Driven Trunk Selection

```csharp
// Read trunk type from config file
var config = new Dictionary<string, object>
{
    { "trunkType", "docstore" },
    { "path", "./production_data" }
};

var trunkType = config["trunkType"].ToString();
config.Remove("trunkType"); // Remove meta-key

var tree = new Acorn<User>()
    .WithTrunkFromNursery(trunkType, config)
    .Sprout();
```

---

## 🔍 Querying the Nursery

### Check if Trunk is Available

```csharp
if (Nursery.HasTrunk("s3"))
{
    Console.WriteLine("S3 trunk is available!");
}
else
{
    Console.WriteLine("S3 trunk not found. Install AcornDB.Persistence.Cloud package.");
}
```

### Query by Category

```csharp
// Find all cloud-based trunks
var cloudTrunks = Nursery.GetByCategory("Cloud");

foreach (var trunk in cloudTrunks)
{
    Console.WriteLine($"{trunk.TypeId}: {trunk.DisplayName}");
}
// Output: azure: Azure Trunk
```

### Query by Capability

```csharp
// Find all trunks that support history
var historyTrunks = Nursery.GetByCapability(caps => caps.SupportsHistory);

foreach (var trunk in historyTrunks)
{
    Console.WriteLine($"{trunk.TypeId} supports history!");
}
// Output:
// docstore supports history!
// git supports history!
```

### Get Metadata for Specific Trunk

```csharp
var metadata = Nursery.GetMetadata("docstore");

if (metadata != null)
{
    Console.WriteLine($"Required config keys: {string.Join(", ", metadata.RequiredConfigKeys)}");
    Console.WriteLine($"Optional config keys: {string.Join(", ", metadata.OptionalConfigKeys.Keys)}");
}
```

---

## 📚 Configuration Keys

Each trunk type has different configuration requirements:

### File Trunk (`file`)
- **Required:** None
- **Optional:**
  - `path` (string) - Storage folder path. Default: `./data/{TypeName}`

```csharp
var tree = new Acorn<User>()
    .WithTrunkFromNursery("file", new() { { "path", "./my_data" } })
    .Sprout();
```

### Memory Trunk (`memory`)
- **Required:** None
- **Optional:** None

```csharp
var tree = new Acorn<User>()
    .WithTrunkFromNursery("memory")
    .Sprout();
```

### DocumentStore Trunk (`docstore`)
- **Required:** None
- **Optional:**
  - `path` (string) - Storage folder path. Default: `./data/docstore/{TypeName}`

```csharp
var tree = new Acorn<User>()
    .WithTrunkFromNursery("docstore", new() { { "path", "./versioned_data" } })
    .Sprout();
```

### Git Trunk (`git`)
- **Required:** None
- **Optional:**
  - `repoPath` (string) - Git repository path. Default: `./acorndb_git_{TypeName}`
  - `authorName` (string) - Git author name. Default: `"AcornDB"`
  - `authorEmail` (string) - Git author email. Default: `"acorn@acorndb.dev"`
  - `autoPush` (bool) - Auto-push to remote. Default: `false`

```csharp
var tree = new Acorn<User>()
    .WithTrunkFromNursery("git", new()
    {
        { "repoPath", "./my_repo" },
        { "authorName", "Alice" },
        { "authorEmail", "alice@example.com" },
        { "autoPush", true }
    })
    .Sprout();
```

### Azure Trunk (`azure`)
- **Required:**
  - `connectionString` (string) - Azure Storage connection string
  - `containerName` (string) - Blob container name
- **Optional:** None

```csharp
var tree = new Acorn<User>()
    .WithTrunkFromNursery("azure", new()
    {
        { "connectionString", "DefaultEndpointsProtocol=https;AccountName=..." },
        { "containerName", "acorndb" }
    })
    .Sprout();
```

---

## 🔧 Planting Custom Trunks

### 1. Implement ITrunkFactory

```csharp
using AcornDB.Storage;
using System;
using System.Collections.Generic;

public class RedisTrunkFactory : ITrunkFactory
{
    public ITrunk<object> Create(Type itemType, Dictionary<string, object> configuration)
    {
        var connectionString = configuration["connectionString"].ToString()!;
        var prefix = configuration.TryGetValue("prefix", out var p) ? p?.ToString() : null;

        // Use reflection to create generic RedisTrunk<T>
        var trunkType = typeof(RedisTrunk<>).MakeGenericType(itemType);
        var trunk = Activator.CreateInstance(trunkType, connectionString, prefix);
        return (ITrunk<object>)trunk!;
    }

    public TrunkMetadata GetMetadata()
    {
        return new TrunkMetadata
        {
            TypeId = "redis",
            DisplayName = "Redis Trunk",
            Description = "Redis-backed storage for high-performance caching and persistence.",
            Capabilities = new TrunkCapabilities
            {
                SupportsHistory = false,
                SupportsSync = true,
                IsDurable = true,
                SupportsAsync = true,
                TrunkType = "RedisTrunk"
            },
            RequiredConfigKeys = new List<string> { "connectionString" },
            OptionalConfigKeys = new Dictionary<string, object>
            {
                { "prefix", "acorndb:" }
            },
            IsBuiltIn = false,
            Category = "Database"
        };
    }

    public bool ValidateConfiguration(Dictionary<string, object> configuration)
    {
        return configuration.ContainsKey("connectionString")
            && configuration["connectionString"] != null;
    }
}
```

### 2. Plant Your Factory in the Nursery

```csharp
// Plant at application startup
Nursery.Plant(new RedisTrunkFactory());

// Now it's available!
var tree = new Acorn<User>()
    .WithTrunkFromNursery("redis", new()
    {
        { "connectionString", "localhost:6379" },
        { "prefix", "users:" }
    })
    .Sprout();
```

### 3. Automatic Discovery (Plugin Systems)

```csharp
// For plugin architectures, scan assemblies and auto-plant
var factoryTypes = Assembly.GetExecutingAssembly()
    .GetTypes()
    .Where(t => typeof(ITrunkFactory).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

foreach (var type in factoryTypes)
{
    var factory = (ITrunkFactory)Activator.CreateInstance(type)!;
    Nursery.Plant(factory);
}
```

---

## 🎛️ Advanced Usage

### Environment-Based Trunk Selection

```csharp
public static Tree<T> CreateTree<T>()
{
    var environment = Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "development";

    return environment switch
    {
        "development" => new Acorn<T>().WithTrunkFromNursery("memory").Sprout(),
        "staging" => new Acorn<T>().WithTrunkFromNursery("file", new() { { "path", "./staging" } }).Sprout(),
        "production" => new Acorn<T>().WithTrunkFromNursery("docstore", new() { { "path", "/var/acorndb" } }).Sprout(),
        _ => throw new InvalidOperationException($"Unknown environment: {environment}")
    };
}
```

### Configuration File Driven

**appsettings.json:**
```json
{
  "AcornDB": {
    "TrunkType": "docstore",
    "Configuration": {
      "path": "./data/production"
    }
  }
}
```

**Code:**
```csharp
var config = Configuration.GetSection("AcornDB");
var trunkType = config["TrunkType"];
var trunkConfig = config.GetSection("Configuration")
    .GetChildren()
    .ToDictionary(x => x.Key, x => (object)x.Value!);

var tree = new Acorn<User>()
    .WithTrunkFromNursery(trunkType, trunkConfig)
    .Sprout();
```

### User-Selectable Storage

```csharp
Console.WriteLine("Select storage backend:");
Console.WriteLine("1. File (Local)");
Console.WriteLine("2. Memory (Fast, non-durable)");
Console.WriteLine("3. Git (Version control)");
Console.WriteLine("4. DocumentStore (Full history)");

var choice = Console.ReadLine();

var trunkType = choice switch
{
    "1" => "file",
    "2" => "memory",
    "3" => "git",
    "4" => "docstore",
    _ => "file"
};

var tree = new Acorn<User>()
    .WithTrunkFromNursery(trunkType)
    .Sprout();
```

---

## 🧪 Testing with the Nursery

### Clear Nursery (Testing Only)

```csharp
[TestMethod]
public void TestCustomTrunk()
{
    // Clear nursery for isolated test
    Nursery.Clear();

    // Plant only your custom trunk
    Nursery.Plant(new MockTrunkFactory());

    // Test trunk creation
    var tree = new Acorn<User>()
        .WithTrunkFromNursery("mock")
        .Sprout();

    Assert.IsNotNull(tree);
}
```

### Mock Trunk Factory

```csharp
public class MockTrunkFactory : ITrunkFactory
{
    public ITrunk<object> Create(Type itemType, Dictionary<string, object> configuration)
    {
        return new MemoryTrunk<object>();
    }

    public TrunkMetadata GetMetadata()
    {
        return new TrunkMetadata
        {
            TypeId = "mock",
            DisplayName = "Mock Trunk",
            Description = "Mock trunk for testing",
            Category = "Testing"
        };
    }

    public bool ValidateConfiguration(Dictionary<string, object> configuration) => true;
}
```

---

## 📖 Summary

The **Nursery** provides:

✅ **Dynamic Discovery** - Browse available trunk types at runtime
✅ **Configuration-Driven** - Grow trunks from config files/environment variables
✅ **Plugin Support** - Plant custom trunk implementations
✅ **Capability Queries** - Filter trunks by capabilities before creation
✅ **Metadata Inspection** - View required/optional configuration keys
✅ **Factory Pattern** - Clean separation between trunk creation and usage

**Example Use Cases:**
- Multi-tenant apps with per-tenant storage backends
- Environment-specific storage (dev/staging/prod)
- User-configurable persistence options
- Plugin architectures with custom storage providers
- Testing with mock/in-memory trunks

---

**Happy trunk growing!** 🌱🌳
