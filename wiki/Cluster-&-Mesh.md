# 🕸️ Cluster & Mesh Networking

AcornDB supports multi-grove clustering and mesh networking for building distributed systems with automatic peer discovery.

## Architecture Overview

```
┌─────────────┐         ┌─────────────┐         ┌─────────────┐
│   Grove 1   │◄───────►│   Grove 2   │◄───────►│   Grove 3   │
│  (Server)   │  HTTP   │ (Desktop)   │  UDP    │  (IoT)      │
└─────────────┘         └─────────────┘         └─────────────┘
      ▲                        ▲                        ▲
      │                        │                        │
  TreeBark               AcornBroadcaster        File P2P
```

---

## 🌲 Multi-Grove Forest

A **Forest** is a collection of Groves that sync with each other.

### Concept

- **Grove**: A single database instance with multiple Trees
- **Forest**: Multiple Groves connected via Branches and Tangles
- **Mesh**: Groves that auto-discover and entangle via UDP broadcast

---

## 📡 UDP-Based Discovery

AcornDB uses **UDP broadcast** for automatic peer discovery on the same network.

### How It Works

1. **Broadcaster** sends UDP packets: `"ACORN:{port}"` every 5 seconds
2. **Listener** receives packets and auto-entangles with discovered peers
3. Groves automatically sync once entangled

### AcornBroadcaster

```csharp
public class AcornBroadcaster
{
    private const int DiscoveryPort = 50505;
    private readonly string _message;

    public AcornBroadcaster(int hardwoodPort)
    {
        _message = $"ACORN:{hardwoodPort}";
    }

    public void StartBroadcast()
    {
        Task.Run(async () =>
        {
            var udp = new UdpClient();
            var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            var data = Encoding.UTF8.GetBytes(_message);

            while (true)
            {
                await udp.SendAsync(data, data.Length, endpoint);
                await Task.Delay(5000); // Every 5 seconds
            }
        });
    }
}
```

### Listener & Auto-Entangle

```csharp
public static async Task ListenAndEntangle(Grove grove)
{
    var udpClient = new UdpClient(DiscoveryPort);
    while (true)
    {
        var result = await udpClient.ReceiveAsync();
        var msg = Encoding.UTF8.GetString(result.Buffer);

        if (msg.StartsWith("ACORN:"))
        {
            var port = msg.Split(":")[1];
            var remoteUrl = $"http://{result.RemoteEndPoint.Address}:{port}";
            grove.EntangleAll(remoteUrl);
        }
    }
}
```

---

## 🛰️ Server Setup with Discovery

### TreeBark Server with Broadcasting

```csharp
var grove = new Grove();
grove.Plant(new Tree<User>(new FileTrunk<User>("data/users")));

// Start TreeBark HTTP server
var builder = WebApplication.CreateBuilder();
builder.Services.AddSingleton(grove);
var app = builder.Build();
app.MapControllers();
app.Run("http://0.0.0.0:5000");

// Start UDP broadcaster
var broadcaster = new AcornBroadcaster(5000);
broadcaster.StartBroadcast();
// Now broadcasts "ACORN:5000" on UDP port 50505
```

### Client with Auto-Discovery

```csharp
var grove = new Grove();
grove.Plant(new Tree<User>(new MemoryTrunk<User>()));

// Listen for broadcasted servers
await AcornBroadcaster.ListenAndEntangle(grove);
// Auto-entangles with any discovered TreeBark server
```

---

## 🌐 Multi-Grove Sync Scenarios

### 1. Hub-and-Spoke (Star Topology)

```
        Server (Hub)
           ▲
           │
    ┌──────┼──────┐
    │      │      │
Client 1  Client 2  Client 3
```

**Setup:**

**Server:**
```csharp
var serverGrove = new Grove();
serverGrove.Plant(new Tree<User>(new DocumentStoreTrunk<User>("data/users")));

// Run TreeBark on http://0.0.0.0:5000
var broadcaster = new AcornBroadcaster(5000);
broadcaster.StartBroadcast();
```

**Clients:**
```csharp
var clientGrove = new Grove();
clientGrove.Plant(new Tree<User>(new FileTrunk<User>("client-data")));

var branch = new Branch("http://server:5000");
clientGrove.Entangle<User>(branch, "sync-id");
```

---

### 2. Mesh (Peer-to-Peer)

```
Grove 1 ◄──► Grove 2
   ▲            ▲
   │            │
   └────────────┘
        ▼
     Grove 3
```

**Setup:**

Each grove broadcasts and listens:

```csharp
var grove = new Grove();
grove.Plant(new Tree<User>(new FileTrunk<User>("data/users")));

// Broadcast presence
var broadcaster = new AcornBroadcaster(port);
broadcaster.StartBroadcast();

// Listen for peers
await AcornBroadcaster.ListenAndEntangle(grove);
```

All groves automatically discover and entangle with each other.

---

### 3. Hierarchical (Multi-Tier)

```
     Central Server
           ▲
           │
      Regional Servers
         ▲   ▲
         │   │
    Edge Devices
```

**Setup:**

**Central:**
```csharp
var central = new Grove();
central.Plant(new Tree<User>(new DocumentStoreTrunk<User>("data/central")));
// Run TreeBark on port 5000
```

**Regional:**
```csharp
var regional = new Grove();
regional.Plant(new Tree<User>(new FileTrunk<User>("data/regional")));

// Connect to central
regional.Entangle<User>(new Branch("http://central:5000"), "central-sync");

// Run TreeBark for edge devices on port 6000
```

