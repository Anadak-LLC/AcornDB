# ⚖️ Conflict Resolution

When two versions of the same nut compete for dominance, a **Squabble** occurs. AcornDB provides built-in conflict resolution with options for custom logic.

## What is a Squabble?

A **Squabble** happens when:
- A local nut has ID `"alice"`
- An incoming sync brings a different `"alice"` nut
- AcornDB must decide which version wins

```
Local Tree:   alice (v1, timestamp: 10:00 AM)
                ▼
             SQUABBLE!
                ▲
Remote Sync:  alice (v2, timestamp: 10:05 AM)
```

---

## 🕰️ Default: Timestamp-Based Resolution

By default, AcornDB uses **Last Write Wins (LWW)** based on timestamps.

### How It Works

```csharp
public void Squabble(string id, NutShell<T> incoming)
{
    if (_cache.TryGetValue(id, out var existing))
    {
        if (existing.Timestamp >= incoming.Timestamp)
        {
            Console.WriteLine($"> ⚖️ Squabble: Local nut for '{id}' is fresher. Keeping it.");
            _squabblesResolved++;
            return; // Local wins
        }

        Console.WriteLine($"> 🥜 Squabble: Incoming nut for '{id}' is newer. Replacing it.");
        _squabblesResolved++;
    }

    _cache[id] = incoming; // Incoming wins
    _trunk.Save(id, incoming);
}
```

### Example

```csharp
var tree = new Tree<User>(new DocumentStoreTrunk<User>("data/users"));

// Local stash at 10:00 AM
tree.Stash("alice", new User { Name = "Alice v1" });

// Simulate incoming sync at 10:05 AM (newer)
var incomingShell = new NutShell<User>
{
    Id = "alice",
    Payload = new User { Name = "Alice v2" },
    Timestamp = DateTime.UtcNow.AddMinutes(5)
};

tree.Squabble("alice", incomingShell);
// Output: > 🥜 Squabble: Incoming nut for 'alice' is newer. Replacing it.

var alice = tree.Crack("alice");
Console.WriteLine(alice.Name); // "Alice v2" (incoming won)
```

---

## 🧑‍⚖️ Custom Judges (Future)

While custom conflict resolution is planned, you can implement a custom judge pattern today.

### Implementing a Custom Judge

```csharp
public interface IConflictJudge<T>
{
    NutShell<T> Resolve(NutShell<T> local, NutShell<T> incoming);
}

public class VersionBasedJudge<T> : IConflictJudge<T>
{
    public NutShell<T> Resolve(NutShell<T> local, NutShell<T> incoming)
    {
        // Prefer higher version number
        return incoming.Version > local.Version ? incoming : local;
    }
}

public class MergeJudge : IConflictJudge<User>
{
    public NutShell<User> Resolve(NutShell<User> local, NutShell<User> incoming)
    {
        // Merge strategy: combine fields
        var merged = new User
        {
            Name = incoming.Payload.Name ?? local.Payload.Name,
            Email = incoming.Payload.Email ?? local.Payload.Email
        };

        return new NutShell<User>
        {
            Id = local.Id,
            Payload = merged,
            Timestamp = DateTime.UtcNow,
            Version = Math.Max(local.Version, incoming.Version) + 1
        };
    }
}
```

### Using a Custom Judge (Workaround)

```csharp
public class CustomTree<T> : Tree<T>
{
    private readonly IConflictJudge<T> _judge;

    public CustomTree(ITrunk<T> trunk, IConflictJudge<T> judge) : base(trunk)
    {
        _judge = judge;
    }

    public new void Squabble(string id, NutShell<T> incoming)
    {
        if (_cache.TryGetValue(id, out var existing))
        {
            var winner = _judge.Resolve(existing, incoming);
            _cache[id] = winner;
            _trunk.Save(id, winner);
            Console.WriteLine($"> ⚖️ Custom judge resolved squabble for '{id}'");
        }
        else
        {
            base.Squabble(id, incoming);
        }
    }
}

// Usage
var judge = new VersionBasedJudge<User>();
var tree = new CustomTree<User>(new FileTrunk<User>("data/users"), judge);
```

---

## 🔄 Undo Squabble

If your trunk supports history (e.g., `DocumentStoreTrunk`), you can undo the last squabble.

### How It Works

```csharp
tree.UndoSquabble("alice");
// Reverts to the previous version from history
```

### Example

```csharp
var trunk = new DocumentStoreTrunk<User>("data/users");
var tree = new Tree<User>(trunk);

tree.Stash("alice", new User { Name = "Alice v1" });
tree.Stash("alice", new User { Name = "Alice v2" });
tree.Stash("alice", new User { Name = "Alice v3" });

var current = tree.Crack("alice");
Console.WriteLine(current.Name); // "Alice v3"

tree.UndoSquabble("alice"); // Reverts to v2

var reverted = tree.Crack("alice");
Console.WriteLine(reverted.Name); // "Alice v2"
```

### Limitations

- Only works with trunks that support `GetHistory()`
- Cannot undo if no history exists
- Returns `false` if undo fails

