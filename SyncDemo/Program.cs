using AcornDB;
using AcornDB.Storage;
using AcornDB.Models;
using System.Text.Json;

// Parse process ID from args
var processId = args.Length > 0 ? args[0] : "1";

Console.WriteLine($"🌰 AcornDB P2P Sync Demo - Process {processId}");
Console.WriteLine("==============================================");
Console.WriteLine("File system-based sync (no server required!)");
Console.WriteLine("==============================================\n");

if (processId == "1")
{
    await RunProcess1();
}
else if (processId == "2")
{
    await RunProcess2();
}
else
{
    Console.WriteLine("Usage: dotnet run -- [1|2]");
    Console.WriteLine("  1 = Process 1 (Desktop)");
    Console.WriteLine("  2 = Process 2 (Mobile)");
    return;
}

async Task RunProcess1()
{
    Console.WriteLine("📱 Process 1: Desktop App");
    Console.WriteLine("-------------------------\n");

    // Local storage for this process
    var localTrunk = new DocumentStoreTrunk<User>("data/process1/users");
    var localTree = new Tree<User>(localTrunk);

    // Shared sync hub directory
    var syncHub = new FileSystemSyncHub<User>("data/sync-hub");

    Console.WriteLine("📥 Creating local users...");
    localTree.Stash("alice", new User { Name = "Alice Squirrel", Email = "alice@acorn.db" });
    localTree.Stash("bob", new User { Name = "Bob Nutcracker", Email = "bob@acorn.db" });

    Console.WriteLine($"  ✅ Created: alice");
    Console.WriteLine($"  ✅ Created: bob\n");

    // Initial sync to hub
    Console.WriteLine("🔄 Initial sync to hub...");
    SyncToHub(localTree, syncHub, "process1");
    Console.WriteLine("✅ Synced to hub!\n");

    Console.WriteLine("⏸️  Process 1 is now running...");
    Console.WriteLine("   - Changes are auto-synced every 3 seconds");
    Console.WriteLine("   - Start Process 2 to see live sync!");
    Console.WriteLine("   - Press 'a' to add a new user");
    Console.WriteLine("   - Press 'q' to quit\n");

    // Background sync loop
    var syncTask = Task.Run(async () =>
    {
        while (true)
        {
            await Task.Delay(3000);

            // Export our changes to hub
            SyncToHub(localTree, syncHub, "process1");

            // Import changes from hub
            var imported = SyncFromHub(localTree, syncHub, "process1");
            if (imported > 0)
            {
                Console.WriteLine($"🔽 Imported {imported} change(s) from other processes");
                DisplayUsers(localTree);
            }
        }
    });

    // User interaction loop
    while (true)
    {
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(true);
            if (key.KeyChar == 'q' || key.KeyChar == 'Q')
            {
                Console.WriteLine("\n👋 Process 1 shutting down...");
                break;
            }
            else if (key.KeyChar == 'a' || key.KeyChar == 'A')
            {
                Console.Write("\n📝 Enter user name: ");
                var name = Console.ReadLine() ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var id = name.ToLower().Replace(" ", "");
                    var user = new User { Name = name, Email = $"{id}@acorn.db" };
                    localTree.Stash(id, user);
                    Console.WriteLine($"✅ Added: {name}");
                    SyncToHub(localTree, syncHub, "process1");
                    Console.WriteLine("🔼 Synced to hub\n");
                }
            }
        }
        await Task.Delay(100);
    }
}

