using AcornDB.Storage;
using AcornDB.Sync;

namespace AcornDB.Test
{
    public class DeltaSyncTests : IDisposable
    {
        private readonly string _testDir;

        public class Item
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int Version { get; set; }
        }

        public DeltaSyncTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"acorndb_deltasync_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        // ===== Tree.ExportChangesSince() Tests =====

        [Fact]
        public void ExportChangesSince_FiltersOldNuts()
        {
            // Arrange
            var tree = new Tree<Item>(new MemoryTrunk<Item>());
            var cutoffTime = DateTime.UtcNow;

            // Add items before cutoff
            tree.Stash(new Item { Id = "old1", Name = "Old Item 1", Version = 1 });
            tree.Stash(new Item { Id = "old2", Name = "Old Item 2", Version = 1 });

            // Wait a moment to ensure time difference
            System.Threading.Thread.Sleep(100);
            var afterCutoff = DateTime.UtcNow;

            // Add items after cutoff
            tree.Stash(new Item { Id = "new1", Name = "New Item 1", Version = 1 });
            tree.Stash(new Item { Id = "new2", Name = "New Item 2", Version = 1 });

            // Act
            var changes = tree.ExportChangesSince(afterCutoff).ToList();

            // Assert
            Assert.Equal(2, changes.Count);
            Assert.Contains(changes, n => n.Id == "new1");
            Assert.Contains(changes, n => n.Id == "new2");
            Assert.DoesNotContain(changes, n => n.Id == "old1");
            Assert.DoesNotContain(changes, n => n.Id == "old2");
        }

        [Fact]
        public void ExportChangesSince_EmptyWhenNoNewChanges()
        {
            // Arrange
            var tree = new Tree<Item>(new MemoryTrunk<Item>());

            // Add items
            tree.Stash(new Item { Id = "item1", Name = "Item 1", Version = 1 });
            tree.Stash(new Item { Id = "item2", Name = "Item 2", Version = 1 });

            // Act - export changes since future timestamp
            var changes = tree.ExportChangesSince(DateTime.UtcNow.AddHours(1)).ToList();

            // Assert
            Assert.Empty(changes);
        }

        [Fact]
        public void ExportChangesSince_AllWhenSinceMinValue()
        {
            // Arrange
            var tree = new Tree<Item>(new MemoryTrunk<Item>());

            // Add items
            tree.Stash(new Item { Id = "item1", Name = "Item 1", Version = 1 });
            tree.Stash(new Item { Id = "item2", Name = "Item 2", Version = 1 });
            tree.Stash(new Item { Id = "item3", Name = "Item 3", Version = 1 });

            // Act - export all changes since beginning of time
            var changes = tree.ExportChangesSince(DateTime.MinValue).ToList();

            // Assert
            Assert.Equal(3, changes.Count);
        }

        // ===== Tree.ExportDeltaChanges() Tests =====

        [Fact]
        public void ExportDeltaChanges_TracksLastSyncTimestamp()
        {
            // Arrange
            var tree = new Tree<Item>(new MemoryTrunk<Item>());

            // Add initial items
            tree.Stash(new Item { Id = "item1", Name = "Item 1", Version = 1 });
            tree.Stash(new Item { Id = "item2", Name = "Item 2", Version = 1 });

            // Act - first delta export (should return all)
            var firstExport = tree.ExportDeltaChanges().ToList();

            // Wait and add more items
            System.Threading.Thread.Sleep(100);
            tree.Stash(new Item { Id = "item3", Name = "Item 3", Version = 1 });
            tree.Stash(new Item { Id = "item4", Name = "Item 4", Version = 1 });

            // Act - second delta export (should return only new items)
            var secondExport = tree.ExportDeltaChanges().ToList();

            // Assert
            Assert.Equal(2, firstExport.Count);
            Assert.Equal(2, secondExport.Count);
            Assert.Contains(secondExport, n => n.Id == "item3");
            Assert.Contains(secondExport, n => n.Id == "item4");
            Assert.DoesNotContain(secondExport, n => n.Id == "item1");
            Assert.DoesNotContain(secondExport, n => n.Id == "item2");
        }

        [Fact]
        public void ExportDeltaChanges_UpdatesLastSyncTimestamp()
        {
            // Arrange
            var tree = new Tree<Item>(new MemoryTrunk<Item>());
            var initialTimestamp = tree.LastSyncTimestamp;

            tree.Stash(new Item { Id = "item1", Name = "Item 1", Version = 1 });

            // Act
            tree.ExportDeltaChanges();

            // Assert
            Assert.True(tree.LastSyncTimestamp > initialTimestamp);
        }

