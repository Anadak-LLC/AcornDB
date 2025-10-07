# Mesh Sync with Loop Prevention - Examples

## Basic Mesh Setup

```csharp
using AcornDB;
using AcornDB.Sync;
using AcornDB.Storage;

// Create a 3-node mesh
var mesh = new MeshCoordinator<string>();

var nodeA = new Tree<string>(new MemoryTrunk<string>());
var nodeB = new Tree<string>(new MemoryTrunk<string>());
var nodeC = new Tree<string>(new MemoryTrunk<string>());

mesh.AddNode("NodeA", nodeA);
mesh.AddNode("NodeB", nodeB);
mesh.AddNode("NodeC", nodeC);

// Create full mesh (all nodes connected to all)
mesh.CreateFullMesh();
// Output: 🕸️ Mesh link created: NodeA ↔ NodeB
//         🕸️ Mesh link created: NodeA ↔ NodeC
//         🕸️ Mesh link created: NodeB ↔ NodeC
//         🕸️ Full mesh created with 3 nodes
```

## Loop Prevention in Action

```csharp
// Stash data on Node A
nodeA.Stash("key1", "value from A");

// Change propagates automatically:
// NodeA → NodeB → NodeC
// NodeA → NodeC
// But NodeC doesn't send back to NodeA (loop detected!)

// Verify all nodes have the data
Console.WriteLine(nodeA.Crack("key1")); // "value from A"
Console.WriteLine(nodeB.Crack("key1")); // "value from A"
Console.WriteLine(nodeC.Crack("key1")); // "value from A"

// Check mesh stats
var stats = nodeA.GetMeshStats();
Console.WriteLine($"Node {stats.NodeId}:");
Console.WriteLine($"  - Tracked changes: {stats.TrackedChangeIds}");
Console.WriteLine($"  - Active tangles: {stats.ActiveTangles}");
Console.WriteLine($"  - Max hops: {stats.MaxHopCount}");
```

## Topology Patterns

### Ring Topology
```csharp
mesh.CreateRing();
// Output: 🔗 Ring topology created with 3 nodes
// A → B → C → A (forms a cycle)
```

### Star Topology
```csharp
mesh.CreateStar("NodeA");
// Output: ⭐ Star topology created with hub: NodeA
// All nodes connect to NodeA only
```

### Custom Topology
```csharp
// Manual connections
mesh.ConnectNodes("NodeA", "NodeB");
mesh.ConnectNodes("NodeB", "NodeC");
// Creates A ↔ B ↔ C (linear chain)
```

## Hop Limiting

```csharp
// Limit propagation distance
nodeA.MaxHopCount = 2;

// In a 5-node linear chain (A-B-C-D-E):
// Change from A reaches B (1 hop) and C (2 hops)
// Change stops at C, doesn't reach D or E
```

## Network Statistics

```csharp
var networkStats = mesh.GetNetworkStats();

Console.WriteLine($"Total nodes: {networkStats.Topology.TotalNodes}");
Console.WriteLine($"Total connections: {networkStats.Topology.TotalConnections}");
Console.WriteLine($"Average degree: {networkStats.Topology.AverageDegree:F2}");

foreach (var (nodeId, nodeStats) in networkStats.NodeStats)
{
    Console.WriteLine($"{nodeId}: {nodeStats.TrackedChangeIds} tracked changes");
}
```

## How Loop Prevention Works

### 1. Change ID Tracking
- Each change gets a unique `ChangeId` (GUID)
- Nodes remember recently seen change IDs
- Duplicate changes are ignored

### 2. Origin Tracking
- Each nut tracks its `OriginNodeId`
- If a change loops back to its origin, it's rejected

### 3. Hop Count
- Each propagation increments `HopCount`
- Changes stop when `HopCount >= MaxHopCount`
- Default limit: 10 hops

### Example Flow:

```
1. NodeA creates change with ChangeId=abc123, HopCount=0
2. NodeA → NodeB: ChangeId=abc123, HopCount=1
3. NodeB → NodeC: ChangeId=abc123, HopCount=2
4. NodeC → NodeA: REJECTED (origin = NodeA)
5. NodeB → NodeC: REJECTED (already seen abc123)
```

This ensures changes propagate throughout the mesh exactly once per node, without loops!
