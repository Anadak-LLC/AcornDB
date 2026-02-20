using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Models;
using AcornDB.Storage.BPlusTree;
using Xunit;

namespace AcornDB.Test
{
    public class BPlusTreeReadPathTests : IDisposable
    {
        private readonly string _testDir;

        public BPlusTreeReadPathTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"acorndb_bpt_read_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }

        private BPlusTreeTrunk<string> CreateTrunk(BPlusTreeOptions? options = null)
        {
            return new BPlusTreeTrunk<string>(
                customPath: _testDir,
                options: options ?? new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 });
        }

        #region Crack (ReadById)

        [Fact]
        public void Crack_EmptyTree_ReturnsNull()
        {
            using var trunk = CreateTrunk();
            var result = trunk.Crack("nonexistent");
            Assert.Null(result);
        }

        [Fact]
        public void Crack_SingleItem_ReturnsCorrectNut()
        {
            using var trunk = CreateTrunk();
            var nut = new Nut<string> { Id = "key1", Payload = "value1" };
            trunk.Stash("key1", nut);

            var result = trunk.Crack("key1");

            Assert.NotNull(result);
            Assert.Equal("key1", result!.Id);
            Assert.Equal("value1", result.Payload);
        }

        [Fact]
        public void Crack_MissingKey_ReturnsNull()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("key1", new Nut<string> { Id = "key1", Payload = "value1" });

