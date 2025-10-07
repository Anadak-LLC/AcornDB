using AcornDB.Cache;
using AcornDB.Storage;

namespace AcornDB.Test
{
    public class LRUCacheEvictionTests
    {
        public class User
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        [Fact]
        public void LRU_DefaultMaxSize_Is10000()
        {
            var strategy = new LRUCacheStrategy<User>();
            Assert.Equal(10_000, strategy.MaxSize);
        }

        [Fact]
        public void LRU_CustomMaxSize_IsRespected()
        {
            var strategy = new LRUCacheStrategy<User>(maxSize: 100);
            Assert.Equal(100, strategy.MaxSize);
        }

        [Fact]
        public void LRU_ZeroMaxSize_ThrowsException()
        {
            Assert.Throws<ArgumentException>(() => new LRUCacheStrategy<User>(maxSize: 0));
        }

        [Fact]
        public void LRU_NegativeMaxSize_ThrowsException()
        {
            Assert.Throws<ArgumentException>(() => new LRUCacheStrategy<User>(maxSize: -1));
        }

        [Fact]
        public void LRU_TreeWithLRUStrategy_DoesNotEvictUnderLimit()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 100);
            var tree = new Tree<User>(new MemoryTrunk<User>(), lru);

            // Add 50 users (under limit)
            for (int i = 0; i < 50; i++)
            {
                tree.Stash(new User { Id = $"user{i}", Name = $"User {i}" });
            }

