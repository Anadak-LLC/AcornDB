# AcornDB Architecture

**AcornDB** is a lightweight, embedded, event-driven NoSQL database engine built for .NET applications. It is designed to be local-first, embeddable, and cloud-extendable via a simple and intuitive API.

## 🌰 Overview

AcornDB is:
- **Local-first**: designed for edge devices, mobile, desktop, or microservices
- **Embedded**: integrates directly into your application without external services
- **Evented**: supports reactive subscriptions to data changes
- **Extendable**: seamlessly connects to **OakTree** — the cloud-hosted, managed service for scaling and backup

## 🔧 Core Concepts

### 📁 Storage Model
- **Single-file document store** per instance
- **JSON document structure**
- Optional support for:
  - TTLs
  - Secondary indexes
  - Binary attachments

### 📚 Data Access
- LINQ-like querying over collections
- Document IDs + secondary index querying
- Full support for `.NET` types via serialization adapters

### ⚡ Reactive Engine
- Supports real-time subscriptions to:
  - Collection-level events
  - Document change events
  - Query result observability
- Designed for low-latency, low-overhead data-driven UIs

### 🧠 In-Memory Caching
- Optional local in-memory LRU cache
- Supports write-behind and eventual persistence to file
- Ideal for hybrid edge/cloud patterns

## 🌐 Cluster Extension

AcornDB can connect to other AcornDB nodes (local or cloud) via simple API-based cluster formation.

### `.Extend(endpoint, key, options)`
Connects to a remote AcornDB or OakTree endpoint.

#### Behavior:
- Authenticates and establishes replication link
- Dynamically provisions a cloud-hosted cluster node if needed
- Negotiates:
  - Storage class
  - Resource scaling
  - Region or affinity group
  - Access roles

### `.ExtendFrom(endpoint, options)`
Spins up a new *local* node and attaches it to an existing cluster.

#### Use Cases:
- Edge computing with cloud master
- Local-first apps with central sync
- Hybrid developer workflows (dev <-> prod)

## 🌳 OakTree (Cloud Control Plane)

OakTree is the cloud-native SaaS that manages AcornDB clusters and provides:

- Secure, scalable hosting of cluster nodes
- Dynamic provisioning via `.Extend()`
- Monitoring, backup, alerting, and billing
- Cross-region replication & snapshots
- Optional edge sync coordination

## 🔐 Security

- Auth via signed keys or OAuth tokens
- Encrypted storage (optional)
- Per-endpoint and per-node permissions
- Granular access controls (RBAC planned)

## 🧪 Conflict Resolution (WIP)

- Planned: CRDT-based data structures for eventual consistency
- Modes:
  - Last-write-wins
  - Manual merge
  - Merge functions per-collection

## 🧱 Pluggable Architecture

Everything in AcornDB is designed to be extensible:

- Storage engine adapters (e.g., in-memory, file, remote)
- Replication transport (WebSocket, gRPC, SignalR, etc.)
- Eventing system (Reactive Extensions-based)
- Serialization format (default: JSON)

## 📦 Planned Modules

| Module | Description |
|--------|-------------|
| `Acorn.Sync` | Replication + snapshot sync engine |
| `Acorn.Cli` | Developer tooling for managing AcornDB locally |
| `Acorn.CloudAgent` | Local agent to coordinate `.Extend()` logic with OakTree |
| `Acorn.Memory` | In-memory only variant |
| `Acorn.Crdt` | Optional CRDT-based collections |
| `Acorn.Cache` | Local-first LRU/TTL caching layer |
| `Acorn.Observer` | Embedded pub/sub system |

## 💡 Usage Examples

### Basic Embedded Use
```csharp
var db = new AcornDb("user-data.acorn");
db.Collection("users").Insert(new { id = 1, name = "Taylor" });
```

### Extend to Cloud
```csharp
db.Cluster.Extend("https://oak.oaktree.dev", key: "abcd1234");
```

### Subscribe to Changes
```csharp
db.Collection("orders").OnChanged(order => {
    Console.WriteLine($"Order changed: {order.Id}");
});
```

## 🧠 Design Principles

- **Simple by default** — but powerful when extended
- **Developer-first** — built for .NET devs, with great DX
- **Local-first** — works offline, syncs when possible
- **Extensible by design** — storage, sync, events, and more
- **No Kubernetes Required™**

## 🚧 Roadmap Highlights (v0.1 – v1.0)

- [x] Embedded single-node JSON store
- [x] Reactive subscriptions
- [ ] Replication engine
- [ ] Cloud cluster provisioning (OakTree MVP)
- [ ] Conflict resolution & CRDT collection types
- [ ] Secure `.Extend()` & `.ExtendFrom()` API
- [ ] Admin UI for OakTree
- [ ] SDKs for .NET, JS, and Rust
- [ ] GitHub Sponsorship / OpenCore SaaS model

## 🤝 License & Community

- **License**: MIT / Elastic-style (TBD)
- **Community**: GitHub Discussions, Discord, Dev Blog, Launch Week coming soon
- **Sponsorships**: GitHub Sponsors, Buy Me A Coffee, or OakTree Pro

## 🐿️ Slogan Ideas

- “From acorns grow databases.”
- “Local-first, cloud-next.”
- “Cluster anywhere, from anything.”
- “No more YAML. Just .Extend().”
