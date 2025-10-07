# 🐿️ GitHubTrunk - Peak Nuttiness Achieved!

## 🎯 What is GitHubTrunk?

**GitHubTrunk** is the nuttiest trunk in AcornDB - your database IS your Git history!

Every `Stash()` operation creates a Git commit. Every `Toss()` is a deletion commit. Your entire database history is preserved in Git, complete with commit messages, timestamps, and author info.

### Features
- ✅ **Every Stash = Git Commit** - Full version control built-in
- ✅ **Time-Travel** - Access any previous version via Git history
- ✅ **Distributed** - Push/pull to GitHub, GitLab, or any Git remote
- ✅ **Conflict Resolution** - Use Git merge strategies
- ✅ **Full History** - Never lose data, every change tracked
- ✅ **Auto-Push** - Optionally push after each commit
- ✅ **Testable** - IGitProvider interface for mocking

---

## 🚀 Quick Start

### Basic Usage

```csharp
using AcornDB;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; }
    public int Age { get; set; }
}

// Create a tree with Git storage
var tree = new Acorn<User>()
    .WithGitStorage()  // 🐿️ That's it!
    .Sprout();

// Every stash is a Git commit!
tree.Stash(new User { Id = "alice", Name = "Alice", Age = 30 });
// ✓ Committed alice → 7a8b9c1

tree.Stash(new User { Id = "bob", Name = "Bob", Age = 25 });
// ✓ Committed bob → 3d4e5f2

// Load normally
var alice = tree.Crack("alice");
Console.WriteLine($"Found: {alice.Name}");

// View Git history!
var history = tree.GetHistory("alice");
Console.WriteLine($"Alice has {history.Count} versions in Git history");
```

---

## 🎨 Configuration Options

### Custom Repository Path

```csharp
var tree = new Acorn<User>()
    .WithGitStorage(repoPath: "./my-git-database")
    .Sprout();
```

### Custom Author Info

```csharp
var tree = new Acorn<User>()
    .WithGitStorage(
        repoPath: "./data",
        authorName: "John Doe",
        authorEmail: "john@example.com"
    )
    .Sprout();
```

### Auto-Push to Remote

```csharp
// Automatically push to remote after each commit
var tree = new Acorn<User>()
    .WithGitStorage(
        repoPath: "./data",
        autoPush: true  // 🚀 Push after every commit!
    )
    .Sprout();

// Make sure you have a Git remote configured:
// git remote add origin https://github.com/user/repo.git
```

---

## 🔄 Manual Push/Pull

```csharp
var gitTrunk = new GitHubTrunk<User>("./data");
var tree = new Acorn<User>()
    .WithTrunk(gitTrunk)
    .Sprout();

// Stash some data
tree.Stash(new User { Id = "alice", Name = "Alice" });
tree.Stash(new User { Id = "bob", Name = "Bob" });

// Manually push to remote
gitTrunk.Push("origin", "main");

// Pull from remote
gitTrunk.Pull("origin", "main");
```

---

## 📜 Time-Travel with Git History

```csharp
var tree = new Acorn<User>()
    .WithGitStorage(repoPath: "./user-db")
    .Sprout();

// Stash multiple versions
tree.Stash(new User { Id = "alice", Name = "Alice v1", Age = 25 });
Thread.Sleep(100);
tree.Stash(new User { Id = "alice", Name = "Alice v2", Age = 26 });
Thread.Sleep(100);
tree.Stash(new User { Id = "alice", Name = "Alice v3", Age = 27 });

// Get full Git history
var history = tree.GetHistory("alice");
Console.WriteLine($"Alice has {history.Count} versions:");
foreach (var version in history)
{
    Console.WriteLine($"  - {version.Payload.Name} (Age: {version.Payload.Age}) @ {version.Timestamp}");
}

// Output:
// Alice has 3 versions:
//   - Alice v1 (Age: 25) @ 2025-10-07 10:15:00
//   - Alice v2 (Age: 26) @ 2025-10-07 10:15:01
//   - Alice v3 (Age: 27) @ 2025-10-07 10:15:02
```

---

## 🎯 Use Cases

### 1. **Audit Trail & Compliance**

Perfect for applications that need full audit trails:

```csharp
var tree = new Acorn<FinancialRecord>()
    .WithGitStorage(
        repoPath: "./audit-trail",
        authorName: "Audit System",
        authorEmail: "audit@company.com"
    )
    .Sprout();

// Every change is tracked with commit message, author, and timestamp
tree.Stash(new FinancialRecord { Id = "tx-001", Amount = 1000.00 });
```

### 2. **Distributed Databases**

Push/pull to sync across machines:

