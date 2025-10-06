# 📝 Changelog - Recent Improvements

## Latest Changes

### ✨ **Simplified API**

#### 1. Optional Trunk (Defaults to FileTrunk)
The trunk parameter is now **optional** and defaults to `FileTrunk<T>`:

```csharp
// Before: Had to specify trunk
var tree = new Tree<User>(new FileTrunk<User>("data/users"));

// Now: FileTrunk is the default
var tree = new Tree<User>(); // Automatically uses FileTrunk in data/User/
```

---

#### 2. Renamed `NutShell<T>` → `Nut<T>`
Simpler, cleaner naming:

```csharp
// Old
NutShell<User> shell = tree.Load("alice");

// New
Nut<User> nut = tree.Load("alice");
```

**Backwards compatibility:** `NutShell<T>` still works (marked obsolete).

---

#### 3. Auto-ID Detection (No Explicit ID Required)
You can now stash without specifying an ID if your class has an `Id` or `Key` property:

```csharp
public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }
}

var tree = new Tree<User>();
var user = new User { Name = "Alice" };

// Before: Had to specify ID explicitly
tree.Stash(user.Id, user);

// Now: Auto-detects ID property
tree.Stash(user); // 🎉 No ID needed!
```

**How it works:**
- Checks if type implements `INutment<TKey>`
- Falls back to reflection: looks for `Id`, `ID`, `Key`, `KEY` properties
- Caches the property accessor for performance
- Throws helpful error if no ID property found

---

#### 4. In-Process Tree Entanglement
Sync two trees in the same process without HTTP:

```csharp
var tree1 = new Tree<User>();
var tree2 = new Tree<User>(new MemoryTrunk<User>());

// Before: Needed TreeBark server + HTTP
var branch = new Branch("http://localhost:5000");
tree1.Entangle(branch);

// Now: Direct tree-to-tree sync
tree1.Entangle(tree2); // 🪢 In-process sync!

tree1.Stash(new User { Id = "alice", Name = "Alice" });
// Automatically synced to tree2 via InProcessBranch
```

---

#### 5. Shared FileTrunk for Same-Host Sync
Two processes can sync by pointing to the **same FileTrunk** directory:

```csharp
// Process 1
var tree1 = new Tree<User>(new FileTrunk<User>("shared/users"));
tree1.Stash(new User { Id = "alice", Name = "Alice" });

// Process 2
var tree2 = new Tree<User>(new FileTrunk<User>("shared/users"));
var alice = tree2.Crack("alice"); // ✅ Synced via shared storage
```

**Simpler than:** Creating a sync hub or running HTTP server.

---

## Migration Guide

### Updating from Old API

| Old Code | New Code |
|----------|----------|
| `new Tree<T>(new FileTrunk<T>())` | `new Tree<T>()` |
| `NutShell<T>` | `Nut<T>` (or keep using NutShell) |
| `tree.Stash(obj.Id, obj)` | `tree.Stash(obj)` (if has Id property) |
| HTTP server for same-host sync | Shared FileTrunk or `tree1.Entangle(tree2)` |

---

## Breaking Changes

**None!** All changes are backwards compatible:
- `NutShell<T>` still works (deprecated warning)
- Explicit `Stash(id, item)` still available
- All trunk constructors unchanged

---

## Performance Improvements

- **ID extraction cached via reflection** - No performance hit on repeated stashes
- **In-process entanglement** - No HTTP overhead for same-process sync

---

🌰 *Simpler API. Same power. More nuts!*