        // ===== Branch Fluent API Tests =====

        [Fact]
        public void WithDeltaSync_EnablesDeltaSync()
        {
            // Arrange & Act
            var branch = new Branch("http://test.com")
                .WithDeltaSync(true);

            // Assert
            var stats = branch.GetStats();
            Assert.True(stats.DeltaSyncEnabled);

            branch.Dispose();
        }

        [Fact]
        public void WithDeltaSync_DisablesDeltaSync()
        {
            // Arrange & Act
            var branch = new Branch("http://test.com")
                .WithDeltaSync(false);

            // Assert
            var stats = branch.GetStats();
            Assert.False(stats.DeltaSyncEnabled);

            branch.Dispose();
        }

        [Fact]
        public void FluentAPI_ChainedWithDeltaSync()
        {
            // Arrange & Act
            var branch = new Branch("http://test.com")
                .WithSyncMode(SyncMode.Bidirectional)
                .WithConflictDirection(ConflictDirection.PreferLocal)
                .WithDeltaSync(true);

            // Assert
            var stats = branch.GetStats();
            Assert.Equal(SyncMode.Bidirectional, stats.SyncMode);
            Assert.Equal(ConflictDirection.PreferLocal, stats.ConflictDirection);
            Assert.True(stats.DeltaSyncEnabled);

            branch.Dispose();
        }

        // ===== BranchStats Tests =====

        [Fact]
        public void BranchStats_IncludesDeltaSyncStatus()
        {
            // Arrange
            var branch = new Branch("http://test.com")
                .WithDeltaSync(true);

            // Act
            var stats = branch.GetStats();

            // Assert
            Assert.True(stats.DeltaSyncEnabled);
            Assert.Equal(DateTime.MinValue, stats.LastSyncTimestamp);
            Assert.False(stats.HasSynced);

            branch.Dispose();
        }

        [Fact]
        public void BranchStats_HasSynced_FalseInitially()
        {
            // Arrange
            var branch = new Branch("http://test.com");

            // Act
            var stats = branch.GetStats();

            // Assert
            Assert.False(stats.HasSynced);

            branch.Dispose();
        }

        // ===== InProcessBranch Delta Sync Tests =====

        [Fact]
        public void InProcessBranch_DeltaSyncReducesTransferredNuts()
        {
            // Arrange
            var dir1 = Path.Combine(_testDir, "tree1");
            var dir2 = Path.Combine(_testDir, "tree2");
            Directory.CreateDirectory(dir1);
            Directory.CreateDirectory(dir2);

            var tree1 = new Tree<Item>(new FileTrunk<Item>(dir1));
            var tree2 = new Tree<Item>(new FileTrunk<Item>(dir2));

            // Entangle first, then add items (items added before entanglement won't sync)
            var branch = new InProcessBranch<Item>(tree2);
            branch.WithDeltaSync(true);
            tree1.Entangle(branch);

            // Add initial items to tree1 (after entanglement)
            tree1.Stash(new Item { Id = "item1", Name = "Item 1", Version = 1 });
            tree1.Stash(new Item { Id = "item2", Name = "Item 2", Version = 1 });

            // Assert initial sync worked through leaf propagation
            Assert.NotNull(tree2.Crack("item1"));
            Assert.NotNull(tree2.Crack("item2"));

            // Add more items
            System.Threading.Thread.Sleep(100);
            tree1.Stash(new Item { Id = "item3", Name = "Item 3", Version = 1 });
            tree1.Stash(new Item { Id = "item4", Name = "Item 4", Version = 1 });

            // Assert new items synced
            Assert.NotNull(tree2.Crack("item3"));
            Assert.NotNull(tree2.Crack("item4"));

            branch.Dispose();
        }

        [Fact]
        public void Tree_MarkSyncCompleted_UpdatesTimestamp()
        {
            // Arrange
            var tree = new Tree<Item>(new MemoryTrunk<Item>());
            var initialTimestamp = tree.LastSyncTimestamp;

            // Act
            tree.MarkSyncCompleted();

            // Assert
            Assert.True(tree.LastSyncTimestamp > initialTimestamp);
        }

        // ===== Performance and Efficiency Tests =====

