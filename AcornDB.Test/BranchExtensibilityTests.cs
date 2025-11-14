using AcornDB;
using AcornDB.Storage;
using AcornDB.Sync;
using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace AcornDB.Test
{
    public class BranchExtensibilityTests : IDisposable
    {
        private readonly string _testDir;

        // Test model class
        public class Person
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
        }

        public BranchExtensibilityTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"acorndb_branch_ext_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        // ===== AuditBranch Tests =====

        [Fact]
        public void AuditBranch_LogsStashOperations()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var auditBranch = new AuditBranch();
            tree.Entangle(auditBranch);

            // Act
            tree.Stash("p1", new Person { Name = "Alice", Age = 30 });
            tree.Stash("p2", new Person { Name = "Bob", Age = 25 });

            // Assert
            var log = auditBranch.GetAuditLog();
            Assert.Equal(2, log.Count);
            Assert.All(log, entry => Assert.Equal("STASH", entry.Action));
            Assert.Contains(log, e => e.Key == "p1");
            Assert.Contains(log, e => e.Key == "p2");

            auditBranch.Dispose();
        }

        [Fact]
        public void AuditBranch_LogsTossOperations()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var auditBranch = new AuditBranch();
            tree.Entangle(auditBranch);

            tree.Stash("p1", new Person { Name = "Alice", Age = 30 });

            // Act
            tree.Toss("p1");

            // Assert
            var log = auditBranch.GetAuditLog();
            Assert.Equal(2, log.Count); // 1 stash + 1 toss
            Assert.Equal("STASH", log[0].Action);
            Assert.Equal("TOSS", log[1].Action);
            Assert.Equal("p1", log[1].Key);

            auditBranch.Dispose();
        }

        [Fact]
        public void AuditBranch_TracksOriginTreeId()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var auditBranch = new AuditBranch();
            tree.Entangle(auditBranch);

            // Act
            tree.Stash("p1", new Person { Name = "Alice", Age = 30 });

            // Assert
            var log = auditBranch.GetAuditLog();
            Assert.Single(log);
            Assert.Equal(tree.TreeId, log[0].OriginTreeId);

            auditBranch.Dispose();
        }

        [Fact]
        public void AuditBranch_SupportsLogClearing()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var auditBranch = new AuditBranch();
            tree.Entangle(auditBranch);

            tree.Stash("p1", new Person { Name = "Alice", Age = 30 });
            Assert.Single(auditBranch.GetAuditLog());

            // Act
            auditBranch.ClearLog();

            // Assert
            Assert.Empty(auditBranch.GetAuditLog());

            auditBranch.Dispose();
        }

        [Fact]
        public void AuditBranch_RespectsMaxLogSize()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var auditBranch = new AuditBranch(maxLogSize: 5);
            tree.Entangle(auditBranch);

            // Act - add 10 items (exceeds max of 5)
            for (int i = 0; i < 10; i++)
            {
                tree.Stash($"p{i}", new Person { Name = $"Person{i}", Age = 20 + i });
            }

            // Assert - should only keep last 5
            var log = auditBranch.GetAuditLog();
            Assert.Equal(5, log.Count);
            Assert.Contains(log, e => e.Key == "p9"); // Most recent
            Assert.DoesNotContain(log, e => e.Key == "p0"); // Oldest should be evicted

            auditBranch.Dispose();
        }

        [Fact]
        public void AuditBranch_GetRecentEntries()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var auditBranch = new AuditBranch();
            tree.Entangle(auditBranch);

            for (int i = 0; i < 10; i++)
            {
                tree.Stash($"p{i}", new Person { Name = $"Person{i}", Age = 20 + i });
            }

            // Act
            var recent = auditBranch.GetRecentEntries(3).ToList();

            // Assert
            Assert.Equal(3, recent.Count);
            Assert.Equal("p9", recent[2].Key); // Most recent

            auditBranch.Dispose();
        }

        // ===== MetricsBranch Tests =====

        [Fact]
        public void MetricsBranch_TracksStashCount()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var metricsBranch = new MetricsBranch();
            tree.Entangle(metricsBranch);

            // Act
            tree.Stash("p1", new Person { Name = "Alice", Age = 30 });
            tree.Stash("p2", new Person { Name = "Bob", Age = 25 });
            tree.Stash("p3", new Person { Name = "Charlie", Age = 35 });

            // Assert
            var summary = metricsBranch.GetSummary();
            Assert.Equal(3, summary.TotalStash);
            Assert.Equal(0, summary.TotalToss);
            Assert.Equal(3, summary.TotalOperations);

            metricsBranch.Dispose();
        }

        [Fact]
        public void MetricsBranch_TracksTossCount()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var metricsBranch = new MetricsBranch();
            tree.Entangle(metricsBranch);

            tree.Stash("p1", new Person { Name = "Alice", Age = 30 });
            tree.Stash("p2", new Person { Name = "Bob", Age = 25 });

            // Act
            tree.Toss("p1");

            // Assert
            var summary = metricsBranch.GetSummary();
            Assert.Equal(2, summary.TotalStash);
            Assert.Equal(1, summary.TotalToss);
            Assert.Equal(3, summary.TotalOperations);

            metricsBranch.Dispose();
        }

        [Fact]
        public void MetricsBranch_TracksUniqueTreesSeen()
        {
            // Arrange
            var dir1 = Path.Combine(_testDir, "tree1");
            var dir2 = Path.Combine(_testDir, "tree2");
            Directory.CreateDirectory(dir1);
            Directory.CreateDirectory(dir2);

            var tree1 = new Tree<Person>(new FileTrunk<Person>(dir1));
            var tree2 = new Tree<Person>(new FileTrunk<Person>(dir2));
            var metricsBranch = new MetricsBranch();

            tree1.Entangle(metricsBranch);
            tree2.Entangle(metricsBranch);

            // Act
            tree1.Stash("p1", new Person { Name = "Alice", Age = 30 });
            tree2.Stash("p2", new Person { Name = "Bob", Age = 25 });

            // Assert
            var summary = metricsBranch.GetSummary();
            Assert.Equal(2, summary.UniqueTreesSeen);

            metricsBranch.Dispose();
        }

        [Fact]
        public void MetricsBranch_TracksTreeSpecificMetrics()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var metricsBranch = new MetricsBranch();
            tree.Entangle(metricsBranch);

            // Act
            tree.Stash("p1", new Person { Name = "Alice", Age = 30 });
            tree.Stash("p2", new Person { Name = "Bob", Age = 25 });
            tree.Toss("p1");

            // Assert
            var treeMetrics = metricsBranch.GetTreeMetrics(tree.TreeId);
            Assert.NotNull(treeMetrics);
            Assert.Equal(2, treeMetrics.StashCount);
            Assert.Equal(1, treeMetrics.TossCount);
            Assert.Equal(3, treeMetrics.TotalOperations);

            metricsBranch.Dispose();
        }

        [Fact]
        public void MetricsBranch_SupportsReset()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var metricsBranch = new MetricsBranch();
            tree.Entangle(metricsBranch);

            tree.Stash("p1", new Person { Name = "Alice", Age = 30 });
            Assert.Equal(1, metricsBranch.GetSummary().TotalStash);

            // Act
            metricsBranch.Reset();

            // Assert
            var summary = metricsBranch.GetSummary();
            Assert.Equal(0, summary.TotalStash);
            Assert.Equal(0, summary.TotalOperations);

            metricsBranch.Dispose();
        }

        [Fact]
        public void MetricsBranch_TracksHopDistribution()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var metricsBranch = new MetricsBranch();

            // Entangle directly - operations from this tree will have hop 0
            tree.Entangle(metricsBranch);

            // Act - all operations from this tree will be hop 0
            tree.Stash("p1", new Person { Name = "Alice", Age = 30 });
            tree.Stash("p2", new Person { Name = "Bob", Age = 25 });

            // Assert
            var hopDist = metricsBranch.GetHopDistribution();
            Assert.NotEmpty(hopDist);
            Assert.Contains(0, hopDist.Keys); // Direct operations have hop 0
            Assert.Equal(2, hopDist[0]); // Both stashes have hop 0

            metricsBranch.Dispose();
        }

        // ===== Combined Branches Tests =====

        [Fact]
        public void MultipleBranches_BothReceiveEvents()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var auditBranch = new AuditBranch();
            var metricsBranch = new MetricsBranch();

            tree.Entangle(auditBranch);
            tree.Entangle(metricsBranch);

            // Act
            tree.Stash("p1", new Person { Name = "Alice", Age = 30 });
            tree.Toss("p1");

            // Assert - Audit branch logged events
            var log = auditBranch.GetAuditLog();
            Assert.Equal(2, log.Count);

            // Assert - Metrics branch tracked events
            var summary = metricsBranch.GetSummary();
            Assert.Equal(1, summary.TotalStash);
            Assert.Equal(1, summary.TotalToss);

            auditBranch.Dispose();
            metricsBranch.Dispose();
        }

        [Fact]
        public void BranchDisposal_ThrowsOnSubsequentUse()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var auditBranch = new AuditBranch();
            tree.Entangle(auditBranch);

            // Act - dispose branch
            auditBranch.Dispose();

            // Assert - should throw when tree tries to use it
            // Note: In production, you'd detangle before disposing, but we're testing error handling
            Assert.Throws<ObjectDisposedException>(() =>
            {
                // Create a leaf and try to use disposed branch directly
                var leaf = new Leaf<Person>
                {
                    LeafId = "test-leaf",
                    OriginTreeId = tree.TreeId,
                    Type = LeafType.Stash,
                    Key = "p1",
                    Data = new Nut<Person> { Id = "p1", Payload = new Person { Name = "Alice", Age = 30 }, Timestamp = DateTime.UtcNow }
                };
                auditBranch.OnStash(leaf);
            });
        }

        // ===== Complex Mesh Tests =====

        [Fact]
        public void ComplexMesh_FiveTrees_NoLoops()
        {
            // Arrange - Create 5 trees in a full mesh
            var dirs = Enumerable.Range(1, 5)
                .Select(i => Path.Combine(_testDir, $"tree{i}"))
                .ToList();

            foreach (var dir in dirs)
            {
                Directory.CreateDirectory(dir);
            }

            var trees = dirs.Select(dir => new Tree<Person>(new FileTrunk<Person>(dir))).ToList();

            // Add audit branch to one tree to track all operations
            var auditBranch = new AuditBranch(maxLogSize: 100);
            trees[0].Entangle(auditBranch);

            // Create full mesh (each tree connected to all others)
            for (int i = 0; i < trees.Count; i++)
            {
                for (int j = 0; j < trees.Count; j++)
                {
                    if (i != j)
                    {
                        trees[i].Entangle(trees[j]);
                    }
                }
            }

            // Act - Stash on tree 0
            trees[0].Stash("p1", new Person { Name = "Alice", Age = 30 });

            // Give time for propagation
            Thread.Sleep(200);

            // Assert - All trees should have the item
            for (int i = 0; i < trees.Count; i++)
            {
                var cracked = trees[i].Crack("p1");
                Assert.NotNull(cracked);
                Assert.Equal("Alice", cracked.Name);
            }

            // Assert - Each tree should process the leaf exactly once (no loops)
            // Audit log should show multiple STASH events but no duplicates
            var log = auditBranch.GetAuditLog();
            Assert.True(log.Count >= 1); // At least the original stash

            // Each leaf should have unique LeafId
            var leafIds = log.Select(e => e.LeafId).Distinct().ToList();
            Assert.Equal(log.Count, leafIds.Count); // No duplicate leaf IDs

            auditBranch.Dispose();
        }

        [Fact]
        public void ComplexMesh_SevenTrees_MetricsTracking()
        {
            // Arrange - Create 7 trees
            var dirs = Enumerable.Range(1, 7)
                .Select(i => Path.Combine(_testDir, $"tree{i}"))
                .ToList();

            foreach (var dir in dirs)
            {
                Directory.CreateDirectory(dir);
            }

            var trees = dirs.Select(dir => new Tree<Person>(new FileTrunk<Person>(dir))).ToList();

            // Add metrics branch to track all activity across the mesh
            var metricsBranch = new MetricsBranch();
            foreach (var tree in trees)
            {
                tree.Entangle(metricsBranch);
            }

            // Act - Stash on multiple trees
            trees[0].Stash("p1", new Person { Name = "Alice", Age = 30 });
            trees[3].Stash("p2", new Person { Name = "Bob", Age = 25 });
            trees[5].Stash("p3", new Person { Name = "Charlie", Age = 35 });

            // Assert - Metrics should track operations from trees that stashed
            var summary = metricsBranch.GetSummary();
            Assert.Equal(3, summary.TotalStash); // Exactly 3 stashes
            Assert.Equal(3, summary.UniqueTreesSeen); // 3 trees originated stashes

            // Assert - All operations should be hop 0 (direct from origin trees)
            var hopDist = metricsBranch.GetHopDistribution();
            Assert.NotEmpty(hopDist);
            Assert.Contains(0, hopDist.Keys); // All operations are hop 0
            Assert.Equal(3, hopDist[0]); // All 3 operations are direct (hop 0)

            metricsBranch.Dispose();
        }

        [Fact]
        public void AuditAndMetrics_TracksSameOperations()
        {
            // Arrange
            var tree = new Tree<Person>(new FileTrunk<Person>(_testDir));
            var auditBranch = new AuditBranch();
            var metricsBranch = new MetricsBranch();

            tree.Entangle(auditBranch);
            tree.Entangle(metricsBranch);

            // Act
            for (int i = 0; i < 10; i++)
            {
                tree.Stash($"p{i}", new Person { Name = $"Person{i}", Age = 20 + i });
            }
            for (int i = 0; i < 5; i++)
            {
                tree.Toss($"p{i}");
            }

            // Assert - Audit log should match metrics
            var log = auditBranch.GetAuditLog();
            var summary = metricsBranch.GetSummary();

            var auditStashCount = log.Count(e => e.Action == "STASH");
            var auditTossCount = log.Count(e => e.Action == "TOSS");

            Assert.Equal(auditStashCount, summary.TotalStash);
            Assert.Equal(auditTossCount, summary.TotalToss);

            auditBranch.Dispose();
            metricsBranch.Dispose();
        }
    }
}
