# 🌰 AcornDB CLI

Command-line interface for AcornDB - the distributed document database with sync superpowers.

## Installation

```bash
dotnet build
# Or publish as standalone:
dotnet publish -c Release -r win-x64 --self-contained
```

## Commands

### `acorn new <path>`
Create a new grove (database) at the specified path.

```bash
acorn new ./mygrove
# ✅ Created new grove at: ./mygrove
# 📁 Grove ready for use!
```

---

### `acorn inspect <path>`
Inspect a grove and show statistics about trees, nuts, and sync status.

```bash
acorn inspect ./mygrove
# 📊 Grove Inspection: ./mygrove
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 📁 Path: C:\Development\mygrove
# 🌳 Tree files found: 3
#
# Trees:
#   • users.acorn (1.2 MB)
#   • posts.acorn (845 KB)
#   • sessions.acorn (234 KB)
#
# Statistics:
#   Total Trees: 3
#   Total Stashed: 1,542
#   Total Tossed: 89
#   Squabbles: 12
#   Active Tangles: 2
```

---

### `acorn sync <path> <url>`
Synchronize a grove with a remote AcornDB server.

```bash
acorn sync ./mygrove http://192.168.1.100:5000
# 🔄 Syncing grove at ./mygrove with http://192.168.1.100:5000...
# ✅ Sync complete!
```

---

### `acorn export <path> [output-file]`
Export grove data to JSON for backup or migration.

```bash
acorn export ./mygrove backup.json
# 📤 Exporting grove from ./mygrove...
# ✅ Exported to: backup.json
```

---

### `acorn discover [port]`
Start Canopy network discovery to find other AcornDB nodes on your network.

```bash
acorn discover 5000
# 🌳 Starting Canopy network discovery on port 5000...
# Press Ctrl+C to stop
#
# 🌳 Canopy Discovery - Found 2 nodes
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# 🟢 Active http://192.168.1.100:5000
#   Trees: 5 | Types: User, Post, Session
#   Last seen: 2s ago
#
# 🟢 Active http://192.168.1.101:5000
#   Trees: 3 | Types: User, Comment, Tag
#   Last seen: 4s ago
#
# Network: 2 active, 8 total trees
```

**Features:**
- Auto-discovers AcornDB nodes via UDP broadcast
- Shows live status and tree information
- Updates every 5 seconds
- Works on local networks (LAN)

---

### `acorn mesh <path>`
Create a mesh network by auto-discovering and connecting to all nearby nodes.

```bash
acorn mesh ./mygrove
# 🕸️  Creating mesh network from grove at ./mygrove...
# This will discover and connect to all nearby AcornDB nodes.
#
# ✅ Mesh discovery started!
# 🌳 Canopy: Discovered 192.168.1.100:5000 (5 trees)
# 🔗 Auto-connected to http://192.168.1.100:5000
# 🌳 Canopy: Discovered 192.168.1.101:5000 (3 trees)
# 🔗 Auto-connected to http://192.168.1.101:5000
# Press Ctrl+C to stop
```

**What it does:**
- Automatically discovers nodes via Canopy
- Entangles all trees with discovered nodes
- Creates a full mesh network topology
- Maintains connections until stopped

---

## Canopy Discovery System

**Canopy** is AcornDB's network auto-discovery system that uses UDP broadcast to find other AcornDB instances on your local network.

### How it works:

1. **Broadcasting**: Each node broadcasts its presence every 5 seconds
2. **Listening**: Nodes listen for broadcasts from others
3. **Auto-connect**: Optionally connects automatically when nodes are discovered
4. **Loop prevention**: Built-in mesh sync with duplicate detection

### Architecture:

```
Node A (Port 5000)                 Node B (Port 5001)
     │                                    │
     ├─ UDP Broadcast (Port 50505) ──────┤
     │  "CANOPY:{nodeId, trees...}"      │
     │                                    │
     └─ HTTP Sync (Port 5000) ←──────────┘
        Bidirectional entanglement
```

### Discovery Packet Format:

```json
{
  "NodeId": "abc-123-def-456",
  "HttpPort": 5000,
  "TreeCount": 5,
  "TreeTypes": ["User", "Post", "Session"],
  "Timestamp": "2025-10-07T12:34:56Z"
}
```

---

## Examples

### Complete Workflow

```bash
# Terminal 1 - Start first node
acorn new ./node1
acorn mesh ./node1

# Terminal 2 - Start second node
acorn new ./node2
acorn mesh ./node2
# 🌳 Canopy: Discovered 127.0.0.1:5000 (0 trees)
# 🔗 Auto-connected to http://127.0.0.1:5000

# Terminal 3 - Start third node
acorn new ./node3
acorn mesh ./node3
# 🌳 Canopy: Discovered 127.0.0.1:5000 (0 trees)
# 🔗 Auto-connected to http://127.0.0.1:5000
# 🌳 Canopy: Discovered 127.0.0.1:5001 (0 trees)
# 🔗 Auto-connected to http://127.0.0.1:5001

# Now all three nodes are connected in a mesh!
```

### Just Discovery (No Auto-Connect)

```bash
# Discover nodes without connecting
acorn discover

# Manually sync with discovered nodes
acorn sync ./mygrove http://192.168.1.100:5000
```

---

## Environment Variables

- `ACORN_PORT` - Default HTTP port (default: 5000)
- `ACORN_DISCOVERY_PORT` - UDP discovery port (default: 50505)
- `ACORN_AUTO_CONNECT` - Auto-connect on discovery (default: true)

---

## Exit Codes

- `0` - Success
- `1` - Error (invalid command, file not found, etc.)

---

## Building from Source

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Publish standalone (Windows)
dotnet publish -c Release -r win-x64 --self-contained -o dist

# Publish standalone (Linux)
dotnet publish -c Release -r linux-x64 --self-contained -o dist

# Publish standalone (macOS)
dotnet publish -c Release -r osx-x64 --self-contained -o dist
```

---

## See Also

- [AcornDB Documentation](../README.md)
- [Canopy Discovery System](../AcornDB/Sync/CanopyDiscovery.cs)
- [Mesh Sync Guide](../AcornDB/Sync/MeshSyncExample.md)