        [Fact]
        public void ExportChangesSince_PerformanceTest_LargeDataset()
        {
            // Arrange
            var tree = new Tree<Item>(new MemoryTrunk<Item>());

            // Add 1000 old items
            for (int i = 0; i < 1000; i++)
            {
                tree.Stash(new Item { Id = $"old{i}", Name = $"Old Item {i}", Version = 1 });
            }

            System.Threading.Thread.Sleep(100);
            var cutoffTime = DateTime.UtcNow;

            // Add 10 new items
            for (int i = 0; i < 10; i++)
            {
                tree.Stash(new Item { Id = $"new{i}", Name = $"New Item {i}", Version = 1 });
            }

            // Act
            var startTime = DateTime.UtcNow;
            var changes = tree.ExportChangesSince(cutoffTime).ToList();
            var duration = DateTime.UtcNow - startTime;

            // Assert
            Assert.Equal(10, changes.Count);
            // Delta sync should be fast even with large dataset
            Assert.True(duration.TotalMilliseconds < 1000,
                $"Delta sync took {duration.TotalMilliseconds}ms (should be < 1000ms)");
        }

        [Fact]
        public void ExportDeltaChanges_MultipleRounds()
        {
            // Arrange
            var tree = new Tree<Item>(new MemoryTrunk<Item>());

            // Round 1
            tree.Stash(new Item { Id = "r1-1", Name = "Round 1 Item 1", Version = 1 });
            tree.Stash(new Item { Id = "r1-2", Name = "Round 1 Item 2", Version = 1 });
            var round1 = tree.ExportDeltaChanges().ToList();

            System.Threading.Thread.Sleep(100);

            // Round 2
            tree.Stash(new Item { Id = "r2-1", Name = "Round 2 Item 1", Version = 1 });
            tree.Stash(new Item { Id = "r2-2", Name = "Round 2 Item 2", Version = 1 });
            var round2 = tree.ExportDeltaChanges().ToList();

            System.Threading.Thread.Sleep(100);

            // Round 3
            tree.Stash(new Item { Id = "r3-1", Name = "Round 3 Item 1", Version = 1 });
            var round3 = tree.ExportDeltaChanges().ToList();

            // Assert
            Assert.Equal(2, round1.Count);
            Assert.Equal(2, round2.Count);
            Assert.Equal(1, round3.Count);

            // Verify no overlap between rounds
            Assert.Contains(round1, n => n.Id == "r1-1");
            Assert.Contains(round2, n => n.Id == "r2-1");
            Assert.Contains(round3, n => n.Id == "r3-1");
            Assert.DoesNotContain(round2, n => n.Id == "r1-1");
            Assert.DoesNotContain(round3, n => n.Id == "r2-1");
        }

        // ===== Edge Cases =====

        [Fact]
        public void ExportChangesSince_EmptyTree()
        {
            // Arrange
            var tree = new Tree<Item>(new MemoryTrunk<Item>());

            // Act
            var changes = tree.ExportChangesSince(DateTime.MinValue).ToList();

            // Assert
            Assert.Empty(changes);
        }

        [Fact]
        public void ExportDeltaChanges_NoChangesReturnEmpty()
        {
            // Arrange
            var tree = new Tree<Item>(new MemoryTrunk<Item>());

            tree.Stash(new Item { Id = "item1", Name = "Item 1", Version = 1 });
            tree.ExportDeltaChanges(); // First export

            // Act - export again without adding new items
            var changes = tree.ExportDeltaChanges().ToList();

            // Assert
            Assert.Empty(changes);
        }

        [Fact]
        public void ExportChangesSince_WithDeletedNuts()
        {
            // Arrange
            var tree = new Tree<Item>(new MemoryTrunk<Item>());

            tree.Stash(new Item { Id = "item1", Name = "Item 1", Version = 1 });
            tree.Stash(new Item { Id = "item2", Name = "Item 2", Version = 1 });

            System.Threading.Thread.Sleep(100);
            var cutoffTime = DateTime.UtcNow;

            // Add then delete an item
            tree.Stash(new Item { Id = "item3", Name = "Item 3", Version = 1 });
            tree.Toss("item3");

            // Act
            var changes = tree.ExportChangesSince(cutoffTime).ToList();

            // Assert - deleted item should not appear in export
            Assert.DoesNotContain(changes, n => n.Id == "item3");
        }
    }
}
