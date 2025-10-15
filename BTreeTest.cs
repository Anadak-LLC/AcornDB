using System;
using AcornDB;
using AcornDB.Storage;

// Quick test of optimized BTreeTrunk
var trunk = new BTreeTrunk<string>(Path.Combine(Path.GetTempPath(), "btree_test"));

// Test write
for (int i = 0; i < 100; i++)
{
    var nut = new Nut<string>
    {
        Id = $"test-{i}",
        Payload = $"Value {i}",
        Timestamp = DateTime.UtcNow,
        Version = 1
    };
    trunk.Save(nut.Id, nut);
}

Console.WriteLine("Wrote 100 nuts");

// Test read
for (int i = 0; i < 100; i++)
{
    var nut = trunk.Load($"test-{i}");
    if (nut == null || nut.Payload != $"Value {i}")
    {
        Console.WriteLine($"ERROR: Failed to read nut {i}");
        return;
    }
}

Console.WriteLine("Read 100 nuts successfully");

// Test delete
trunk.Delete("test-50");
var deleted = trunk.Load("test-50");
if (deleted != null)
{
    Console.WriteLine("ERROR: Delete failed");
    return;
}

Console.WriteLine("Delete works");

trunk.Dispose();
Console.WriteLine("✓ All tests passed!");
