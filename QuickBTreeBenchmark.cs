using System;
using System.Diagnostics;
using AcornDB;
using AcornDB.Storage;

var docCount = 1000;

// Benchmark BTreeTrunk
var dataDir = Path.Combine(Path.GetTempPath(), $"btree_bench_{Guid.NewGuid()}");
Directory.CreateDirectory(dataDir);

var sw = Stopwatch.StartNew();
using (var trunk = new BTreeTrunk<TestDoc>(dataDir))
{
    var tree = new Tree<TestDoc>(trunk);
    tree.TtlEnforcementEnabled = false;
    tree.CacheEvictionEnabled = false;

    for (int i = 0; i < docCount; i++)
    {
        tree.Stash(new TestDoc
        {
            Id = $"doc-{i}",
            Name = $"Document {i}",
            Value = i
        });
    }
}
sw.Stop();
Console.WriteLine($"BTreeTrunk Insert ({docCount} docs): {sw.Elapsed.TotalMilliseconds:F2} ms ({sw.Elapsed.TotalMilliseconds * 1000 / docCount:F2} μs/doc)");

Directory.Delete(dataDir, true);

public class TestDoc
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}
