using System.Diagnostics;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Cache;

Console.WriteLine("🌰 AcornDB Quick Performance Test\n");
Console.WriteLine("=====================================\n");

// Test 1: Stash performance (MemoryTrunk)
var memTree = new Tree<TestItem>(new MemoryTrunk<TestItem>());
var sw = Stopwatch.StartNew();

for (int i = 0; i < 10_000; i++)
{
    memTree.Stash(new TestItem { Id = $"item-{i}", Name = $"Test {i}", Value = i });
}

sw.Stop();
var stashOpsPerSec = 10_000.0 / sw.Elapsed.TotalSeconds;
Console.WriteLine($"✓ Stash (Memory): {stashOpsPerSec:N0} ops/sec ({sw.ElapsedMilliseconds}ms for 10k items)");

// Test 2: Crack performance
sw.Restart();

for (int i = 0; i < 10_000; i++)
{
    var item = memTree.Crack($"item-{i}");
}

sw.Stop();
var crackOpsPerSec = 10_000.0 / sw.Elapsed.TotalSeconds;
Console.WriteLine($"✓ Crack (Memory): {crackOpsPerSec:N0} ops/sec ({sw.ElapsedMilliseconds}ms for 10k items)");

// Test 3: Mixed workload
sw.Restart();

for (int i = 0; i < 5_000; i++)
{
    memTree.Stash(new TestItem { Id = $"mixed-{i}", Name = $"Mixed {i}", Value = i });
    var item = memTree.Crack($"mixed-{i}");
}

sw.Stop();
var mixedOpsPerSec = 10_000.0 / sw.Elapsed.TotalSeconds;
Console.WriteLine($"✓ Mixed (Stash+Crack): {mixedOpsPerSec:N0} ops/sec ({sw.ElapsedMilliseconds}ms for 10k ops)");

// Test 4: LRU Cache eviction performance
var lruTree = new Tree<TestItem>(new MemoryTrunk<TestItem>(), new LRUCacheStrategy<TestItem>(maxSize: 5_000));
sw.Restart();

for (int i = 0; i < 20_000; i++)
{
    lruTree.Stash(new TestItem { Id = $"lru-{i}", Name = $"LRU {i}", Value = i });
}

sw.Stop();
var lruOpsPerSec = 20_000.0 / sw.Elapsed.TotalSeconds;
Console.WriteLine($"✓ LRU Cache (20k items, 5k limit): {lruOpsPerSec:N0} ops/sec ({sw.ElapsedMilliseconds}ms)");
Console.WriteLine($"  Cache size after eviction: {lruTree.NutCount} items");

// Test 5: File trunk performance
var fileTree = new Tree<TestItem>(new FileTrunk<TestItem>());
sw.Restart();

for (int i = 0; i < 1_000; i++)
{
    fileTree.Stash(new TestItem { Id = $"file-{i}", Name = $"File {i}", Value = i });
}

sw.Stop();
var fileOpsPerSec = 1_000.0 / sw.Elapsed.TotalSeconds;
Console.WriteLine($"✓ Stash (File): {fileOpsPerSec:N0} ops/sec ({sw.ElapsedMilliseconds}ms for 1k items)");

// Test 6: In-process sync
var source = new Tree<TestItem>(new MemoryTrunk<TestItem>());
var target = new Tree<TestItem>(new MemoryTrunk<TestItem>());

for (int i = 0; i < 1_000; i++)
{
    source.Stash(new TestItem { Id = $"sync-{i}", Name = $"Sync {i}", Value = i });
}

sw.Restart();
source.Entangle(target);
source.Shake();
sw.Stop();

Console.WriteLine($"✓ In-process Sync: {sw.ElapsedMilliseconds}ms for 1k items");
Console.WriteLine($"  Target received: {target.NutCount} items");

Console.WriteLine("\n📊 Summary:");
Console.WriteLine($"  • Memory ops: {stashOpsPerSec:N0} writes/sec, {crackOpsPerSec:N0} reads/sec");
Console.WriteLine($"  • File ops: {fileOpsPerSec:N0} writes/sec");
Console.WriteLine($"  • LRU eviction: {lruOpsPerSec:N0} ops/sec (with eviction overhead)");
Console.WriteLine($"  • Sync latency: {sw.ElapsedMilliseconds}ms for 1k items");

// Cleanup
if (Directory.Exists("data"))
{
    Directory.Delete("data", true);
}

public class TestItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Value { get; set; }
}
