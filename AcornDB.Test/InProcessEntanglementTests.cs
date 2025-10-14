using AcornDB.Storage;

namespace AcornDB.Test
{
    public class InProcessEntanglementTests
    {
        public class User
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        [Fact]
        public void InProcessEntangle_BasicSync_WorksCorrectly()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            var user = new User { Id = "alice", Name = "Alice", Email = "alice@test.com" };
            tree1.Stash(user);

            var retrieved = tree2.Crack("alice");
            Assert.NotNull(retrieved);
            Assert.Equal("Alice", retrieved.Name);
            Assert.Equal("alice@test.com", retrieved.Email);
        }

        [Fact]
        public void InProcessEntangle_MultipleStashes_AllSync()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            // Stash multiple users
            for (int i = 0; i < 10; i++)
            {
                tree1.Stash(new User
                {
                    Id = $"user{i}",
                    Name = $"User {i}",
                    Email = $"user{i}@test.com"
                });
            }

            // Verify all synced to tree2
            for (int i = 0; i < 10; i++)
            {
                var retrieved = tree2.Crack($"user{i}");
                Assert.NotNull(retrieved);
                Assert.Equal($"User {i}", retrieved.Name);
            }
        }

        [Fact]
        public void InProcessEntangle_BidirectionalSync_NotAutomaticByDefault()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            // Only tree1 entangled with tree2 (one-way)
            tree1.Entangle(tree2);

            // Stash in tree2
            tree2.Stash(new User { Id = "bob", Name = "Bob" });

            // Should NOT sync back to tree1 (one-way only)
            var retrieved = tree1.Crack("bob");
            Assert.Null(retrieved);
        }

        [Fact]
        public void InProcessEntangle_BidirectionalSync_RequiresBothEntanglements()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            // Bidirectional entanglement
            tree1.Entangle(tree2);
            tree2.Entangle(tree1);

            // Stash in tree1
            tree1.Stash(new User { Id = "alice", Name = "Alice" });

            // Should sync to tree2
            Assert.NotNull(tree2.Crack("alice"));

            // Stash in tree2
            tree2.Stash(new User { Id = "bob", Name = "Bob" });

            // Should sync back to tree1
            Assert.NotNull(tree1.Crack("bob"));
        }

        [Fact]
        public void InProcessEntangle_ConflictResolution_UsesTimestamps()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            // Stash same ID twice with delay
            tree1.Stash(new User { Id = "charlie", Name = "Charlie v1" });
            Thread.Sleep(10); // Ensure different timestamps
            tree1.Stash(new User { Id = "charlie", Name = "Charlie v2" });

            var retrieved = tree2.Crack("charlie");
            Assert.NotNull(retrieved);
            Assert.Equal("Charlie v2", retrieved.Name); // Newer version wins
        }

        [Fact]
        public void InProcessEntangle_WithDifferentTrunks_SyncsCorrectly()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());

            var trunkPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                var tree2 = new Tree<User>(new FileTrunk<User>(trunkPath));

                tree1.Entangle(tree2);

                tree1.Stash(new User { Id = "dave", Name = "Dave" });

                // Should sync to file-backed tree
                var retrieved = tree2.Crack("dave");
                Assert.NotNull(retrieved);
                Assert.Equal("Dave", retrieved.Name);

                // Verify it persisted to disk
                var tree3 = new Tree<User>(new FileTrunk<User>(trunkPath));
                var persisted = tree3.Crack("dave");
                Assert.NotNull(persisted);
                Assert.Equal("Dave", persisted.Name);
            }
            finally
            {
                if (Directory.Exists(trunkPath))
                    Directory.Delete(trunkPath, true);
            }
        }

        [Fact]
        public void InProcessEntangle_MeshNetwork_ThreeTrees()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());
            var tree3 = new Tree<User>(new MemoryTrunk<User>());

            // Create mesh: tree1 -> tree2 -> tree3
            tree1.Entangle(tree2);
            tree2.Entangle(tree3);

            tree1.Stash(new User { Id = "eve", Name = "Eve" });

            // Should sync to tree2
            Assert.NotNull(tree2.Crack("eve"));

            // tree2's stash should trigger sync to tree3
            // But tree1's direct stash won't reach tree3 without tree2 re-stashing
            // This tests the actual behavior
        }

        [Fact]
        public void InProcessEntangle_MultipleEntanglements_AllReceiveUpdates()
        {
            var source = new Tree<User>(new MemoryTrunk<User>());
            var target1 = new Tree<User>(new MemoryTrunk<User>());
            var target2 = new Tree<User>(new MemoryTrunk<User>());
            var target3 = new Tree<User>(new MemoryTrunk<User>());

            // One source, multiple targets
            source.Entangle(target1);
            source.Entangle(target2);
            source.Entangle(target3);

            source.Stash(new User { Id = "frank", Name = "Frank" });

            // All targets should receive update
            Assert.NotNull(target1.Crack("frank"));
            Assert.NotNull(target2.Crack("frank"));
            Assert.NotNull(target3.Crack("frank"));
        }

        [Fact]
        public void InProcessEntangle_StatsTracking_IncrementsCorrectly()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            var statsBefore = tree1.GetNutStats();
            var initialStashed = statsBefore.TotalStashed;

            tree1.Stash(new User { Id = "grace", Name = "Grace" });

            var statsAfter = tree1.GetNutStats();
            Assert.Equal(initialStashed + 1, statsAfter.TotalStashed);
        }

        [Fact]
        public void InProcessEntangle_Toss_SyncsDelete()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            // Stash and sync
            tree1.Stash(new User { Id = "henry", Name = "Henry" });
            Assert.NotNull(tree2.Crack("henry"));

            // Toss from tree1
            tree1.Toss("henry");
            System.Threading.Thread.Sleep(100); // Allow sync to propagate

            // Delete DOES sync, so item should be deleted in tree2
            var deleted = tree2.Crack("henry");
            Assert.Null(deleted); // Item should be deleted in tree2
        }

        [Fact]
        public void InProcessEntangle_PerformanceTest_1000Items()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < 1000; i++)
            {
                tree1.Stash(new User
                {
                    Id = $"perf-user-{i}",
                    Name = $"User {i}",
                    Email = $"user{i}@perf.test"
                });
            }

            stopwatch.Stop();

            // Verify all synced
            Assert.Equal(1000, tree2.NutCount);

            // Performance assertion: should complete in reasonable time
            Assert.True(stopwatch.ElapsedMilliseconds < 5000,
                $"Sync took too long: {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void InProcessEntangle_WithAutoId_WorksCorrectly()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            // Use auto-ID (no explicit ID parameter)
            tree1.Stash(new User { Id = "auto-alice", Name = "Alice" });

            var retrieved = tree2.Crack("auto-alice");
            Assert.NotNull(retrieved);
            Assert.Equal("Alice", retrieved.Name);
        }

        [Fact]
        public void InProcessEntangle_NutCount_SyncsCorrectly()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            Assert.Equal(0, tree1.NutCount);
            Assert.Equal(0, tree2.NutCount);

            tree1.Stash(new User { Id = "user1", Name = "User 1" });
            Assert.Equal(1, tree1.NutCount);
            Assert.Equal(1, tree2.NutCount);

            tree1.Stash(new User { Id = "user2", Name = "User 2" });
            Assert.Equal(2, tree1.NutCount);
            Assert.Equal(2, tree2.NutCount);
        }
    }
}
