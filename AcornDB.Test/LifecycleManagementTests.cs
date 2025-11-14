using Xunit;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Sync;
using System;

namespace AcornDB.Test
{
    public class LifecycleManagementTests
    {
        public class User
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        public class Product
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        [Fact]
        public void Branch_ImplementsIDisposable()
        {
            var branch = new Branch("http://localhost:5000");
            Assert.IsAssignableFrom<IDisposable>(branch);
        }

        [Fact]
        public void Branch_CanBeDisposed()
        {
            var branch = new Branch("http://localhost:5000");
            branch.Dispose();
            // Should not throw
        }

        [Fact]
        public void Branch_Snap_DisposesCorrectly()
        {
            var branch = new Branch("http://localhost:5000");
            branch.Snap(); // Nutty alias for Dispose
            // Should not throw
        }

        [Fact]
        public void Branch_ThrowsAfterDisposal()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            var branch = new Branch("http://localhost:5000");
            branch.Dispose();

            // Should throw ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(() =>
                branch.TryPush("test", new Nut<User>
                {
                    Id = "test",
                    Payload = new User { Id = "test", Name = "Test" },
                    Timestamp = DateTime.UtcNow
                }));
        }

        [Fact]
        public void Branch_CanBeUsedInUsing()
        {
            using (var branch = new Branch("http://localhost:5000"))
            {
                Assert.NotNull(branch);
            }
            // Branch should be disposed after using block
        }

        [Fact]
        public void Tangle_ImplementsIDisposable()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            var branch = new InProcessBranch<User>(tree);
            var tangle = new Tangle<User>(tree, branch, "test-tangle");

            Assert.IsAssignableFrom<IDisposable>(tangle);
        }

        [Fact]
        public void Tangle_CanBeDisposed()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            var branch = new InProcessBranch<User>(tree);
            var tangle = new Tangle<User>(tree, branch, "test-tangle");

            tangle.Dispose();
            // Should not throw
        }

        [Fact]
        public void Tangle_Break_DisposesCorrectly()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            var branch = new InProcessBranch<User>(tree);
            var tangle = new Tangle<User>(tree, branch, "test-tangle");

            tangle.Break(); // Nutty alias for Dispose
            // Should not throw
        }

        [Fact]
        public void Tangle_ThrowsAfterDisposal()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            var branch = new InProcessBranch<User>(tree);
            var tangle = new Tangle<User>(tree, branch, "test-tangle");

            tangle.Dispose();

            // Should throw ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(() =>
                tangle.PushUpdate("test", new User { Id = "test", Name = "Test" }));
        }

        [Fact]
        public void Tangle_UnregistersFromTreeOnDispose()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            var branch = new InProcessBranch<User>(tree);
            var tangle = new Tangle<User>(tree, branch, "test-tangle");

            var initialCount = tree.GetTangles().Count();
            Assert.Equal(1, initialCount); // Tangle auto-registers

            tangle.Dispose();

            var afterDispose = tree.GetTangles().Count();
            Assert.Equal(0, afterDispose); // Tangle should unregister
        }

        [Fact]
        public void Tree_Entangle_Branch_ReturnsManageableBranch()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            var branch = new Branch("http://localhost:5000");

            var returned = tree.Entangle(branch);

            Assert.NotNull(returned);
            Assert.Same(branch, returned);
        }

        [Fact]
        public void Tree_Entangle_Tree_ReturnsManageableTangle()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());

            var tangle = tree1.Entangle(tree2);

            Assert.NotNull(tangle);
            Assert.IsType<Tangle<User>>(tangle);
        }

        [Fact]
        public void Tree_Detangle_Branch_RemovesAndDisposes()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            var branch = new Branch("http://localhost:5000");
            tree.Entangle(branch);

            tree.Detangle(branch);

            // Branch should be disposed (checking would require accessing private state)
            // But we can verify it throws after detangle
            Assert.Throws<ObjectDisposedException>(() =>
                branch.TryPush("test", new Nut<User>
                {
                    Id = "test",
                    Payload = new User { Id = "test", Name = "Test" },
                    Timestamp = DateTime.UtcNow
                }));
        }

        [Fact]
        public void Tree_Detangle_Tangle_RemovesAndDisposes()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());
            var tangle = tree1.Entangle(tree2);

            var initialCount = tree1.GetTangles().Count();
            Assert.Equal(1, initialCount);

            tree1.Detangle(tangle);

            var afterDetangle = tree1.GetTangles().Count();
            Assert.Equal(0, afterDetangle);

            // Tangle should throw after disposal
            Assert.Throws<ObjectDisposedException>(() =>
                tangle.PushUpdate("test", new User { Id = "test", Name = "Test" }));
        }

        [Fact]
        public void Tree_DetangleAll_ClearsAllConnections()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            var branch1 = new Branch("http://localhost:5000");
            var branch2 = new Branch("http://localhost:5001");
            var tree2 = new Tree<User>(new MemoryTrunk<User>());
            var tree3 = new Tree<User>(new MemoryTrunk<User>());

            tree.Entangle(branch1);
            tree.Entangle(branch2);
            tree.Entangle(tree2);
            tree.Entangle(tree3);

            var tangleCount = tree.GetTangles().Count();
            Assert.Equal(2, tangleCount); // Two in-process tangles

            tree.DetangleAll();

            var afterDetangle = tree.GetTangles().Count();
            Assert.Equal(0, afterDetangle);

            // All branches should be disposed
            Assert.Throws<ObjectDisposedException>(() =>
                branch1.TryPush("test", new Nut<User>
                {
                    Id = "test",
                    Payload = new User { Id = "test", Name = "Test" },
                    Timestamp = DateTime.UtcNow
                }));
        }

        [Fact]
        public void Grove_Detangle_RemovesTangle()
        {
            var grove = new Models.Grove();
            var tree = new Tree<User>(new MemoryTrunk<User>());
            grove.Plant(tree);

            var branch = new Branch("http://localhost:5000");
            var tangle = grove.Entangle<User>(branch, "test-tangle");

            Assert.NotNull(tangle);

            grove.Detangle(tangle);

            // Tangle should throw after disposal
            Assert.Throws<ObjectDisposedException>(() =>
                tangle.PushUpdate("test", new User { Id = "test", Name = "Test" }));
        }

        [Fact]
        public void Grove_DetangleAll_ClearsAllTangles()
        {
            var grove = new Models.Grove();
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<Product>(new MemoryTrunk<Product>());
            grove.Plant(tree1);
            grove.Plant(tree2);

            var branch1 = new Branch("http://localhost:5000");
            var branch2 = new Branch("http://localhost:5001");
            var tangle1 = grove.Entangle<User>(branch1, "tangle1");
            var tangle2 = grove.Entangle<Product>(branch2, "tangle2");

            grove.DetangleAll();

            // All tangles should be disposed
            Assert.Throws<ObjectDisposedException>(() =>
                tangle1.PushUpdate("test", new User { Id = "test", Name = "Test" }));
            Assert.Throws<ObjectDisposedException>(() =>
                tangle2.PushUpdate("test", new Product { Id = "test", Name = "Test Product" }));
        }

        [Fact]
        public void FluentUsage_EntangleAndDetangle()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());

            // Fluent entangle
            var branch = tree.Entangle(new Branch("http://localhost:5000"));

            // Use it
            Assert.NotNull(branch);

            // Snap it (nutty disconnect)
            branch.Snap();

            // Verify disposal
            Assert.Throws<ObjectDisposedException>(() =>
                branch.TryPush("test", new Nut<User>
                {
                    Id = "test",
                    Payload = new User { Id = "test", Name = "Test" },
                    Timestamp = DateTime.UtcNow
                }));
        }

        [Fact]
        public void UsingPattern_AutoDisposeBranch()
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            Branch? capturedBranch = null;

            using (var branch = tree.Entangle(new Branch("http://localhost:5000")))
            {
                capturedBranch = branch;
                tree.Stash("user1", new User { Id = "user1", Name = "Alice" });
                // Branch auto-pushes during stash
            }
            // Branch disposed here

            Assert.NotNull(capturedBranch);
            Assert.Throws<ObjectDisposedException>(() =>
                capturedBranch.TryPush("test", new Nut<User>
                {
                    Id = "test",
                    Payload = new User { Id = "test", Name = "Test" },
                    Timestamp = DateTime.UtcNow
                }));
        }

        [Fact]
        public void UsingPattern_AutoDisposeTangle()
        {
            var tree1 = new Tree<User>(new MemoryTrunk<User>());
            var tree2 = new Tree<User>(new MemoryTrunk<User>());
            Tangle<User>? capturedTangle = null;

            using (var tangle = tree1.Entangle(tree2))
            {
                capturedTangle = tangle;
                tree1.Stash("user1", new User { Id = "user1", Name = "Alice" });
                // Changes sync via tangle
            }
            // Tangle disposed here

            Assert.NotNull(capturedTangle);
            Assert.Throws<ObjectDisposedException>(() =>
                capturedTangle.PushUpdate("test", new User { Id = "test", Name = "Test" }));
        }
    }
}
