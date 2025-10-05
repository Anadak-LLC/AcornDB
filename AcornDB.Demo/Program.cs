using AcornDB;
using AcornDB.Storage;
using AcornDB.Models;

Console.WriteLine("🌰 AcornDB Demo");
Console.WriteLine("===============");

// Create a Tree with file storage
var tree = new Tree<User>(new FileTrunk<User>("data/users"));

// Stash some nuts
Console.WriteLine("\n📥 Stashing users...");
tree.Stash("alice", new User("Alice Squirrel"));
tree.Stash("bob", new User("Bob Nutcracker"));
tree.Stash("charlie", new User("Charlie Chipmunk"));

// Crack them back
Console.WriteLine("\n📤 Cracking users...");
var alice = tree.Crack("alice");
var bob = tree.Crack("bob");
Console.WriteLine($"  - {alice?.Name}");
Console.WriteLine($"  - {bob?.Name}");

// Create a Grove and plant the tree
Console.WriteLine("\n🌳 Creating a Grove...");
var grove = new Grove();
grove.Plant(tree);

// Show stats
var retrieved = grove.GetTree<User>();
Console.WriteLine($"  - Retrieved tree from grove: {retrieved != null}");

Console.WriteLine("\n✅ Demo complete! Check the 'data/users' folder for persisted nuts.");
Console.WriteLine("Press any key to exit...");
Console.ReadKey();

record User(string Name);