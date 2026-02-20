using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcornDB.Models;
using AcornDB.Storage.BPlusTree;
using Xunit;

namespace AcornDB.Test
{
    public class BPlusTreeDeleteTests : IDisposable
    {
        private readonly string _testDir;

        public BPlusTreeDeleteTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"acorndb_bpt_del_{Guid.NewGuid()}");
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

        private BPlusTreeTrunk<string> CreateTrunkInSubDir(string subDir, BPlusTreeOptions? options = null)
        {
            var path = Path.Combine(_testDir, subDir);
            Directory.CreateDirectory(path);
            return new BPlusTreeTrunk<string>(
                customPath: path,
                options: options ?? new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 });
        }

        #region Basic Delete

        [Fact]
        public void Toss_SingleItem_TreeBecomesEmpty()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("key1", new Nut<string> { Id = "key1", Payload = "value1" });
            Assert.Equal(1, trunk.Count);

            trunk.Toss("key1");

            Assert.Equal(0, trunk.Count);
            Assert.Null(trunk.Crack("key1"));
        }

        [Fact]
        public void Toss_NonexistentKey_NoEffect()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("key1", new Nut<string> { Id = "key1", Payload = "value1" });

            trunk.Toss("nonexistent");

            Assert.Equal(1, trunk.Count);
            Assert.NotNull(trunk.Crack("key1"));
        }

        [Fact]
        public void Toss_EmptyTree_NoError()
        {
            using var trunk = CreateTrunk();
            trunk.Toss("anything"); // Should not throw
            Assert.Equal(0, trunk.Count);
        }

        [Fact]
        public void Toss_FirstKey_RemainingKeysIntact()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("a", new Nut<string> { Id = "a", Payload = "va" });
            trunk.Stash("b", new Nut<string> { Id = "b", Payload = "vb" });
            trunk.Stash("c", new Nut<string> { Id = "c", Payload = "vc" });

            trunk.Toss("a");

            Assert.Null(trunk.Crack("a"));
            Assert.Equal("vb", trunk.Crack("b")!.Payload);
            Assert.Equal("vc", trunk.Crack("c")!.Payload);
            Assert.Equal(2, trunk.Count);
        }

        [Fact]
        public void Toss_LastKey_RemainingKeysIntact()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("a", new Nut<string> { Id = "a", Payload = "va" });
            trunk.Stash("b", new Nut<string> { Id = "b", Payload = "vb" });
            trunk.Stash("c", new Nut<string> { Id = "c", Payload = "vc" });

            trunk.Toss("c");

            Assert.Equal("va", trunk.Crack("a")!.Payload);
            Assert.Equal("vb", trunk.Crack("b")!.Payload);
            Assert.Null(trunk.Crack("c"));
            Assert.Equal(2, trunk.Count);
        }

        [Fact]
        public void Toss_MiddleKey_RemainingKeysIntact()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("a", new Nut<string> { Id = "a", Payload = "va" });
            trunk.Stash("b", new Nut<string> { Id = "b", Payload = "vb" });
            trunk.Stash("c", new Nut<string> { Id = "c", Payload = "vc" });

            trunk.Toss("b");

            Assert.Equal("va", trunk.Crack("a")!.Payload);
            Assert.Null(trunk.Crack("b"));
            Assert.Equal("vc", trunk.Crack("c")!.Payload);
            Assert.Equal(2, trunk.Count);
        }

        #endregion

        #region Bulk Delete

        [Fact]
        public void Toss_DeleteAllKeys_TreeBecomesEmpty()
        {
            using var trunk = CreateTrunk();
            int count = 200;

            for (int i = 0; i < count; i++)
                trunk.Stash($"key-{i:D5}", new Nut<string> { Id = $"key-{i:D5}", Payload = $"val-{i}" });

            Assert.Equal(count, trunk.Count);

            for (int i = 0; i < count; i++)
                trunk.Toss($"key-{i:D5}");

            Assert.Equal(0, trunk.Count);
            Assert.Empty(trunk.CrackAll());
        }

        [Fact]
        public void Toss_DeleteAllKeysReverseOrder_TreeBecomesEmpty()
        {
            using var trunk = CreateTrunk();
            int count = 200;

            for (int i = 0; i < count; i++)
                trunk.Stash($"key-{i:D5}", new Nut<string> { Id = $"key-{i:D5}", Payload = $"val-{i}" });

            // Delete in reverse order
            for (int i = count - 1; i >= 0; i--)
                trunk.Toss($"key-{i:D5}");

            Assert.Equal(0, trunk.Count);
        }

        [Fact]
        public void Toss_DeleteHalf_RemainingHalfIntact()
        {
            using var trunk = CreateTrunk();
            int count = 300;

            for (int i = 0; i < count; i++)
                trunk.Stash($"key-{i:D5}", new Nut<string> { Id = $"key-{i:D5}", Payload = $"val-{i}" });

            // Delete even-indexed keys
            for (int i = 0; i < count; i += 2)
                trunk.Toss($"key-{i:D5}");

            Assert.Equal(count / 2, trunk.Count);

            // Verify deleted keys are gone
            for (int i = 0; i < count; i += 2)
                Assert.Null(trunk.Crack($"key-{i:D5}"));

            // Verify remaining keys are intact
            for (int i = 1; i < count; i += 2)
            {
                var result = trunk.Crack($"key-{i:D5}");
                Assert.NotNull(result);
                Assert.Equal($"val-{i}", result!.Payload);
            }
        }

        [Fact]
        public void Toss_DeleteRandomSubset_RemainingIntact()
        {
            using var trunk = CreateTrunk();
            int count = 200;
            var rng = new Random(42);

            for (int i = 0; i < count; i++)
                trunk.Stash($"key-{i:D5}", new Nut<string> { Id = $"key-{i:D5}", Payload = $"val-{i}" });

            // Randomly select half to delete
            var allIndices = Enumerable.Range(0, count).OrderBy(_ => rng.Next()).ToList();
            var toDelete = new HashSet<int>(allIndices.Take(count / 2));

            foreach (var i in toDelete)
                trunk.Toss($"key-{i:D5}");

            Assert.Equal(count - toDelete.Count, trunk.Count);

            for (int i = 0; i < count; i++)
            {
                var result = trunk.Crack($"key-{i:D5}");
                if (toDelete.Contains(i))
                    Assert.Null(result);
                else
                {
                    Assert.NotNull(result);
                    Assert.Equal($"val-{i}", result!.Payload);
                }
            }
        }

        #endregion

        #region Delete + Reinsert

        [Fact]
        public void Toss_ThenReinsert_NewValueVisible()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("key1", new Nut<string> { Id = "key1", Payload = "original" });
            trunk.Toss("key1");
            trunk.Stash("key1", new Nut<string> { Id = "key1", Payload = "reinserted" });

            var result = trunk.Crack("key1");
            Assert.NotNull(result);
            Assert.Equal("reinserted", result!.Payload);
        }

        [Fact]
        public void Toss_DeleteAllThenReinsertAll_AllVisible()
        {
            using var trunk = CreateTrunk();
            int count = 100;

            // Insert
            for (int i = 0; i < count; i++)
                trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = $"v1-{i}" });

            // Delete all
            for (int i = 0; i < count; i++)
                trunk.Toss($"k-{i:D5}");
            Assert.Equal(0, trunk.Count);

            // Reinsert with different values
            for (int i = 0; i < count; i++)
                trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = $"v2-{i}" });

            Assert.Equal(count, trunk.Count);

            for (int i = 0; i < count; i++)
            {
                var result = trunk.Crack($"k-{i:D5}");
                Assert.NotNull(result);
                Assert.Equal($"v2-{i}", result!.Payload);
            }
        }

        #endregion

        #region Delete + Scan Interactions

        [Fact]
        public void CrackAll_AfterDeletes_ReturnsOnlyRemaining()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("a", new Nut<string> { Id = "a", Payload = "va" });
            trunk.Stash("b", new Nut<string> { Id = "b", Payload = "vb" });
            trunk.Stash("c", new Nut<string> { Id = "c", Payload = "vc" });
            trunk.Stash("d", new Nut<string> { Id = "d", Payload = "vd" });

            trunk.Toss("b");
            trunk.Toss("d");

            var results = trunk.CrackAll().ToList();
            Assert.Equal(2, results.Count);
            Assert.Equal("a", results[0].Id);
            Assert.Equal("c", results[1].Id);
        }

        [Fact]
        public void RangeScan_AfterDeletes_SkipsDeletedKeys()
        {
            using var trunk = CreateTrunk();
            for (int i = 0; i < 50; i++)
                trunk.Stash($"key-{i:D3}", new Nut<string> { Id = $"key-{i:D3}", Payload = $"val-{i}" });

            // Delete keys 010-019
            for (int i = 10; i < 20; i++)
                trunk.Toss($"key-{i:D3}");

            // Range scan across the deleted region
            var results = trunk.RangeScan("key-005", "key-025").ToList();

            // Should have 005-009 (5) + 020-025 (6) = 11
            Assert.Equal(11, results.Count);
            Assert.Equal("key-005", results.First().Id);
            Assert.Equal("key-025", results.Last().Id);

            // Verify none of the deleted keys are present
            var ids = results.Select(r => r.Id).ToHashSet();
            for (int i = 10; i < 20; i++)
                Assert.DoesNotContain($"key-{i:D3}", ids);
        }

        #endregion

        #region Interleaved Insert/Delete

        [Fact]
        public void InterleavedInsertDelete_MaintainsCorrectState()
        {
            using var trunk = CreateTrunk();
            var expected = new Dictionary<string, string>();

            // Interleave inserts and deletes
            for (int i = 0; i < 200; i++)
            {
                var id = $"item-{i:D5}";
                trunk.Stash(id, new Nut<string> { Id = id, Payload = $"val-{i}" });
                expected[id] = $"val-{i}";

                // Delete every 3rd item that was previously inserted
                if (i >= 3 && i % 3 == 0)
                {
                    var delId = $"item-{i - 3:D5}";
                    trunk.Toss(delId);
                    expected.Remove(delId);
                }
            }

            Assert.Equal(expected.Count, trunk.Count);

            foreach (var (id, payload) in expected)
            {
                var result = trunk.Crack(id);
                Assert.NotNull(result);
                Assert.Equal(payload, result!.Payload);
            }
        }

        #endregion

        #region Delete with Page Splits (multi-level tree)

        [Fact]
        public void Toss_AfterManySplits_CorrectAfterDelete()
        {
            // Small page size forces many splits -> multi-level tree
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 32 };
            using var trunk = new BPlusTreeTrunk<string>(customPath: _testDir, options: options);

            int count = 500;
            for (int i = 0; i < count; i++)
            {
                var id = $"doc-{i:D5}";
                trunk.Stash(id, new Nut<string> { Id = id, Payload = new string('x', 80 + (i % 40)) });
            }

            // Delete every other key
            for (int i = 0; i < count; i += 2)
                trunk.Toss($"doc-{i:D5}");

            Assert.Equal(count / 2, trunk.Count);

            // Verify remaining keys
            for (int i = 1; i < count; i += 2)
            {
                var result = trunk.Crack($"doc-{i:D5}");
                Assert.NotNull(result);
                Assert.Equal(80 + (i % 40), result!.Payload.Length);
            }

            // Verify deleted keys are gone
            for (int i = 0; i < count; i += 2)
                Assert.Null(trunk.Crack($"doc-{i:D5}"));
        }

        #endregion

        #region Delete Persistence (Restart)

        [Fact]
        public void Toss_PersistsAcrossRestart()
        {
            var subDir = Path.Combine(_testDir, "persist_del");
            Directory.CreateDirectory(subDir);
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };

            // Insert and delete, then close
            using (var trunk = new BPlusTreeTrunk<string>(customPath: subDir, options: options))
            {
                for (int i = 0; i < 100; i++)
                    trunk.Stash($"k-{i:D3}", new Nut<string> { Id = $"k-{i:D3}", Payload = $"v-{i}" });

                // Delete subset
                for (int i = 0; i < 100; i += 3)
                    trunk.Toss($"k-{i:D3}");
            }

            // Reopen and verify
            using (var trunk = new BPlusTreeTrunk<string>(customPath: subDir, options: options))
            {
                for (int i = 0; i < 100; i++)
                {
                    var result = trunk.Crack($"k-{i:D3}");
                    if (i % 3 == 0)
                        Assert.Null(result);
                    else
                    {
                        Assert.NotNull(result);
                        Assert.Equal($"v-{i}", result!.Payload);
                    }
                }

                // 0,3,6,...,99 → floor(99/3)+1 = 34 items deleted
                Assert.Equal(100 - 34, trunk.Count);
            }
        }

        [Fact]
        public void Toss_DeleteAll_PersistsEmptyTree()
        {
            var subDir = Path.Combine(_testDir, "persist_empty");
            Directory.CreateDirectory(subDir);
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };

            using (var trunk = new BPlusTreeTrunk<string>(customPath: subDir, options: options))
            {
                for (int i = 0; i < 50; i++)
                    trunk.Stash($"k-{i:D3}", new Nut<string> { Id = $"k-{i:D3}", Payload = $"v-{i}" });

                for (int i = 0; i < 50; i++)
                    trunk.Toss($"k-{i:D3}");

                Assert.Equal(0, trunk.Count);
            }

            using (var trunk = new BPlusTreeTrunk<string>(customPath: subDir, options: options))
            {
                Assert.Equal(0, trunk.Count);
                Assert.Null(trunk.Crack("k-000"));
                Assert.Empty(trunk.CrackAll());
            }
        }

        #endregion

        #region Double Delete

        [Fact]
        public void Toss_SameKeyTwice_SecondCallNoOp()
        {
            using var trunk = CreateTrunk();
            trunk.Stash("key1", new Nut<string> { Id = "key1", Payload = "v" });
            trunk.Stash("key2", new Nut<string> { Id = "key2", Payload = "v" });

            trunk.Toss("key1");
            trunk.Toss("key1"); // Should not throw or corrupt

            Assert.Equal(1, trunk.Count);
            Assert.Null(trunk.Crack("key1"));
            Assert.NotNull(trunk.Crack("key2"));
        }

        #endregion

        #region Count After Delete

        [Fact]
        public void Count_DecreasesAfterDelete()
        {
            using var trunk = CreateTrunk();

            for (int i = 0; i < 10; i++)
                trunk.Stash($"k{i}", new Nut<string> { Id = $"k{i}", Payload = "v" });

            Assert.Equal(10, trunk.Count);

            trunk.Toss("k5");
            Assert.Equal(9, trunk.Count);

            trunk.Toss("k0");
            Assert.Equal(8, trunk.Count);

            trunk.Toss("k9");
            Assert.Equal(7, trunk.Count);
        }

        #endregion

        #region Merge/Redistribution Tests

        [Fact]
        public void Merge_DeleteMostEntries_TreeShrinks()
        {
            // Small page size to force many splits, then delete most entries
            // triggering merges as leaves become underfull
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };
            using var trunk = CreateTrunk(options);
            int count = 300;

            for (int i = 0; i < count; i++)
                trunk.Stash($"key-{i:D5}", new Nut<string> { Id = $"key-{i:D5}", Payload = $"val-{i}" });

            Assert.Equal(count, trunk.Count);

            // Delete 90% of entries to trigger many merges
            for (int i = 0; i < count; i++)
            {
                if (i % 10 != 0) // Keep every 10th entry
                    trunk.Toss($"key-{i:D5}");
            }

            int expectedRemaining = (count + 9) / 10; // ceil(300/10) = 30
            Assert.Equal(expectedRemaining, trunk.Count);

            // Verify remaining entries
            for (int i = 0; i < count; i += 10)
            {
                var result = trunk.Crack($"key-{i:D5}");
                Assert.NotNull(result);
                Assert.Equal($"val-{i}", result!.Payload);
            }

            // Scan should return entries in order
            var all = trunk.CrackAll().ToList();
            Assert.Equal(expectedRemaining, all.Count);
            for (int j = 0; j < all.Count - 1; j++)
                Assert.True(string.Compare(all[j].Id, all[j + 1].Id, StringComparison.Ordinal) < 0);
        }

        [Fact]
        public void Merge_DeleteFromLeftEdge_ScanRemainsOrdered()
        {
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };
            using var trunk = CreateTrunk(options);

            for (int i = 0; i < 200; i++)
                trunk.Stash($"key-{i:D5}", new Nut<string> { Id = $"key-{i:D5}", Payload = $"val-{i}" });

            // Delete the first 150 keys, which should trigger leftmost leaf merges
            for (int i = 0; i < 150; i++)
                trunk.Toss($"key-{i:D5}");

            Assert.Equal(50, trunk.Count);

            var all = trunk.CrackAll().ToList();
            Assert.Equal(50, all.Count);
            Assert.Equal("key-00150", all[0].Id);
            Assert.Equal("key-00199", all[49].Id);
        }

        [Fact]
        public void Merge_DeleteFromRightEdge_ScanRemainsOrdered()
        {
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };
            using var trunk = CreateTrunk(options);

            for (int i = 0; i < 200; i++)
                trunk.Stash($"key-{i:D5}", new Nut<string> { Id = $"key-{i:D5}", Payload = $"val-{i}" });

            // Delete the last 150 keys
            for (int i = 50; i < 200; i++)
                trunk.Toss($"key-{i:D5}");

            Assert.Equal(50, trunk.Count);

            var all = trunk.CrackAll().ToList();
            Assert.Equal(50, all.Count);
            Assert.Equal("key-00000", all[0].Id);
            Assert.Equal("key-00049", all[49].Id);
        }

        [Fact]
        public void Merge_AlternatingDelete_LeafChainCorrect()
        {
            // Deleting alternating keys forces many underfull leaves that should merge/redistribute
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };
            using var trunk = CreateTrunk(options);

            for (int i = 0; i < 400; i++)
                trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = new string('x', 50) });

            // Delete every other key
            for (int i = 0; i < 400; i += 2)
                trunk.Toss($"k-{i:D5}");

            Assert.Equal(200, trunk.Count);

            // Verify scan uses leaf chain correctly (all remaining entries in order)
            var all = trunk.CrackAll().ToList();
            Assert.Equal(200, all.Count);

            for (int j = 0; j < all.Count - 1; j++)
                Assert.True(string.Compare(all[j].Id, all[j + 1].Id, StringComparison.Ordinal) < 0);

            // Range scan should also work
            var range = trunk.RangeScan("k-00050", "k-00150").ToList();
            foreach (var item in range)
            {
                int idx = int.Parse(item.Id.Substring(2));
                Assert.True(idx % 2 == 1); // Only odd indices remain
                Assert.True(idx >= 50 && idx <= 150);
            }
        }

        [Fact]
        public void Merge_DeleteAndReinsert_AfterMerge_Correct()
        {
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };
            using var trunk = CreateTrunk(options);

            // Insert, then delete most (triggering merges), then reinsert
            for (int i = 0; i < 200; i++)
                trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = $"v1-{i}" });

            for (int i = 0; i < 180; i++)
                trunk.Toss($"k-{i:D5}");

            Assert.Equal(20, trunk.Count);

            // Reinsert deleted keys with new values
            for (int i = 0; i < 180; i++)
                trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = $"v2-{i}" });

            Assert.Equal(200, trunk.Count);

            // Verify all values correct
            for (int i = 0; i < 180; i++)
            {
                var result = trunk.Crack($"k-{i:D5}");
                Assert.NotNull(result);
                Assert.Equal($"v2-{i}", result!.Payload);
            }
            for (int i = 180; i < 200; i++)
            {
                var result = trunk.Crack($"k-{i:D5}");
                Assert.NotNull(result);
                Assert.Equal($"v1-{i}", result!.Payload);
            }
        }

        [Fact]
        public void Merge_PersistsAcrossRestart()
        {
            var subDir = Path.Combine(_testDir, "persist_merge");
            Directory.CreateDirectory(subDir);
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };

            using (var trunk = new BPlusTreeTrunk<string>(customPath: subDir, options: options))
            {
                for (int i = 0; i < 200; i++)
                    trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = $"v-{i}" });

                // Delete 90% to trigger merges
                for (int i = 0; i < 200; i++)
                {
                    if (i % 10 != 0)
                        trunk.Toss($"k-{i:D5}");
                }
            }

            // Reopen and verify
            using (var trunk = new BPlusTreeTrunk<string>(customPath: subDir, options: options))
            {
                Assert.Equal(20, trunk.Count);

                for (int i = 0; i < 200; i += 10)
                {
                    var result = trunk.Crack($"k-{i:D5}");
                    Assert.NotNull(result);
                    Assert.Equal($"v-{i}", result!.Payload);
                }

                var all = trunk.CrackAll().ToList();
                Assert.Equal(20, all.Count);
                for (int j = 0; j < all.Count - 1; j++)
                    Assert.True(string.Compare(all[j].Id, all[j + 1].Id, StringComparison.Ordinal) < 0);
            }
        }

        [Fact]
        public void Merge_DeleteAll_ViaSmallBatches_TreeBecomesEmpty()
        {
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };
            using var trunk = CreateTrunk(options);

            for (int i = 0; i < 500; i++)
                trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = new string('a', 30) });

            // Delete in small batches of 10, interleaved with count checks
            for (int batch = 0; batch < 50; batch++)
            {
                for (int j = 0; j < 10; j++)
                {
                    int idx = batch * 10 + j;
                    trunk.Toss($"k-{idx:D5}");
                }
                Assert.Equal(500 - (batch + 1) * 10, trunk.Count);
            }

            Assert.Equal(0, trunk.Count);
            Assert.Empty(trunk.CrackAll());
        }

        [Fact]
        public void Merge_LargeValues_MergeAndRedistribute()
        {
            // With large values, fewer entries per page — merges and redistributions
            // happen more frequently
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };
            using var trunk = CreateTrunk(options);

            int count = 100;
            for (int i = 0; i < count; i++)
                trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = new string('x', 200 + (i % 100)) });

            // Delete 75%
            for (int i = 0; i < count; i++)
            {
                if (i % 4 != 0)
                    trunk.Toss($"k-{i:D5}");
            }

            int expected = (count + 3) / 4;
            Assert.Equal(expected, trunk.Count);

            for (int i = 0; i < count; i += 4)
            {
                var result = trunk.Crack($"k-{i:D5}");
                Assert.NotNull(result);
                Assert.Equal(200 + (i % 100), result!.Payload.Length);
            }
        }

        [Fact]
        public void Merge_RangeScan_AfterHeavyMerges_Correct()
        {
            var options = new BPlusTreeOptions { PageSize = 4096, MaxCachePages = 64 };
            using var trunk = CreateTrunk(options);

            for (int i = 0; i < 300; i++)
                trunk.Stash($"key-{i:D5}", new Nut<string> { Id = $"key-{i:D5}", Payload = $"val-{i}" });

            // Delete middle 200 entries (50-249), keeping 50 at start and 50 at end
            for (int i = 50; i < 250; i++)
                trunk.Toss($"key-{i:D5}");

            Assert.Equal(100, trunk.Count);

            // Range scan across the deleted region
            var range = trunk.RangeScan("key-00040", "key-00260").ToList();
            // Should have keys 40-49 (10) + 250-260 (11) = 21
            Assert.Equal(21, range.Count);
            Assert.Equal("key-00040", range.First().Id);
            Assert.Equal("key-00260", range.Last().Id);
        }

        #endregion
    }
}
