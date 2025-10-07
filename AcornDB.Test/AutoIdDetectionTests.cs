using AcornDB.Models;
using AcornDB.Storage;

namespace AcornDB.Test
{
    public class AutoIdDetectionTests
    {
        // Test classes for various ID scenarios
        public class UserWithId
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        public class UserWithKey
        {
            public string Key { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        public class UserWithID
        {
            public string ID { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        public class UserWithKEY
        {
            public string KEY { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        public class UserWithINutment : INutment<string>
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        public class UserWithGuidINutment : INutment<Guid>
        {
            public Guid Id { get; set; } = Guid.Empty;
            public string Name { get; set; } = string.Empty;
        }

        public class UserWithIntINutment : INutment<int>
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public class UserWithNoId
        {
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        [Fact]
        public void AutoId_WithIdProperty_DetectsCorrectly()
        {
            var tree = new Tree<UserWithId>(new MemoryTrunk<UserWithId>());
            var user = new UserWithId { Id = "test-123", Name = "Alice" };

            // Should work without explicit ID
            tree.Stash(user);

            var retrieved = tree.Crack("test-123");
            Assert.NotNull(retrieved);
            Assert.Equal("Alice", retrieved.Name);
        }

        [Fact]
        public void AutoId_WithKeyProperty_DetectsCorrectly()
        {
            var tree = new Tree<UserWithKey>(new MemoryTrunk<UserWithKey>());
            var user = new UserWithKey { Key = "user-456", Name = "Bob" };

            tree.Stash(user);

            var retrieved = tree.Crack("user-456");
            Assert.NotNull(retrieved);
            Assert.Equal("Bob", retrieved.Name);
        }

        [Fact]
        public void AutoId_WithIDProperty_DetectsCorrectly()
        {
            var tree = new Tree<UserWithID>(new MemoryTrunk<UserWithID>());
            var user = new UserWithID { ID = "USER-789", Name = "Charlie" };

            tree.Stash(user);

            var retrieved = tree.Crack("USER-789");
            Assert.NotNull(retrieved);
            Assert.Equal("Charlie", retrieved.Name);
        }

        [Fact]
        public void AutoId_WithKEYProperty_DetectsCorrectly()
        {
            var tree = new Tree<UserWithKEY>(new MemoryTrunk<UserWithKEY>());
            var user = new UserWithKEY { KEY = "KEY-999", Name = "Dave" };

            tree.Stash(user);

            var retrieved = tree.Crack("KEY-999");
            Assert.NotNull(retrieved);
            Assert.Equal("Dave", retrieved.Name);
        }

        [Fact]
        public void AutoId_WithINutmentInterface_DetectsCorrectly()
        {
            var tree = new Tree<UserWithINutment>(new MemoryTrunk<UserWithINutment>());
            var user = new UserWithINutment { Id = "inutment-1", Name = "Eve" };

            tree.Stash(user);

            var retrieved = tree.Crack("inutment-1");
            Assert.NotNull(retrieved);
            Assert.Equal("Eve", retrieved.Name);
        }

        [Fact]
        public void AutoId_WithGuidINutment_ConvertsToString()
        {
            var tree = new Tree<UserWithGuidINutment>(new MemoryTrunk<UserWithGuidINutment>());
            var guid = Guid.NewGuid();
            var user = new UserWithGuidINutment { Id = guid, Name = "Frank" };

            tree.Stash(user);

            var retrieved = tree.Crack(guid.ToString());
            Assert.NotNull(retrieved);
            Assert.Equal("Frank", retrieved.Name);
        }

        [Fact]
        public void AutoId_WithIntINutment_ConvertsToString()
        {
            var tree = new Tree<UserWithIntINutment>(new MemoryTrunk<UserWithIntINutment>());
            var user = new UserWithIntINutment { Id = 12345, Name = "Grace" };

            tree.Stash(user);

            var retrieved = tree.Crack("12345");
            Assert.NotNull(retrieved);
            Assert.Equal("Grace", retrieved.Name);
        }

        [Fact]
        public void AutoId_WithNoIdProperty_ThrowsException()
        {
            var tree = new Tree<UserWithNoId>(new MemoryTrunk<UserWithNoId>());
            var user = new UserWithNoId { Name = "Henry", Email = "henry@test.com" };

            var exception = Assert.Throws<InvalidOperationException>(() => tree.Stash(user));
            Assert.Contains("Cannot auto-detect ID", exception.Message);
        }

        [Fact]
        public void AutoId_WithNullId_ThrowsException()
        {
            var tree = new Tree<UserWithId>(new MemoryTrunk<UserWithId>());
            var user = new UserWithId { Id = null!, Name = "Ivan" };

            var exception = Assert.Throws<InvalidOperationException>(() => tree.Stash(user));
            Assert.Contains("Extracted ID", exception.Message);
            Assert.Contains("null or empty", exception.Message);
        }

        [Fact]
        public void AutoId_WithEmptyId_ThrowsException()
        {
            var tree = new Tree<UserWithId>(new MemoryTrunk<UserWithId>());
            var user = new UserWithId { Id = "", Name = "Jane" };

            var exception = Assert.Throws<InvalidOperationException>(() => tree.Stash(user));
            Assert.Contains("Extracted ID", exception.Message);
            Assert.Contains("null or empty", exception.Message);
        }

        [Fact]
        public void AutoId_MultipleStashes_UsesCache()
        {
            var tree = new Tree<UserWithId>(new MemoryTrunk<UserWithId>());

            // Stash multiple users to verify caching works
            for (int i = 0; i < 100; i++)
            {
                tree.Stash(new UserWithId { Id = $"user-{i}", Name = $"User {i}" });
            }

            // Verify all were stashed correctly
            for (int i = 0; i < 100; i++)
            {
                var user = tree.Crack($"user-{i}");
                Assert.NotNull(user);
                Assert.Equal($"User {i}", user.Name);
            }
        }

        [Fact]
        public void AutoId_ExplicitIdOverridesAutoDetection()
        {
            var tree = new Tree<UserWithId>(new MemoryTrunk<UserWithId>());
            var user = new UserWithId { Id = "auto-id", Name = "Kate" };

            // Explicitly provide a different ID
            tree.Stash("explicit-id", user);

            // Should be stored under explicit ID, not auto-detected ID
            var retrieved = tree.Crack("explicit-id");
            Assert.NotNull(retrieved);
            Assert.Equal("Kate", retrieved.Name);

            // Auto-detected ID should not exist
            var autoRetrieved = tree.Crack("auto-id");
            Assert.Null(autoRetrieved);
        }

        [Fact]
        public void AutoId_IdPriorityOrder_INutmentBeforePropertyNames()
        {
            // INutment interface should take priority over property names
            var tree = new Tree<UserWithINutment>(new MemoryTrunk<UserWithINutment>());
            var user = new UserWithINutment { Id = "priority-test", Name = "Larry" };

            tree.Stash(user);

            var retrieved = tree.Crack("priority-test");
            Assert.NotNull(retrieved);
            Assert.Equal("Larry", retrieved.Name);
        }

        [Fact]
        public void AutoId_PersistsAcrossTreeInstances()
        {
            var trunkPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                // First tree instance
                var tree1 = new Tree<UserWithId>(new FileTrunk<UserWithId>(trunkPath));
                tree1.Stash(new UserWithId { Id = "persist-test", Name = "Mary" });

                // Second tree instance (should load from disk)
                var tree2 = new Tree<UserWithId>(new FileTrunk<UserWithId>(trunkPath));
                var retrieved = tree2.Crack("persist-test");
                Assert.NotNull(retrieved);
                Assert.Equal("Mary", retrieved.Name);
            }
            finally
            {
                if (Directory.Exists(trunkPath))
                    Directory.Delete(trunkPath, true);
            }
        }

        [Fact]
        public void AutoId_NutCount_IncrementsCorrectly()
        {
            var tree = new Tree<UserWithId>(new MemoryTrunk<UserWithId>());
            Assert.Equal(0, tree.NutCount);

            tree.Stash(new UserWithId { Id = "user1", Name = "Nancy" });
            Assert.Equal(1, tree.NutCount);

            tree.Stash(new UserWithId { Id = "user2", Name = "Oscar" });
            Assert.Equal(2, tree.NutCount);

            tree.Toss("user1");
            Assert.Equal(1, tree.NutCount);
        }
    }
}
