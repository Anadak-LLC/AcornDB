# 🌰 AcornDB

![AcornDB logo](https://raw.githubusercontent.com/Anadak-LLC/AcornDB/main/cdf1927f-1efd-4e54-8772-45476d5e6819.png)

**AcornDB** is a lightweight, reactive, embedded database for .NET applications — built for devs who are tired of paying cloud bills to store 5MB of JSON.

> 🐿️ Nutty by design. Practical by necessity.

---

## 🚀 Why AcornDB Exists

Most apps don't need Cosmos DB, Kafka, or Redis.  
They need:

- Fast, local persistence  
- Simple per-tenant or per-user storage  
- Sync that works *without* devoting your life to conflict resolution  

AcornDB is ideal for:

- Desktop apps  
- IoT devices  
- Mobile backends  
- CLI tools  
- Edge & serverless workloads  
- You. Yes, you. With the 10MB JSON doc and $400 cloud bill.

---

## 🍁 Core Concepts

| Term            | What It Means                                      |
|-----------------|----------------------------------------------------|
| `Tree<T>`       | Your document collection (aka embedded table)      |
| `NutShell<T>`   | Your wrapped object with metadata                  |
| `Trunk`         | On-disk file storage for each Tree                 |
| `Branch`        | Sync connection to another Tree (`HTTP`)           |
| `Grove`         | A group of Trees connected via sync                |
| `Tangle`        | Live link between two Trees (coming soon)          |
| `Shake()`       | Sync your Tree with the world                      |
| `Stash`, `Crack`, `Toss` | Insert, update, and delete – the squirrel way |

---

## 🔧 Features

| Feature                     | Description |
|----------------------------|-------------|
| 🌰 `Stash` / `Crack` / `Toss` | Insert, update, and delete objects with zero boilerplate |
| 🛡️ `NutShell<T>`             | Metadata-wrapped documents with TTL, timestamps, and versioning |
| 🔁 `AutoSync`, `Branch`, `Grove` | Sync locally or over network (with TreeBark server) |
| 🧠 `INutment<TKey>`         | Interface for typed document IDs |
| 🧹 `SmushNow()`             | Manual log compaction |
| 🛰️ `ExportChanges()` / `ImportChanges()` | Sync between nodes manually or automatically |
| ⚡ `Shake()`                | Verbally expressive way to trigger syncing |
| 🌲 `Grove.Entangle()`       | One-liner to join a network of Trees |
| 🔐 Totem-based auth (Coming Soon) | Yes. We're doing woodland-themed security too. |

---

## 🧪 Getting Started

```bash
# Coming soon to NuGet:
dotnet add package AcornDB
```

```c#
var tree = new Tree<User>();
tree.Stash(new User { Id = "1", Name = "Squirrelius Maximus" });

var grove = new Grove();
grove.Entangle(tree, "http://localhost:5000"); // starts syncing!

tree.Shake(); // optional: force sync
```

## 🧱 Project Structure

| Folder             | Purpose                                       |
|--------------------|-----------------------------------------------|
| `AcornDB`          | Core embedded DB (Trees, Trunks, NutShells)   |
| `AcornSyncServer`  | TreeBark: a standalone HTTP sync server       |
| `Tests`            | xUnit-based test coverage                     |
| `AcornDash` (planned) | UI for nut inspection & tree browsing     |

---

## 🧙 What's Coming Next

- 🔐 Auth: Totems, Critters, ForageRights, and BarkCodes  
- 🌍 Peer mesh with Tangle support  
- 🎨 NutDash visual dashboard  
- 📦 NuGet packaging & CLI tools  
- 🧪 Example apps & playgrounds  

---

## 🦦 Made with acorns and sarcasm

Built by devs who’ve had enough of bloated infra and naming things like `DataManagerServiceClientFactoryFactory`.

Contribute, fork, star, and if you build something with it — send us your weirdest squirrel pun.