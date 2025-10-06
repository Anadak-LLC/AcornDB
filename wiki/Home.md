# 🌰 Welcome to AcornDB

**AcornDB** is a lightweight, reactive, embedded database for .NET — for developers who'd rather ship products than pay $400/month to store 5MB of JSON.

> 🐿️ Nutty by design. Practical by necessity.

## What is AcornDB?

AcornDB is a local-first, embeddable document database that uses quirky woodland-themed metaphors to make data persistence fun again. Instead of "tables" and "documents," you work with **Trees** and **NutShells**. Instead of "syncing," you **Shake** your trees. And when conflicts happen? Your nuts **Squabble** it out.

But beneath the whimsy lies a powerful, production-ready database engine with:

- **Zero-configuration persistence** - Stash data, crack it open later
- **Live sync** - Real-time data synchronization across devices and processes
- **Conflict resolution** - Timestamp-based with custom override support
- **Time-travel** - Full versioning and history with DocumentStoreTrunk
- **Pluggable storage** - Swap between file, memory, blob, or versioned backends
- **Reactive events** - Subscribe to changes with Rx.NET
- **Mesh networking** - UDP-based peer discovery and auto-entangling

## Why AcornDB Exists

Most apps don't need Cosmos DB, Kafka, or Redis.

They need:
- Fast, local-first persistence
- Simple per-tenant or per-user storage
- Offline support + syncing that doesn't make you cry

**AcornDB is perfect for:**
- Desktop applications
- IoT devices
- Mobile backends
- CLI tools
- Serverless & edge workloads
- Single-user SaaS apps that store 10KB per user

## Core Philosophy

> 🐿️ Serious software. Zero seriousness.

We believe:
- Developers deserve **fun**
- Tools should make you **smile**, not sigh
- Syncing JSON should not require Kubernetes and a degree in wizardry
- **"Toss the nut and shake the tree"** should be valid engineering advice

If you've ever rage-quit YAML, yelled at Terraform, or cried syncing offline-first apps — welcome. You've found your grove.

## Quick Example

```csharp
// Create a Tree and stash some data
var tree = new Tree<User>(new FileTrunk<User>("data/users"));
tree.Stash("squirrel-1", new User { Name = "Squirrelius Maximus" });

// Set up syncing with a Grove
var grove = new Grove();
grove.Plant(tree);
grove.Oversee<User>(new Branch("http://sync-server:5000"));

tree.Shake(); // Force sync
```

## What's in the Grove?

This wiki contains everything you need to become an AcornDB expert:

- **[[Concepts]]** - The woodland lexicon (Tree, NutShell, Grove, Branch, Tangle)
- **[[Getting Started]]** - Install, configure, and stash your first nut
- **[[Data Sync]]** - Entangling, shaking, and branch management
- **[[Events]]** - Reactive subscriptions and change notifications
- **[[Conflict Resolution]]** - Squabbles, judges, and timestamp wars
- **[[Storage]]** - ITrunk implementations (File, Memory, Azure, DocumentStore)
- **[[Cluster & Mesh]]** - Multi-grove forests, UDP discovery, and mesh sync
- **[[Dashboard]]** - Web UI for visualizing your grove

## Get Started

👉 **New to AcornDB?** Start with [[Getting Started]]

👉 **Want to understand the metaphors?** Read [[Concepts]]

👉 **Building a sync system?** Jump to [[Data Sync]]

---

🌰 *Stash boldly. Crack with confidence. And never, ever apologize for getting a little squirrelly.*