            var result = trunk.Crack("key_missing");
            Assert.Null(result);
        }

        [Fact]
        public void Crack_ManyItems_ReturnsCorrectValues()
        {
            using var trunk = CreateTrunk();
            int count = 500;

            for (int i = 0; i < count; i++)
            {
                var id = $"doc-{i:D5}";
                trunk.Stash(id, new Nut<string> { Id = id, Payload = $"payload-{i}" });
            }

            // Read all back in random order
            var rng = new Random(42);
            var indices = Enumerable.Range(0, count).OrderBy(_ => rng.Next()).ToList();

            foreach (var i in indices)
            {
                var id = $"doc-{i:D5}";
                var result = trunk.Crack(id);
                Assert.NotNull(result);
                Assert.Equal(id, result!.Id);
                Assert.Equal($"payload-{i}", result.Payload);
            }
        }

        [Fact]
        public void Crack_AfterUpdate_ReturnsLatestValue()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("key1", new Nut<string> { Id = "key1", Payload = "original" });
            trunk.Stash("key1", new Nut<string> { Id = "key1", Payload = "updated" });

            var result = trunk.Crack("key1");
            Assert.NotNull(result);
            Assert.Equal("updated", result!.Payload);
        }

        [Fact]
        public void Crack_AfterDelete_ReturnsNull()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("key1", new Nut<string> { Id = "key1", Payload = "value1" });
            trunk.Toss("key1");

            var result = trunk.Crack("key1");
            Assert.Null(result);
        }

        #endregion

        #region CrackAll (ScanAll)

        [Fact]
        public void CrackAll_EmptyTree_ReturnsEmpty()
        {
            using var trunk = CreateTrunk();
            var results = trunk.CrackAll();
            Assert.Empty(results);
        }

        [Fact]
        public void CrackAll_ReturnsAllItemsSorted()
        {
            using var trunk = CreateTrunk();
            var ids = new[] { "charlie", "alpha", "bravo", "delta" };

            foreach (var id in ids)
                trunk.Stash(id, new Nut<string> { Id = id, Payload = $"val-{id}" });

            var results = trunk.CrackAll().ToList();

            Assert.Equal(4, results.Count);
            // B+Tree stores keys in sorted (lexicographic byte) order
            var sortedIds = ids.OrderBy(x => x, StringComparer.Ordinal).ToList();
            for (int i = 0; i < sortedIds.Count; i++)
            {
                Assert.Equal(sortedIds[i], results[i].Id);
                Assert.Equal($"val-{sortedIds[i]}", results[i].Payload);
            }
        }

        [Fact]
        public void CrackAll_ManyItems_ReturnsAll()
        {
            using var trunk = CreateTrunk();
            int count = 200;

            for (int i = 0; i < count; i++)
            {
                var id = $"item-{i:D5}";
                trunk.Stash(id, new Nut<string> { Id = id, Payload = $"payload-{i}" });
            }

            var results = trunk.CrackAll().ToList();
            Assert.Equal(count, results.Count);
        }

        #endregion

        #region RangeScan

        [Fact]
        public void RangeScan_ReturnsCorrectSubset()
        {
            using var trunk = CreateTrunk();
            var ids = Enumerable.Range(0, 100).Select(i => $"key-{i:D3}").ToList();

            foreach (var id in ids)
                trunk.Stash(id, new Nut<string> { Id = id, Payload = $"val-{id}" });

            // Range: "key-020" to "key-030" (inclusive)
            var results = trunk.RangeScan("key-020", "key-030").ToList();

            Assert.Equal(11, results.Count); // 020..030 inclusive
            Assert.Equal("key-020", results.First().Id);
            Assert.Equal("key-030", results.Last().Id);
        }

        [Fact]
        public void RangeScan_EmptyRange_ReturnsEmpty()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("a", new Nut<string> { Id = "a", Payload = "val" });
            trunk.Stash("z", new Nut<string> { Id = "z", Payload = "val" });

            // No keys between "b" and "c"
            var results = trunk.RangeScan("b", "c").ToList();
            Assert.Empty(results);
        }

        #endregion

        #region Count

        [Fact]
        public void Count_EmptyTree_ReturnsZero()
        {
            using var trunk = CreateTrunk();
            Assert.Equal(0, trunk.Count);
        }

        [Fact]
        public void Count_AfterInserts_ReturnsCorrectCount()
        {
            using var trunk = CreateTrunk();
            int n = 150;

            for (int i = 0; i < n; i++)
                trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = "v" });

            Assert.Equal(n, trunk.Count);
        }

        #endregion

        #region Concurrent Reads (Snapshot Isolation)

        [Fact]
        public void ConcurrentReads_DoNotCorruptOrDeadlock()
        {
            using var trunk = CreateTrunk();
            int count = 100;

            for (int i = 0; i < count; i++)
            {
                var id = $"doc-{i:D5}";
                trunk.Stash(id, new Nut<string> { Id = id, Payload = $"payload-{i}" });
            }

            // Verify all items readable before concurrent test (this flushes the batch)
            for (int i = 0; i < count; i++)
            {
                var id = $"doc-{i:D5}";
                Assert.NotNull(trunk.Crack(id));
            }

            var errors = new ConcurrentBag<string>();
            int readCount = 0;

            Parallel.For(0, count * 10, new ParallelOptions { MaxDegreeOfParallelism = 8 }, iter =>
            {
                int idx = iter % count;
                var id = $"doc-{idx:D5}";
                var result = trunk.Crack(id);

                if (result == null)
                    errors.Add($"Null result for {id}");
                else if (result.Payload != $"payload-{idx}")
                    errors.Add($"Wrong payload for {id}: {result.Payload}");

                Interlocked.Increment(ref readCount);
            });

            Assert.Empty(errors);
            Assert.Equal(count * 10, readCount);
        }

        #endregion

        #region Restart Persistence (Read after reopen)

        [Fact]
        public void Crack_AfterDisposeAndReopen_ReturnsPersistedData()
        {
            var subDir = Path.Combine(_testDir, "persist");
            Directory.CreateDirectory(subDir);
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };

            // Write data, then dispose
            using (var trunk = new BPlusTreeTrunk<string>(customPath: subDir, options: options))
            {
                for (int i = 0; i < 50; i++)
                {
                    var id = $"persist-{i:D3}";
                    trunk.Stash(id, new Nut<string> { Id = id, Payload = $"value-{i}" });
                }
            }

            // Reopen and read
            using (var trunk = new BPlusTreeTrunk<string>(customPath: subDir, options: options))
            {
                for (int i = 0; i < 50; i++)
                {
                    var id = $"persist-{i:D3}";
                    var result = trunk.Crack(id);
                    Assert.NotNull(result);
                    Assert.Equal(id, result!.Id);
                    Assert.Equal($"value-{i}", result.Payload);
                }

                Assert.Equal(50, trunk.Count);
            }
        }

        #endregion

        #region Page Split Correctness (Large Records Force Splits)

        [Fact]
        public void Crack_AfterManySplits_AllKeysReadable()
        {
            // Use small page size to force many splits
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 32 };
            using var trunk = new BPlusTreeTrunk<string>(customPath: _testDir, options: options);

            int count = 300;
            for (int i = 0; i < count; i++)
            {
                var id = $"split-{i:D5}";
                // Moderate-length payloads to fill pages faster
                trunk.Stash(id, new Nut<string> { Id = id, Payload = new string('x', 100 + (i % 50)) });
            }

            // Verify all keys readable
            for (int i = 0; i < count; i++)
            {
                var id = $"split-{i:D5}";
                var result = trunk.Crack(id);
                Assert.NotNull(result);
                Assert.Equal(id, result!.Id);
                Assert.Equal(100 + (i % 50), result.Payload.Length);
            }
        }

        #endregion
    }
}
