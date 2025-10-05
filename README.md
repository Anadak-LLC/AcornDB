
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
| `Tree<T>`        | A local document collection — your “embedded table”          |
| `NutShell<T>`    | An object wrapped with metadata (TTL, version, timestamp)    |
| `Trunk`          | The on-disk file store behind each Tree                      |
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

## 🧱 Project Structure

| Folder             | Purpose                                      |
|--------------------|----------------------------------------------|
| `AcornDB`          | Core engine (Tree, NutShell, Trunk, Tangle)  |
| `AcornSyncServer`  | TreeBark: minimal HTTP server for sync       |
| `Tests`            | xUnit tests (conflict, stash/toss, etc.)     |
| `AcornDash` (soon) | GUI explorer for your nut collections        |

---

## 🧙 What’s Coming

- 🔐 **Auth**: Totems, ForageRights, Critters, and BarkCodes  
- 📡 **Mesh sync**: Peer-to-peer Tangle networks  
- 🪟 **NutDash**: GUI dashboard to browse trees  
- 📦 **NuGet & CLI**: Install and create projects with `acorn new`  
- 🔁 **AutoRecovery**: Offline-first sync queue with resilience  
- 🧪 **Playgrounds**: Sample apps, code snippets, and demos  

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
