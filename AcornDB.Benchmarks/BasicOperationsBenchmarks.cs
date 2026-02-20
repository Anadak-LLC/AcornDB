using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using AcornDB;
using AcornDB.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for basic Tree operations across multiple trunks using Params/ParamsSource.
    /// One benchmark method per operation; BenchmarkDotNet runs all combinations.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 5)]
    public class BasicOperationsBenchmarks
    {
        // ----------------------------
        // Models
        // ----------------------------
        public sealed class TestItem
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
            public DateTime Timestamp { get; set; }
        }

        // ----------------------------
        // Parameters
        // ----------------------------

        // Vary item count without duplicating methods
        [Params(1_000)]
        public int ItemCount { get; set; }

        // Trunk cases (extend this list as you add trunks)
        public readonly record struct TrunkCase(string Name, Func<string, ITrunk<TestItem>> Factory)
        {
            public override string ToString() => Name;
        }

        // BDN limitation: ParamsSource must reference a public instance field/property/method
        public IEnumerable<TrunkCase> Trunks => new[]
        {
            new TrunkCase("Memory",  _ => new MemoryTrunk<TestItem>()),
            new TrunkCase("File",    (root) => new FileTrunk<TestItem>(Path.Combine(root, "file"))),
            new TrunkCase("Bitcask", (root) => new BitcaskTrunk<TestItem>(Path.Combine(root, "bitcask"))),
        };

        [ParamsSource(nameof(Trunks))]
        public TrunkCase Case { get; set; }

        // ----------------------------
        // State
        // ----------------------------
        private Tree<TestItem>? _tree;
        private List<TestItem>? _items;
        private List<string>? _ids;

        // Make per-process unique roots to avoid cross-case collisions
        private string _caseRoot = string.Empty;

        // ----------------------------
        // Setup/Cleanup helpers
        // ----------------------------
        private static string NewCaseRoot(string trunkName, int itemCount)
        {
            // Put under a single bench root folder; unique per run + case
            var root = Path.Combine(
                AppContext.BaseDirectory,
                "benchdata",
                $"{trunkName}-{itemCount}",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(root);
            return root;
        }

        private void BuildDataset()
        {
            _items = Enumerable.Range(0, ItemCount).Select(i => new TestItem
            {
                Id = $"item-{i}",
                Name = $"Test Item {i}",
                Value = i,
                Timestamp = DateTime.UtcNow
            }).ToList();

            _ids = _items.Select(x => x.Id).ToList();
        }

        private void CreateTreeFresh()
        {
            DisposeTree();

            _caseRoot = NewCaseRoot(Case.Name, ItemCount);
            var trunk = Case.Factory(_caseRoot);
            _tree = new Tree<TestItem>(trunk);
        }

        private void DisposeTree()
        {
            // If your Tree/Trunk types implement IDisposable, dispose them here.
            // We only know trunks generally implement ITrunk, so dispose opportunistically.
            if (_tree is IDisposable d)
                d.Dispose();

            _tree = null;
        }

        private void CleanupCaseRoot()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_caseRoot) && Directory.Exists(_caseRoot))
                    Directory.Delete(_caseRoot, recursive: true);
            }
            catch
            {
                // Swallow cleanup exceptions; benchmarks should continue
            }
            _caseRoot = string.Empty;
        }

        private void PreloadAll()
        {
            // Ensure we have a fresh tree and write all items
            if (_tree is null || _items is null) throw new InvalidOperationException("Setup not complete.");

            foreach (var item in _items)
                _tree.Stash(item);

            FlushIfSupported();
        }

        private void FlushIfSupported()
        {
            // If your trunks derive from TrunkBase<T> with batching, flush it.
            // Adapt this if your actual API differs.
            if (_tree is null) return;

            // Tree likely wraps a trunk, but Tree API isn’t shown here.
            // If you can get the trunk out of Tree, do it; otherwise ignore.
            // Example if Tree exposes Trunk:
            // if (_tree.Trunk is TrunkBase<TestItem> tb) tb.FlushBatchAsync().GetAwaiter().GetResult();
        }

        // ----------------------------
        // Global setup/cleanup
        // ----------------------------
        [GlobalSetup]
        public void GlobalSetup()
        {
            BuildDataset();
            CreateTreeFresh();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            DisposeTree();

            // Delete entire benchdata root once (optional). Keeping per-case delete is safer.
            // If you prefer nuking everything, do:
            // Directory.Delete(Path.Combine(AppContext.BaseDirectory, "benchdata"), true);

            CleanupCaseRoot();
        }

        // ----------------------------
        // Operation-specific iteration setup
        // ----------------------------

        // Insert should start from empty each iteration for fairness.
        [IterationSetup(Target = nameof(Stash_All))]
        public void IterationSetup_Stash()
        {
            CreateTreeFresh();
        }

        // Read/Update/Delete should start from a loaded state each iteration.
        [IterationSetup(Targets = new[] { nameof(Crack_ById_All), nameof(Toss_All), nameof(Mixed_StashCrackUpdate) })]
        public void IterationSetup_Loaded()
        {
            CreateTreeFresh();
            PreloadAll();
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            DisposeTree();
            CleanupCaseRoot();
        }

        // ----------------------------
        // Benchmarks (one per operation)
        // ----------------------------

        [Benchmark(Description = "Stash: insert N new items")]
        public void Stash_All()
        {
            if (_tree is null || _items is null) throw new InvalidOperationException();

            foreach (var item in _items)
                _tree.Stash(item);

            FlushIfSupported();
        }

        [Benchmark(Description = "Crack: read N items by id")]
        public void Crack_ById_All()
        {
            if (_tree is null || _ids is null) throw new InvalidOperationException();

            // Hot-path: avoid LINQ in benchmarks
            for (int i = 0; i < _ids.Count; i++)
                _ = _tree.Crack(_ids[i]);
        }

        [Benchmark(Description = "Toss: delete N items")]
        public void Toss_All()
        {
            if (_tree is null || _ids is null) throw new InvalidOperationException();

            for (int i = 0; i < _ids.Count; i++)
                _tree.Toss(_ids[i]);

            FlushIfSupported();
        }

        [Benchmark(Description = "Mixed: stash+crack+update for N/2 items")]
        public void Mixed_StashCrackUpdate()
        {
            if (_tree is null) throw new InvalidOperationException();

            int ops = ItemCount / 2;

            for (int i = 0; i < ops; i++)
            {
                var id = $"item-{i}";

                // Stash (insert/update)
                _tree.Stash(new TestItem
                {
                    Id = id,
                    Name = $"Test Item {i}",
                    Value = i,
                    Timestamp = DateTime.UtcNow
                });

                // Crack
                _ = _tree.Crack(id);

                // Update
                _tree.Stash(new TestItem
                {
                    Id = id,
                    Name = $"Updated Item {i}",
                    Value = i * 2,
                    Timestamp = DateTime.UtcNow
                });
            }

            FlushIfSupported();
        }
    }
}