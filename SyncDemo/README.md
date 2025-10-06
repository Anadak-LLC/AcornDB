# 🌐 AcornDB P2P Sync Demo

This demo showcases **file system-based peer-to-peer synchronization** between two AcornDB processes running on the same host.

**No server required!** Both processes sync via a shared directory.

## 🏗️ Architecture

```
┌─────────────────┐         ┌─────────────────┐
│   Process 1     │         │   Process 2     │
│   (Desktop)     │         │   (Mobile)      │
├─────────────────┤         ├─────────────────┤
│ DocumentStore   │         │ DocumentStore   │
│ data/process1   │         │ data/process2   │
└────────┬────────┘         └────────┬────────┘
         │                           │
         │    ┌───────────────┐     │
         └───►│  Sync Hub     │◄────┘
              │ data/sync-hub │
              └───────────────┘
                Shared File System
```

Each process:
- Has its own local `DocumentStoreTrunk` for persistence
- Periodically exports changes to the shared sync hub
- Imports changes from other processes via the hub
- Resolves conflicts using timestamp-based logic

## 🚀 Running the Demo

### Option 1: Using the Launcher Script

**Terminal 1 - Process 1:**
```bash
cd SyncDemo
run-demo.cmd 1
```

**Terminal 2 - Process 2:**
```bash
cd SyncDemo
run-demo.cmd 2
```

### Option 2: Using dotnet run

**Terminal 1 - Process 1:**
```bash
cd SyncDemo
dotnet run -- 1
```

**Terminal 2 - Process 2:**
```bash
cd SyncDemo
dotnet run -- 2
```

### Cleaning Data

```bash
run-demo.cmd clean
```

## 📋 What Happens

1. **Process 1** starts and creates two users (Alice, Bob)
2. **Process 1** exports these to the sync hub (`data/sync-hub/process1.json`)
3. **Process 2** starts and imports changes from the hub
4. **Process 2** sees Alice and Bob in its local tree!
5. **Process 2** adds Charlie and exports to hub
6. **Process 1** imports Charlie on next sync cycle
7. Both processes stay in sync via the shared hub directory

## 🎮 Interactive Features

While running, you can:
- **Press 'a'** - Add a new user interactively
- **Press 'q'** - Quit the process

Changes sync automatically every 3 seconds!

## 🔍 How It Works

### FileSystemSyncHub

The `FileSystemSyncHub<T>` class implements a simple file-based sync broker:

1. **PublishChanges()** - Each process writes its full state to `{processId}.json`
2. **PullChanges()** - Reads all other process files and returns their changes
3. **Conflict Resolution** - Timestamp-based (newer wins)

### Sync Flow

```csharp
// Every 3 seconds:
SyncToHub(localTree, syncHub, "process1");    // Export my changes
var imported = SyncFromHub(localTree, syncHub, "process1"); // Import others' changes

// SyncFromHub only imports if:
// - Document doesn't exist locally, OR
// - Remote timestamp is newer than local
```

## 📂 Data Storage

After running, you'll see:

```
SyncDemo/
  data/
    process1/
      users/
        changes.log       # DocumentStore append-only log
    process2/
      users/
        changes.log
    sync-hub/
      process1.json       # Process 1's exported state
      process2.json       # Process 2's exported state
```

## ⚡ Benefits of File-Based Sync

✅ **No network stack** - Just file I/O
✅ **Simple debugging** - Inspect JSON files directly
✅ **Same-host optimized** - Perfect for local multi-process apps
✅ **Conflict resolution** - Built-in timestamp-based squabbles
✅ **Audit trail** - DocumentStoreTrunk keeps full history

## 🆚 Comparison with TreeBark Server

| Feature | File-Based P2P | TreeBark Server |
|---------|----------------|-----------------|
| Network required | ❌ No | ✅ Yes (HTTP) |
| Multi-host sync | ❌ No | ✅ Yes |
| Same-host sync | ✅ Excellent | ⚠️ Overkill |
| Setup complexity | 🟢 Minimal | 🟡 Moderate |
| Scalability | 🟡 Limited | 🟢 Good |
| Use case | Desktop apps, CLI tools | Distributed systems |

## 🧪 Try It Yourself

1. Start Process 1 and let it initialize
2. Start Process 2 and watch it pull Alice and Bob
3. Press 'a' in Process 2 and add "David"
4. Watch Process 1 import David after 3 seconds!
5. Add users in both processes simultaneously and see conflict resolution

---

🌰 **Nutty by design. Practical by necessity.**
