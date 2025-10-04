<p align="center">
  <img src="acorn-logo.png" alt="AcornDB logo" width="300"/>
</p>
# 🌰 AcornDB

**AcornDB** is a lightweight, reactive, embedded database for .NET applications — built for devs who are tired of paying cloud bills to store 5MB of JSON.

> 🐿️ Nutty by design. Practical by necessity.

---

## 🚀 Why AcornDB Exists

Most apps don't need Cosmos DB, Kafka, or Redis.  
They need:

- Fast, local persistence
- Simple per-tenant or per-user storage
- Sync that works *without* devoting your life to conflict resolution

**AcornDB** is for:
- Desktop apps
- IoT devices
- Mobile backends
- CLI tools
- Serverless & edge workloads

---

## 🔧 Features

| Feature | Description |
|--------|-------------|
| 🌰 `Stash` / `Crack` | Insert and retrieve objects with zero boilerplate |
| 🛡️ `NutShell<T>` | Metadata-wrapped documents (with TTL, versioning, timestamps) |
| 🔁 `AutoSync<T>` | Automated two-way syncing between collections |
| 🧠 `INutment<TKey>` | Dev-controlled document IDs |
| 🧹 `SmushNow()` | Manual log compaction |
| 🛰️ `ExportChanges()` / `ImportChanges()` | Sync between AcornDB nodes |

---

## 🧪 Getting Started

```bash
dotnet add package AcornDB # (coming soon to NuGet!)

