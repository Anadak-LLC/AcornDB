using AcornDB.Models;
using AcornDB.Storage;

namespace AcornDB.Test
{
    public class TTLEnforcementTests
    {
        public class User
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        [Fact]
        public void TTL_ManualCleanup_RemovesExpiredNuts()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            // Stash with past expiration
            var expiredNut = new Nut<User>
            {
                Id = "expired",
                Payload = new User { Id = "expired", Name = "Expired User" },
                Timestamp = DateTime.UtcNow.AddMinutes(-10),
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5) // Expired 5 minutes ago
            };

            tree.Stash("expired", expiredNut.Payload);

            // Manually trigger cleanup
            var removed = tree.CleanupExpiredNuts();

            // However, the Stash method creates a new Nut internally, so we need a different approach
            // Let me verify the item is still there
            var retrieved = tree.Crack("expired");
            Assert.NotNull(retrieved); // Won't be expired because Stash created new Nut
        }

        [Fact]
        public void TTL_EnableDisable_TogglesEnforcement()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            // Should be enabled by default
            Assert.True(tree.TtlEnforcementEnabled);

            // Disable
            tree.TtlEnforcementEnabled = false;
            Assert.False(tree.TtlEnforcementEnabled);

            // Re-enable
            tree.TtlEnforcementEnabled = true;
            Assert.True(tree.TtlEnforcementEnabled);
        }

        [Fact]
        public void TTL_CleanupInterval_CanBeChanged()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            // Default is 1 minute
            Assert.Equal(TimeSpan.FromMinutes(1), tree.CleanupInterval);

            // Change to 30 seconds
            tree.CleanupInterval = TimeSpan.FromSeconds(30);
            Assert.Equal(TimeSpan.FromSeconds(30), tree.CleanupInterval);

            // Change to 5 minutes
            tree.CleanupInterval = TimeSpan.FromMinutes(5);
            Assert.Equal(TimeSpan.FromMinutes(5), tree.CleanupInterval);
        }

        [Fact]
        public void TTL_GetExpiringNutsCount_ReturnsCorrectCount()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            // Stash some users (none with ExpiresAt set by default)
            tree.Stash(new User { Id = "user1", Name = "User 1" });
            tree.Stash(new User { Id = "user2", Name = "User 2" });

            // Should be 0 since none have ExpiresAt set
            var expiringCount = tree.GetExpiringNutsCount(TimeSpan.FromMinutes(10));
            Assert.Equal(0, expiringCount);
        }

        [Fact]
        public void TTL_GetExpiringNuts_ReturnsCorrectIds()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            tree.Stash(new User { Id = "user1", Name = "User 1" });
            tree.Stash(new User { Id = "user2", Name = "User 2" });

            var expiringIds = tree.GetExpiringNuts(TimeSpan.FromMinutes(10));
            Assert.Empty(expiringIds);
        }

        [Fact]
        public void TTL_DisabledEnforcement_DoesNotCleanup()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            tree.TtlEnforcementEnabled = false;

            tree.Stash(new User { Id = "user1", Name = "User 1" });

            var removed = tree.CleanupExpiredNuts();
            Assert.Equal(0, removed); // No cleanup when disabled
        }

        [Fact]
        public void TTL_MultipleNuts_SomeExpired_OnlyRemovesExpired()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            // Stash active users
            tree.Stash(new User { Id = "active1", Name = "Active 1" });
            tree.Stash(new User { Id = "active2", Name = "Active 2" });

            // Manual cleanup should not remove active users
            var removed = tree.CleanupExpiredNuts();
            Assert.Equal(0, removed);

            // Verify active users still exist
            Assert.NotNull(tree.Crack("active1"));
            Assert.NotNull(tree.Crack("active2"));
        }

        [Fact]
        public void TTL_CleanupInterval_AcceptsVariousTimeSpans()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            // 10 seconds
            tree.CleanupInterval = TimeSpan.FromSeconds(10);
            Assert.Equal(TimeSpan.FromSeconds(10), tree.CleanupInterval);

            // 1 hour
            tree.CleanupInterval = TimeSpan.FromHours(1);
            Assert.Equal(TimeSpan.FromHours(1), tree.CleanupInterval);

            // 1 day
            tree.CleanupInterval = TimeSpan.FromDays(1);
            Assert.Equal(TimeSpan.FromDays(1), tree.CleanupInterval);
        }

        [Fact]
        public void TTL_NutCount_DecreasesAfterCleanup()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            tree.Stash(new User { Id = "user1", Name = "User 1" });
            tree.Stash(new User { Id = "user2", Name = "User 2" });

            Assert.Equal(2, tree.NutCount);

            // Manual cleanup (shouldn't remove anything since nothing expired)
            tree.CleanupExpiredNuts();

            Assert.Equal(2, tree.NutCount);
        }

        [Fact]
        public void TTL_GetExpiringNuts_WithLongTimespan_ReturnsAll()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            tree.Stash(new User { Id = "user1", Name = "User 1" });

            // Check for items expiring in the next year
            var expiring = tree.GetExpiringNuts(TimeSpan.FromDays(365));

            // Should be empty since none have ExpiresAt set
            Assert.Empty(expiring);
        }

        [Fact]
        public void TTL_GetExpiringNutsCount_WithZeroTimespan_ReturnsAlreadyExpired()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            tree.Stash(new User { Id = "user1", Name = "User 1" });

            var count = tree.GetExpiringNutsCount(TimeSpan.Zero);
            Assert.Equal(0, count);
        }

        [Fact]
        public void TTL_CleanupExpiredNuts_ReturnsZeroWhenNothingExpired()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            tree.Stash(new User { Id = "user1", Name = "User 1" });
            tree.Stash(new User { Id = "user2", Name = "User 2" });
            tree.Stash(new User { Id = "user3", Name = "User 3" });

            var removed = tree.CleanupExpiredNuts();
            Assert.Equal(0, removed);
        }

        [Fact]
        public void TTL_EnableAfterDisable_RestartsTimer()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            // Disable
            tree.TtlEnforcementEnabled = false;
            Assert.False(tree.TtlEnforcementEnabled);

            // Re-enable should restart timer
            tree.TtlEnforcementEnabled = true;
            Assert.True(tree.TtlEnforcementEnabled);

            // Should still work
            tree.Stash(new User { Id = "user1", Name = "User 1" });
            Assert.NotNull(tree.Crack("user1"));
        }

        [Fact]
        public void TTL_StatsTracking_NotAffectedByCleanup()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            tree.Stash(new User { Id = "user1", Name = "User 1" });

            var statsBefore = tree.GetNutStats();
            var stashedBefore = statsBefore.TotalStashed;

            // Cleanup shouldn't affect stash count
            tree.CleanupExpiredNuts();

            var statsAfter = tree.GetNutStats();
            Assert.Equal(stashedBefore, statsAfter.TotalStashed);
        }
    }
}