```csharp
// Machine 1
var tree1 = new Acorn<Document>()
    .WithGitStorage(repoPath: "./docs")
    .Sprout();

tree1.Stash(new Document { Id = "doc1", Title = "Report" });

// Push to GitHub
var gitTrunk = (GitHubTrunk<Document>)tree1._trunk;
gitTrunk.Push("origin", "main");

// Machine 2
var tree2 = new Acorn<Document>()
    .WithGitStorage(repoPath: "./docs")
    .Sprout();

// Pull from GitHub
var gitTrunk2 = (GitHubTrunk<Document>)tree2._trunk;
gitTrunk2.Pull("origin", "main");

// Now both machines have the same data!
```

### 3. **Time-Travel Debugging**

Access any previous state:

```csharp
var tree = new Acorn<Configuration>()
    .WithGitStorage()
    .Sprout();

// Make changes over time
tree.Stash(new Configuration { Id = "app-config", Setting = "v1" });
tree.Stash(new Configuration { Id = "app-config", Setting = "v2" });
tree.Stash(new Configuration { Id = "app-config", Setting = "v3-broken" });

// Oops, v3 broke something! Check history:
var history = tree.GetHistory("app-config");
var workingVersion = history[1]; // Get v2
Console.WriteLine($"Last working setting: {workingVersion.Payload.Setting}");
```

### 4. **Collaborative Editing**

Multiple users can push/pull changes:

```csharp
var tree = new Acorn<WikiPage>()
    .WithGitStorage(
        repoPath: "./wiki",
        authorName: "Alice",
        authorEmail: "alice@example.com",
        autoPush: true
    )
    .Sprout();

// Alice edits
tree.Stash(new WikiPage { Id = "homepage", Content = "Welcome to our wiki!" });

// Auto-pushed to remote

// Bob pulls, edits, pushes
var bobTree = new Acorn<WikiPage>()
    .WithGitStorage(
        repoPath: "./wiki-bob",
        authorName: "Bob",
        authorEmail: "bob@example.com"
    )
    .Sprout();
```

---

## 🛠️ Advanced: Custom Git Provider

For testing or custom Git implementations:

```csharp
public class MockGitProvider : IGitProvider
{
    // Implement IGitProvider for testing without actual Git
    // ...
}

var mockGit = new MockGitProvider();
var tree = new Acorn<User>()
    .WithGitStorage(mockGit)
    .Sprout();
```

---

## 📊 How It Works

### Storage Structure

```
./acorndb_git_User/
├── .git/                    # Git repository
├── alice.json               # User with ID "alice"
├── bob.json                 # User with ID "bob"
└── charlie.json             # User with ID "charlie"
```

Each nut is stored as a separate JSON file. The filename is the nut's ID (sanitized).

### Commit Messages

Every commit has a descriptive message:

```
Stash: alice at 2025-10-07 10:30:15
Stash: bob at 2025-10-07 10:30:20
Toss: charlie at 2025-10-07 10:30:25
```

### Git Log

Use standard Git commands to inspect your database:

```bash
cd ./acorndb_git_User
git log --oneline
# 7a8b9c1 Stash: alice at 2025-10-07 10:30:15
# 3d4e5f2 Stash: bob at 2025-10-07 10:30:20
# 9f1a2b3 Toss: charlie at 2025-10-07 10:30:25

git show 7a8b9c1:alice.json
# View Alice's data at that commit
```

---

## ⚡ Performance Considerations

### Pros:
- ✅ Full version history (never lose data)
- ✅ Natural distributed sync via Git
- ✅ Standard Git tools work (git log, git diff, etc.)
- ✅ Compression via Git (delta compression)
- ✅ Conflict resolution built-in (Git merge)

### Cons:
- ⚠️ Slower than FileTrunk (commit overhead)
- ⚠️ Repo size grows over time (use `git gc` for cleanup)
- ⚠️ Not suitable for high-frequency writes (thousands per second)

### Best For:
- Configuration management
- Audit trails
- Collaborative editing
- Long-term data retention
- Distributed systems
- Compliance requirements

### Not Ideal For:
- High-frequency sensors/IoT
- Real-time applications
- Large binary data
- Sub-millisecond latency requirements

---

## 🎉 Summary

**GitHubTrunk** brings the power of Git to your database:

```csharp
// Simple
var tree = new Acorn<User>().WithGitStorage().Sprout();

// With options
var tree = new Acorn<User>()
    .WithGitStorage(
        repoPath: "./my-db",
        authorName: "Me",
        authorEmail: "me@example.com",
        autoPush: true
    )
    .Sprout();

// Use like any other tree
tree.Stash(new User { Id = "alice", Name = "Alice" });
var alice = tree.Crack("alice");
var history = tree.GetHistory("alice");
```

**Your database IS your Git history!** 🐿️🌰

---

## 🔮 Future Enhancements

- [ ] Squash/rebase support for compaction
- [ ] Branch support (multiple timelines)
- [ ] Git LFS for large payloads
- [ ] PR-based conflict resolution
- [ ] GitHub Actions integration
- [ ] GitLab/Bitbucket support

---

**Peak nuttiness achieved!** 🌰✨