---

## 🏆 Conflict Resolution Strategies

### 1. Last Write Wins (Default)

**Strategy:** Newest timestamp wins.

**Pros:**
- Simple, deterministic
- Works well for single-field updates

**Cons:**
- Can lose data if concurrent writes happen
- No semantic understanding

**When to use:** Simple CRUD apps, single-user scenarios

---

### 2. Version-Based

**Strategy:** Highest version number wins.

**Pros:**
- Tracks explicit version increments
- Prevents accidental overwrites

**Cons:**
- Requires version management
- Can still lose data

**When to use:** Multi-user apps with version tracking

```csharp
if (incoming.Version > existing.Version)
{
    return incoming; // Newer version wins
}
```

---

### 3. Merge Strategy

**Strategy:** Combine non-null fields from both versions.

**Pros:**
- No data loss
- Smart field-level merging

**Cons:**
- Complex to implement
- May create invalid states

**When to use:** Collaborative apps, field-level updates

```csharp
var merged = new User
{
    Name = incoming.Payload.Name ?? local.Payload.Name,
    Email = incoming.Payload.Email ?? local.Payload.Email
};
```

---

### 4. User Prompt (Interactive)

**Strategy:** Ask the user to resolve.

**Pros:**
- Human decision-making
- No data loss

**Cons:**
- Requires UI/CLI interaction
- Slows down sync

**When to use:** Desktop apps, manual conflict review

```csharp
Console.WriteLine("Conflict detected:");
Console.WriteLine($"Local: {local.Payload.Name}");
Console.WriteLine($"Remote: {incoming.Payload.Name}");
Console.Write("Keep (L)ocal or (R)emote? ");
var choice = Console.ReadLine();

return choice?.ToUpper() == "L" ? local : incoming;
```

---

### 5. Operational Transform (Advanced)

**Strategy:** Apply changes as operations, not values.

**Pros:**
- True concurrent editing
- No lost updates

**Cons:**
- Extremely complex
- Requires operation log

**When to use:** Real-time collaboration (Google Docs style)

---

## 📊 Squabble Statistics

Track conflict resolution activity:

```csharp
var stats = tree.GetNutStats();
Console.WriteLine($"Squabbles resolved: {stats.SquabblesResolved}");
```

### Monitoring Squabbles

```csharp
var squabbleLog = new List<string>();

tree.EventManager.Subscribe(user =>
{
    squabbleLog.Add($"{DateTime.UtcNow}: Squabble resolved for {user.Id}");
});

// After sync operations
foreach (var log in squabbleLog)
{
    Console.WriteLine(log);
}
```

---

## 🧪 Testing Conflict Resolution

### Test Default LWW

```csharp
[Fact]
public void Test_LastWriteWins_NewerWins()
{
    var tree = new Tree<User>(new MemoryTrunk<User>());

    var older = new NutShell<User>
    {
        Id = "alice",
        Payload = new User { Name = "Alice v1" },
        Timestamp = DateTime.UtcNow.AddMinutes(-10)
    };

    var newer = new NutShell<User>
    {
        Id = "alice",
        Payload = new User { Name = "Alice v2" },
        Timestamp = DateTime.UtcNow
    };

    tree.Squabble("alice", older);
    tree.Squabble("alice", newer);

    var result = tree.Crack("alice");
    Assert.Equal("Alice v2", result.Name); // Newer wins
}
```

### Test Undo Squabble

```csharp
[Fact]
public void Test_UndoSquabble_RevertsToHistory()
{
    var trunk = new DocumentStoreTrunk<User>("test-data");
    var tree = new Tree<User>(trunk);

    tree.Stash("alice", new User { Name = "v1" });
    tree.Stash("alice", new User { Name = "v2" });

    tree.UndoSquabble("alice");

    var result = tree.Crack("alice");
    Assert.Equal("v1", result.Name);
}
```

---

## 🛠️ Best Practices

### ✅ Do:
- Use timestamps for simple scenarios
- Implement custom judges for domain-specific logic
- Log all squabbles for debugging
- Test conflict scenarios thoroughly
- Use DocumentStoreTrunk for undo support

### ❌ Don't:
- Ignore conflicts (always handle them)
- Use random/non-deterministic resolution
- Resolve conflicts without logging
- Assume conflicts won't happen
- Overwrite without checking timestamps

---

## 🔮 Future Features

**Coming Soon:**

- Built-in `IConflictJudge<T>` interface
- Pluggable judge registration
- Multi-way merge support
- Conflict detection events
- Visual conflict diff viewer in Dashboard

```csharp
// Future API
tree.SetConflictJudge(new CustomJudge());

tree.OnConflict += (local, incoming) =>
{
    Console.WriteLine("Conflict detected!");
};
```

---

## 🧭 Navigation

- **Previous:** [[Events]] - Subscriptions and change notifications
- **Next:** [[Storage]] - ITrunk implementations and capabilities
- **Related:** [[Data Sync]] - Sync strategies that trigger squabbles

🌰 *May your squabbles be few, and your resolutions wise!*