**Edge:**
```csharp
var edge = new Grove();
edge.Plant(new Tree<User>(new MemoryTrunk<User>()));

// Connect to regional
edge.Entangle<User>(new Branch("http://regional:6000"), "edge-sync");
```

---

## 🔄 EntangleAll (Multi-Tree Sync)

The `EntangleAll()` method entangles **all Trees** in a Grove with a remote endpoint.

### Usage

```csharp
var grove = new Grove();
grove.Plant(new Tree<User>(new FileTrunk<User>("data/users")));
grove.Plant(new Tree<Product>(new FileTrunk<Product>("data/products")));
grove.Plant(new Tree<Order>(new FileTrunk<Order>("data/orders")));

grove.EntangleAll("http://sync-server:5000");
// All 3 trees now auto-sync with the server
```

### Implementation (Conceptual)

```csharp
public void EntangleAll(string remoteUrl)
{
    foreach (var kvp in _trees)
    {
        var treeType = kvp.Value.GetType().GenericTypeArguments[0];
        var branch = new Branch(remoteUrl);

        // Dynamically entangle based on tree type
        var entangleMethod = typeof(Grove)
            .GetMethod("Entangle")
            .MakeGenericMethod(treeType);

        entangleMethod.Invoke(this, new object[] { branch, $"auto-{treeType.Name}" });
    }
}
```

---

## 📊 Tangle Statistics

Track sync activity across all tangles in a grove:

```csharp
var stats = grove.GetTangleStats();

foreach (var stat in stats)
{
    Console.WriteLine($"Tree: {stat.TreeType}");
    Console.WriteLine($"Remote: {stat.RemoteAddress}");
    Console.WriteLine($"Pushes: {stat.TotalPushes}");
    Console.WriteLine($"Pulls: {stat.TotalPulls}");
    Console.WriteLine($"Last Sync: {stat.LastSyncTime}");
    Console.WriteLine($"Status: {stat.Status}");
}
```

---

## 🛡️ Security Considerations (Future)

**Totem-Based Auth (Coming Soon):**

```csharp
var branch = new Branch("http://server:5000");
branch.Authenticate(new Totem
{
    CritterId = "squirrel-123",
    BarkCode = "abc123xyz"
});

grove.Entangle<User>(branch, "secure-sync");
```

**Planned Security Features:**

- **Totems** - Authentication tokens
- **Critters** - User identities
- **BarkCodes** - API keys
- **ForageRights** - Permission system

---

## 🏗️ Building a Distributed System

### Example: IoT Sensor Network

**Central Server:**

```csharp
var central = new Grove();
central.Plant(new Tree<SensorReading>(new DocumentStoreTrunk<SensorReading>("data/readings")));

// TreeBark on port 5000
var broadcaster = new AcornBroadcaster(5000);
broadcaster.StartBroadcast();
```

**Edge Sensor:**

```csharp
var edge = new Grove();
edge.Plant(new Tree<SensorReading>(new FileTrunk<SensorReading>("data/local")));

// Auto-discover central server
await AcornBroadcaster.ListenAndEntangle(edge);

// Push sensor data
var tree = edge.GetTree<SensorReading>();
tree.Stash($"reading-{DateTime.UtcNow.Ticks}", new SensorReading
{
    Temperature = 22.5,
    Humidity = 60.0
});
// Auto-syncs to central server via tangle
```

---

## 🧪 Testing Mesh Networks

### Simulate Multiple Groves

```csharp
[Fact]
public async Task Test_MeshSync_AcrossThreeGroves()
{
    // Grove 1
    var grove1 = new Grove();
    grove1.Plant(new Tree<User>(new MemoryTrunk<User>()));

    // Grove 2
    var grove2 = new Grove();
    grove2.Plant(new Tree<User>(new MemoryTrunk<User>()));

    // Grove 3
    var grove3 = new Grove();
    grove3.Plant(new Tree<User>(new MemoryTrunk<User>()));

    // Entangle 1 → 2
    var branch12 = new Branch("http://grove2:5000");
    grove1.Entangle<User>(branch12, "1-to-2");

    // Entangle 2 → 3
    var branch23 = new Branch("http://grove3:5000");
    grove2.Entangle<User>(branch23, "2-to-3");

    // Stash in Grove 1
    var tree1 = grove1.GetTree<User>();
    tree1.Stash("alice", new User { Name = "Alice" });

    // Sync propagates: Grove 1 → Grove 2 → Grove 3
    await Task.Delay(1000); // Simulate sync delay

    // Verify Grove 3 has Alice
    var tree3 = grove3.GetTree<User>();
    var alice = tree3.Crack("alice");
    Assert.NotNull(alice);
    Assert.Equal("Alice", alice.Name);
}
```

---

## 🔮 Future: Advanced Mesh Features

**Coming Soon:**

- **Mesh Routing** - Intelligent path finding between groves
- **Gossip Protocol** - Eventual consistency across large meshes
- **Partition Tolerance** - Automatic healing of network splits
- **Leader Election** - Coordinated writes in distributed groves
- **Sharding** - Distribute Trees across multiple groves
- **Replication Factor** - Configurable redundancy

---

## 🧭 Navigation

- **Previous:** [[Storage]] - ITrunk implementations and capabilities
- **Next:** [[Dashboard]] - Web UI for grove visualization
- **Related:** [[Data Sync]] - Branches, Tangles, and sync strategies

🌰 *Your forest is now a mesh of synchronized groves. Let the nuts flow freely!*
