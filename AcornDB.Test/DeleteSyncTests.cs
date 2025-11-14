using Xunit;
using AcornDB;
using AcornDB.Models;
using AcornDB.Storage;
using AcornDB.Sync;
using System;
using System.Linq;
using System.Threading;

namespace AcornDB.Test
{
    public class DeleteSyncTests
    {
        public class User
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        [Fact]
        public void Toss_WithoutPropagate_DoesNotSyncToOtherTrees()
        {
            // Arrange
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
            Thread.Sleep(100); // Allow sync

            // Act - Toss with propagate=false
            tree1.Toss("user1", propagate: false);

            // Assert - tree1 should be deleted, tree2 should still have it
            Assert.Null(tree1.Crack("user1"));
            Assert.NotNull(tree2.Crack("user1")); // Should still exist in tree2
        }

        [Fact]
        public void Toss_WithPropagate_SyncsToEntangledTree()
        {
            // Arrange
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
            Thread.Sleep(100); // Allow sync

            // Act - Toss with propagate=true (default)
            tree1.Toss("user1");
            Thread.Sleep(100); // Allow delete to sync

            // Assert - both trees should have the item deleted
            Assert.Null(tree1.Crack("user1"));
            Assert.Null(tree2.Crack("user1"));
        }

        [Fact]
        public void Toss_DefaultBehavior_PropagatesDelete()
        {
            // Arrange
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
            Thread.Sleep(100);

            // Act - Toss without explicit propagate parameter (should default to true)
            tree1.Toss("user1");
            Thread.Sleep(100);

            // Assert
            Assert.Null(tree1.Crack("user1"));
            Assert.Null(tree2.Crack("user1"));
        }

        [Fact]
        public void Toss_BidirectionalMesh_DoesNotCreateInfiniteLoop()
        {
            // Arrange - Create bidirectional mesh
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);
            tree2.Entangle(tree1); // Bidirectional

            tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
            Thread.Sleep(100);

            // Act - This should NOT cause infinite loop
            tree1.Toss("user1");
            Thread.Sleep(100);

            // Assert - both should be deleted, no crash/hang
            Assert.Null(tree1.Crack("user1"));
            Assert.Null(tree2.Crack("user1"));
        }

        [Fact]
        public void Toss_FullMeshTopology_PropagatesCorrectly()
        {
            // Arrange - Create 3-tree full mesh
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());
            var tree3 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);
            tree1.Entangle(tree3);
            tree2.Entangle(tree1);
            tree2.Entangle(tree3);
            tree3.Entangle(tree1);
            tree3.Entangle(tree2);

            // Stash to all trees
            tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
            Thread.Sleep(200); // Allow full sync

            // Act - Delete from tree1
            tree1.Toss("user1");
            Thread.Sleep(200); // Allow delete to propagate

            // Assert - all three should have deleted
            Assert.Null(tree1.Crack("user1"));
            Assert.Null(tree2.Crack("user1"));
            Assert.Null(tree3.Crack("user1"));
        }

        [Fact]
        public void Toss_MultipleItems_SyncsAllDeletes()
        {
            // Arrange
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
            tree1.Stash("user2", new User { Id = "user2", Name = "Bob" });
            tree1.Stash("user3", new User { Id = "user3", Name = "Charlie" });
            Thread.Sleep(100);

            // Act - Delete multiple items
            tree1.Toss("user1");
            tree1.Toss("user2");
            Thread.Sleep(100);

            // Assert
            Assert.Null(tree1.Crack("user1"));
            Assert.Null(tree1.Crack("user2"));
            Assert.NotNull(tree1.Crack("user3")); // Should still exist

            Assert.Null(tree2.Crack("user1"));
            Assert.Null(tree2.Crack("user2"));
            Assert.NotNull(tree2.Crack("user3"));
        }

        [Fact]
        public void Toss_TanglePushDelete_CallsCorrectly()
        {
            // Arrange
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());
            var branch = new InProcessBranch<User>(tree2);

            var tangle = new Tangle<User>(tree1, branch, "test-tangle");

            tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
            Thread.Sleep(100);

            // Act
            tangle.PushDelete("user1");
            Thread.Sleep(50);

            // Assert - tree2 should have the delete
            Assert.Null(tree2.Crack("user1"));
        }

        [Fact]
        public void Toss_DuplicateDelete_DoesNotCauseProblem()
        {
            // Arrange
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
            Thread.Sleep(100);

            // Act - Delete same item twice
            tree1.Toss("user1");
            tree1.Toss("user1"); // Second delete (item already gone)
            Thread.Sleep(100);

            // Assert - should handle gracefully
            Assert.Null(tree1.Crack("user1"));
            Assert.Null(tree2.Crack("user1"));
        }

        [Fact]
        public void Toss_NonExistentItem_HandlesGracefully()
        {
            // Arrange
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            // Act - Delete item that doesn't exist
            tree1.Toss("nonexistent");
            Thread.Sleep(50);

            // Assert - should not crash
            Assert.Null(tree1.Crack("nonexistent"));
            Assert.Null(tree2.Crack("nonexistent"));
        }

        [Fact]
        public void Toss_ViaTangle_IncrementsStats()
        {
            // Arrange
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);

            tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
            Thread.Sleep(100);

            var statsBefore = tree1.GetNutStats();

            // Act
            tree1.Toss("user1");
            Thread.Sleep(100);

            var statsAfter = tree1.GetNutStats();

            // Assert
            Assert.Equal(statsBefore.TotalTossed + 1, statsAfter.TotalTossed);
        }

        [Fact]
        public void Toss_FromSecondaryTree_PropagatesBack()
        {
            // Arrange - Tree1 → Tree2
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            tree1.Entangle(tree2);
            tree2.Entangle(tree1); // Bidirectional

            tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
            Thread.Sleep(100);

            // Act - Delete from tree2 (not tree1)
            tree2.Toss("user1");
            Thread.Sleep(100);

            // Assert - should propagate back to tree1
            Assert.Null(tree1.Crack("user1"));
            Assert.Null(tree2.Crack("user1"));
        }

        [Fact]
        public void Toss_Grove_EntangleAllMesh_DeletesPropagateAcrossMesh()
        {
            // Arrange
            var grove = new Grove();
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());
            var tree3 = new Tree<User>(new MemoryTrunk<User>());

            grove.Plant(tree1);
            grove.Plant(tree2);
            grove.Plant(tree3);

            grove.EntangleAll(bidirectional: true);

            tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
            Thread.Sleep(200);

            // Act - Delete from tree1
            tree1.Toss("user1");
            Thread.Sleep(200);

            // Assert - should propagate to all trees in mesh
            Assert.Null(tree1.Crack("user1"));
            Assert.Null(tree2.Crack("user1"));
            Assert.Null(tree3.Crack("user1"));
        }
    }
}
