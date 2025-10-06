using AcornDB;
using AcornDB.Storage;
using AcornDB.Models;

Console.WriteLine("🌰 AcornDB Trunk Abstraction Demo");
Console.WriteLine("==================================\n");

// Demo 1: FileTrunk (Simple, no history)
Console.WriteLine("📁 Demo 1: FileTrunk (Simple file storage)");
Console.WriteLine("------------------------------------------");
var fileTree = new Tree<User>(new FileTrunk<User>("data/demo-file"));
fileTree.Stash("alice", new User("Alice Squirrel"));
fileTree.Stash("bob", new User("Bob Nutcracker"));
Console.WriteLine($"  ✅ Stashed: {fileTree.Crack("alice")?.Name}");

try
{
    var fileHistory = fileTree.GetHistory("alice");
    Console.WriteLine($"  📜 History: {fileHistory.Count} versions");
}
catch (NotSupportedException)
{
    Console.WriteLine("  ⚠️ FileTrunk does not support history");
}

// Demo 2: MemoryTrunk (Fast, non-durable)
Console.WriteLine("\n💾 Demo 2: MemoryTrunk (In-memory storage)");
Console.WriteLine("------------------------------------------");
var memoryTree = new Tree<User>(new MemoryTrunk<User>());
memoryTree.Stash("charlie", new User("Charlie Chipmunk"));
Console.WriteLine($"  ✅ Stashed: {memoryTree.Crack("charlie")?.Name}");
Console.WriteLine("  ⚡ Fast but non-durable (data lost on restart)");

// Demo 3: DocumentStoreTrunk (Full history & versioning)
Console.WriteLine("\n📚 Demo 3: DocumentStoreTrunk (Versioned storage)");
Console.WriteLine("--------------------------------------------------");
var docTree = new Tree<User>(new DocumentStoreTrunk<User>("data/demo-docstore"));
docTree.Stash("dave", new User("Dave Oak"));
docTree.Stash("dave", new User("Dave Oak (Updated)"));
docTree.Stash("dave", new User("Dave Oak (Final)"));

var current = docTree.Crack("dave");
var docHistory = docTree.GetHistory("dave");
Console.WriteLine($"  ✅ Current: {current?.Name}");
Console.WriteLine($"  📜 History: {docHistory.Count} previous versions");

// Demo 4: Export/Import between trunks
Console.WriteLine("\n🔄 Demo 4: Export/Import between trunks");
Console.WriteLine("----------------------------------------");
var sourceTree = new Tree<User>(new FileTrunk<User>("data/export-demo"));
sourceTree.Stash("user1", new User("Export User"));

var targetTrunk = new MemoryTrunk<User>();
targetTrunk.ImportChanges(sourceTree.ExportChanges());

var targetTree = new Tree<User>(targetTrunk);
Console.WriteLine($"  ✅ Exported and imported: {targetTree.Crack("user1")?.Name}");

// Demo 5: Grove with multiple tree types
Console.WriteLine("\n🌳 Demo 5: Grove with mixed trunks");
Console.WriteLine("-----------------------------------");
var grove = new Grove();
grove.Plant(new Tree<User>(new FileTrunk<User>("data/grove-users")));
grove.Plant(new Tree<Product>(new MemoryTrunk<Product>()));

var userTree = grove.GetTree<User>();
var productTree = grove.GetTree<Product>();
userTree?.Stash("admin", new User("Admin User"));
productTree?.Stash("acorn-1", new Product("Golden Acorn"));

Console.WriteLine($"  ✅ Grove has {grove.TreeCount} trees");
Console.WriteLine($"  - Users: {userTree?.Crack("admin")?.Name}");
Console.WriteLine($"  - Products: {productTree?.Crack("acorn-1")?.Name}");

Console.WriteLine("\n✅ All demos complete!");
Console.WriteLine("📂 Check 'data/' folders for persisted files");

record User(string Name);
record Product(string Name);