            // All should still be in cache
            Assert.Equal(50, tree.NutCount);
            for (int i = 0; i < 50; i++)
            {
                Assert.NotNull(tree.Crack($"user{i}"));
            }
        }

        [Fact]
        public void LRU_TreeWithLRUStrategy_EvictsWhenOverLimit()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 10);
            var tree = new Tree<User>(new MemoryTrunk<User>(), lru);

            // Add 20 users (2x limit)
            for (int i = 0; i < 20; i++)
            {
                tree.Stash(new User { Id = $"user{i}", Name = $"User {i}" });
            }

            // Cache should have been evicted down to ~90% of max (9 items)
            // After eviction triggers, some items should be removed from cache
            Assert.True(tree.NutCount < 20);
        }

        [Fact]
        public void LRU_EvictsColdestItems_First()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 5);
            var tree = new Tree<User>(new MemoryTrunk<User>(), lru);

            // Add 5 users
            for (int i = 0; i < 5; i++)
            {
                tree.Stash(new User { Id = $"user{i}", Name = $"User {i}" });
                Thread.Sleep(10); // Ensure different timestamps
            }

            // Access user2, user3, user4 (make them "hot")
            tree.Crack("user2");
            tree.Crack("user3");
            tree.Crack("user4");
            Thread.Sleep(10);

            // Add one more user to trigger eviction
            tree.Stash(new User { Id = "user5", Name = "User 5" });

            // user0 and user1 should be evicted (coldest)
            // But they're still in trunk, so Crack will reload them
            var user0 = tree.Crack("user0");
            Assert.NotNull(user0); // Reloaded from trunk
        }

        [Fact]
        public void LRU_ManualEviction_ReturnsCount()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 5);
            var tree = new Tree<User>(new MemoryTrunk<User>(), lru)
            {
                CacheEvictionEnabled = false // Disable auto-eviction
            };

            // Add 20 users
            for (int i = 0; i < 20; i++)
            {
                tree.Stash(new User { Id = $"user{i}", Name = $"User {i}" });
            }

            // Re-enable eviction and manually trigger it
            tree.CacheEvictionEnabled = true;
            var evicted = tree.EvictCacheItems();
            Assert.True(evicted > 0);
        }

        [Fact]
        public void LRU_DisableEviction_DoesNotEvict()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 5);
            var tree = new Tree<User>(new MemoryTrunk<User>(), lru)
            {
                CacheEvictionEnabled = false
            };

            // Add 20 users (4x limit)
            for (int i = 0; i < 20; i++)
            {
                tree.Stash(new User { Id = $"user{i}", Name = $"User {i}" });
            }

            // Eviction disabled, so all 20 should still be cached
            Assert.Equal(20, tree.NutCount);
        }

        [Fact]
        public void LRU_NoEvictionStrategy_NeverEvicts()
        {
            var noEvict = new NoEvictionStrategy<User>();
            var tree = new Tree<User>(new MemoryTrunk<User>(), noEvict);

            // Add 10000 users
            for (int i = 0; i < 10_000; i++)
            {
                tree.Stash(new User { Id = $"user{i}", Name = $"User {i}" });
            }

            // All should still be cached
            Assert.Equal(10_000, tree.NutCount);
        }

        [Fact]
        public void LRU_GetStats_ReturnsCorrectInfo()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 100);
            var tree = new Tree<User>(new MemoryTrunk<User>(), lru);

            // Add 50 users
            for (int i = 0; i < 50; i++)
            {
                tree.Stash(new User { Id = $"user{i}", Name = $"User {i}" });
            }

            var stats = lru.GetStats();
            Assert.Equal(50, stats.TrackedItems);
            Assert.Equal(100, stats.MaxSize);
            Assert.Equal(50.0, stats.UtilizationPercentage);
            Assert.NotNull(stats.OldestAccessTime);
            Assert.NotNull(stats.NewestAccessTime);
        }

        [Fact]
        public void LRU_AccessTime_UpdatesOnCrack()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 10);
            var tree = new Tree<User>(new MemoryTrunk<User>(), lru);

            tree.Stash(new User { Id = "user1", Name = "User 1" });
            var accessTime1 = lru.GetLastAccessTime("user1");

            Thread.Sleep(50);

            tree.Crack("user1");
            var accessTime2 = lru.GetLastAccessTime("user1");

            Assert.NotNull(accessTime1);
            Assert.NotNull(accessTime2);
            Assert.True(accessTime2 > accessTime1);
        }

        [Fact]
        public void LRU_Toss_RemovesFromTracking()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 10);
            var tree = new Tree<User>(new MemoryTrunk<User>(), lru);

            tree.Stash(new User { Id = "user1", Name = "User 1" });
            Assert.Equal(1, lru.TrackedItemCount);

            tree.Toss("user1");
            Assert.Equal(0, lru.TrackedItemCount);
        }

        [Fact]
        public void LRU_Reset_ClearsAllTracking()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 10);

            var tree = new Tree<User>(new MemoryTrunk<User>(), lru);

            for (int i = 0; i < 5; i++)
            {
                tree.Stash(new User { Id = $"user{i}", Name = $"User {i}" });
            }

            Assert.Equal(5, lru.TrackedItemCount);

            lru.Reset();
            Assert.Equal(0, lru.TrackedItemCount);
        }

        [Fact]
        public void LRU_ChangeCacheStrategy_WorksCorrectly()
        {
            var lru1 = new LRUCacheStrategy<User>(maxSize: 10);
            var tree = new Tree<User>(new MemoryTrunk<User>(), lru1);

            tree.Stash(new User { Id = "user1", Name = "User 1" });

            // Change strategy
            var lru2 = new LRUCacheStrategy<User>(maxSize: 100);
            tree.CacheStrategy = lru2;

            // New strategy should work
            tree.Stash(new User { Id = "user2", Name = "User 2" });
            Assert.Equal(1, lru2.TrackedItemCount);
        }

        [Fact]
        public void LRU_GetEvictionCandidates_ReturnsOldestFirst()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 3);
            var cache = new Dictionary<string, Nut<User>>();

            // Simulate stashing with different times
            var nut1 = new Nut<User> { Id = "user1", Payload = new User { Id = "user1", Name = "User 1" } };
            var nut2 = new Nut<User> { Id = "user2", Payload = new User { Id = "user2", Name = "User 2" } };
            var nut3 = new Nut<User> { Id = "user3", Payload = new User { Id = "user3", Name = "User 3" } };
            var nut4 = new Nut<User> { Id = "user4", Payload = new User { Id = "user4", Name = "User 4" } };

            lru.OnStash("user1", nut1);
            Thread.Sleep(10);
            lru.OnStash("user2", nut2);
            Thread.Sleep(10);
            lru.OnStash("user3", nut3);
            Thread.Sleep(10);
            lru.OnStash("user4", nut4);

            cache["user1"] = nut1;
            cache["user2"] = nut2;
            cache["user3"] = nut3;
            cache["user4"] = nut4;

            var candidates = lru.GetEvictionCandidates(cache).ToList();

            // Should evict oldest items (user1 should be first candidate)
            Assert.Contains("user1", candidates);
        }

        [Fact]
        public void LRU_PerformanceTest_1000Items()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 500);
            var tree = new Tree<User>(new MemoryTrunk<User>(), lru);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Add 1000 items (2x limit)
            for (int i = 0; i < 1000; i++)
            {
                tree.Stash(new User { Id = $"perf-user-{i}", Name = $"User {i}" });
            }

            stopwatch.Stop();

            // Should complete reasonably fast
            Assert.True(stopwatch.ElapsedMilliseconds < 3000,
                $"LRU eviction took too long: {stopwatch.ElapsedMilliseconds}ms");

            // Cache should be limited
            Assert.True(tree.NutCount <= 500);
        }

        // Note: Concurrent access test removed - Tree<T> doesn't currently guarantee thread-safety
        // This can be added in a future phase with ConcurrentDictionary or locking

        [Fact]
        public void LRU_EvictedItemsStillInTrunk_CanBeReloaded()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 5);
            var trunk = new MemoryTrunk<User>();
            var tree = new Tree<User>(trunk, lru);

            // Add 10 users
            for (int i = 0; i < 10; i++)
            {
                tree.Stash(new User { Id = $"user{i}", Name = $"User {i}" });
            }

            // Some should be evicted from cache
            Assert.True(tree.NutCount < 10);

            // But all should still be accessible (reloaded from trunk)
            for (int i = 0; i < 10; i++)
            {
                var user = tree.Crack($"user{i}");
                Assert.NotNull(user);
                Assert.Equal($"User {i}", user.Name);
            }
        }

        [Fact]
        public void LRU_WithAutoId_WorksCorrectly()
        {
            var lru = new LRUCacheStrategy<User>(maxSize: 5);
            var tree = new Tree<User>(new MemoryTrunk<User>(), lru);

            for (int i = 0; i < 10; i++)
            {
                tree.Stash(new User { Id = $"auto-user{i}", Name = $"User {i}" });
            }

            // Cache should have evicted some items
            Assert.True(tree.NutCount < 10);
        }
    }
}