async Task RunProcess2()
{
    Console.WriteLine("📱 Process 2: Mobile App");
    Console.WriteLine("------------------------\n");

    // Local storage for this process
    var localTrunk = new DocumentStoreTrunk<User>("data/process2/users");
    var localTree = new Tree<User>(localTrunk);

    // Shared sync hub directory
    var syncHub = new FileSystemSyncHub<User>("data/sync-hub");

    Console.WriteLine("⏳ Waiting 2 seconds for Process 1 to initialize...");
    await Task.Delay(2000);

    Console.WriteLine("🔄 Initial sync from hub...");
    var imported = SyncFromHub(localTree, syncHub, "process2");
    Console.WriteLine($"✅ Imported {imported} user(s) from hub\n");

    DisplayUsers(localTree);

    Console.WriteLine("\n📥 Adding a user from Process 2...");
    localTree.Stash("charlie", new User { Name = "Charlie Chipmunk", Email = "charlie@acorn.db" });
    Console.WriteLine("  ✅ Created: charlie");

    SyncToHub(localTree, syncHub, "process2");
    Console.WriteLine("🔼 Synced to hub\n");

    Console.WriteLine("⏸️  Process 2 is now running...");
    Console.WriteLine("   - Changes are auto-synced every 3 seconds");
    Console.WriteLine("   - Press 'a' to add a new user");
    Console.WriteLine("   - Press 'q' to quit\n");

    // Background sync loop
    var syncTask = Task.Run(async () =>
    {
        while (true)
        {
            await Task.Delay(3000);

            // Export our changes to hub
            SyncToHub(localTree, syncHub, "process2");

            // Import changes from hub
            var newImports = SyncFromHub(localTree, syncHub, "process2");
            if (newImports > 0)
            {
                Console.WriteLine($"🔽 Imported {newImports} change(s) from other processes");
                DisplayUsers(localTree);
            }
        }
    });

    // User interaction loop
    while (true)
    {
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(true);
            if (key.KeyChar == 'q' || key.KeyChar == 'Q')
            {
                Console.WriteLine("\n👋 Process 2 shutting down...");
                break;
            }
            else if (key.KeyChar == 'a' || key.KeyChar == 'A')
            {
                Console.Write("\n📝 Enter user name: ");
                var name = Console.ReadLine() ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var id = name.ToLower().Replace(" ", "");
                    var user = new User { Name = name, Email = $"{id}@acorn.db" };
                    localTree.Stash(id, user);
                    Console.WriteLine($"✅ Added: {name}");
                    SyncToHub(localTree, syncHub, "process2");
                    Console.WriteLine("🔼 Synced to hub\n");
                }
            }
        }
        await Task.Delay(100);
    }
}

void SyncToHub(Tree<User> localTree, FileSystemSyncHub<User> hub, string processId)
{
    var changes = localTree.ExportChanges().ToList();
    hub.PublishChanges(processId, changes);
}

int SyncFromHub(Tree<User> localTree, FileSystemSyncHub<User> hub, string processId)
{
    var changes = hub.PullChanges(processId);
    var localShells = localTree.ExportChanges().ToDictionary(s => s.Id);
    int count = 0;

    foreach (var shell in changes)
    {
        // Only import if newer or doesn't exist
        if (!localShells.TryGetValue(shell.Id, out var existing) ||
            shell.Timestamp > existing.Timestamp)
        {
            localTree.Stash(shell.Id, shell.Payload!);
            count++;
        }
    }

    return count;
}

void DisplayUsers(Tree<User> tree)
{
    var users = tree.ExportChanges().ToList();
    Console.WriteLine($"\n📊 Current users ({users.Count}):");
    foreach (var shell in users.OrderBy(s => s.Id))
    {
        Console.WriteLine($"   👤 {shell.Payload?.Name} ({shell.Id})");
        Console.WriteLine($"      📧 {shell.Payload?.Email}");
        Console.WriteLine($"      🕒 {shell.Timestamp:HH:mm:ss}");
    }
    Console.WriteLine();
}

/// <summary>
/// File system-based sync hub for peer-to-peer synchronization
/// Each process writes to its own file in the hub directory
/// All processes read from all other files
/// </summary>
public class FileSystemSyncHub<T>
{
    private readonly string _hubPath;
    private readonly Dictionary<string, DateTime> _lastSync = new();

    public FileSystemSyncHub(string hubPath)
    {
        _hubPath = hubPath;
        Directory.CreateDirectory(_hubPath);
    }

    /// <summary>
    /// Publish changes from a process to the hub
    /// </summary>
    public void PublishChanges(string processId, IEnumerable<Nut<T>> changes)
    {
        var filePath = Path.Combine(_hubPath, $"{processId}.json");
        var json = JsonSerializer.Serialize(changes, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(filePath, json);
        _lastSync[processId] = DateTime.UtcNow;
    }

    /// <summary>
    /// Pull changes from all other processes in the hub
    /// </summary>
    public IEnumerable<Nut<T>> PullChanges(string currentProcessId)
    {
        var allChanges = new List<Nut<T>>();

        foreach (var file in Directory.GetFiles(_hubPath, "*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);

            // Skip our own file
            if (fileName == currentProcessId)
                continue;

            try
            {
                var json = File.ReadAllText(file);
                var changes = JsonSerializer.Deserialize<List<Nut<T>>>(json);

                if (changes != null)
                {
                    allChanges.AddRange(changes);
                }
            }
            catch
            {
                // File might be locked or corrupted, skip
            }
        }

        return allChanges;
    }
}

public class User
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}
