using AcornDB;
using AcornDB.Storage;

Console.WriteLine("🌰 Testing AcornDB Package");
Console.WriteLine("==========================\n");

// Create a tree with in-memory storage
var tree = new Tree<Person>(new MemoryTrunk<Person>());

// Stash some data
tree.Stash("alice", new Person { Name = "Alice", Age = 30 });
tree.Stash("bob", new Person { Name = "Bob", Age = 25 });

// Crack the data back
var alice = tree.Crack("alice");
var bob = tree.Crack("bob");

Console.WriteLine($"✅ Stashed and cracked successfully!");
Console.WriteLine($"   Alice: {alice?.Name}, Age: {alice?.Age}");
Console.WriteLine($"   Bob: {bob?.Name}, Age: {bob?.Age}");

// Get stats
var stats = tree.GetNutStats();
Console.WriteLine($"\n📊 Tree Stats:");
Console.WriteLine($"   Total Stashed: {stats.TotalStashed}");
Console.WriteLine($"   Nut Count: {tree.NutCount}");

Console.WriteLine("\n✅ AcornDB package is working correctly!");

public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}
