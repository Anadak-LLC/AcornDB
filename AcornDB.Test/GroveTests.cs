
using Xunit;
using AcornDB;
using AcornDB.Models;
using AcornDB.Storage;
using AcornDB.Sync;
using System;
using System.Linq;

public class GroveTests
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
    public void Can_Plant_And_Get_Tree()
    {
        var grove = new Grove();
        var tree = new Tree<string>();

        grove.Plant(tree);

        var retrieved = grove.GetTree<string>();
        Assert.NotNull(retrieved);
    }

    [Fact]
    public void Can_Entangle_Tree_With_Branch()
    {
        var grove = new Grove();
        var tree = new Tree<string>();
        grove.Plant(tree);

        var branch = new Branch("http://localhost:5000");
        var tangle = grove.Entangle<string>(branch, "test-tangle");

        Assert.NotNull(tangle);
    }

    [Fact]
    public void EntangleAll_Mesh_TwoTrees_CreatesSingleTangle()
    {
        // Arrange
        var grove = new Grove();
        var tree1 = new Tree<User>(new MemoryTrunk<User>());
        var tree2 = new Tree<User>(new MemoryTrunk<User>());

        grove.Plant(tree1);
        grove.Plant(tree2);

        // Act
        var tangleCount = grove.EntangleAll(bidirectional: true);

        // Assert
        Assert.Equal(1, tangleCount); // 2 trees = 1 tangle (tree1 ↔ tree2)
    }

    [Fact]
    public void EntangleAll_Mesh_FiveTrees_CreatesCorrectTangles()
    {
        // Arrange
        var grove = new Grove();
        for (int i = 0; i < 5; i++)
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            grove.Plant(tree);
        }

        // Act
        var tangleCount = grove.EntangleAll(bidirectional: true);

        // Assert
        // 5 trees = 10 tangles in a full mesh: (5 * 4) / 2 = 10
        // But we only create 4 tangles because we use a simple pairing (each tree connects to one other)
        // Actually with the i+1 loop, we get: C(5,2) = 10 pairs, but only same-type trees connect
        // Since all are User trees, we expect: C(5,2) = 10 tangles
        // Wait, looking at the code, we do i < trees.Count and j = i + 1
        // So for 5 trees: (0,1), (0,2), (0,3), (0,4), (1,2), (1,3), (1,4), (2,3), (2,4), (3,4) = 10 pairs
        // But they all have same type, so we create tangles for all 10 pairs
        // However, Tree.Entangle(Tree<T>) only creates one tangle from tree1 to tree2
        // So we expect: 5 choose 2 = 10 tangles... but wait
        // Actually, the implementation calls tree1.Entangle(tree2), which creates a single Tangle
        // So for 5 trees of same type, we get 10 tangles
        Assert.Equal(10, tangleCount);
    }

    [Fact]
    public void EntangleAll_Mesh_TenTrees_CreatesCorrectTangles()
    {
        // Arrange
        var grove = new Grove();
        for (int i = 0; i < 10; i++)
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            grove.Plant(tree);
        }

        // Act
        var tangleCount = grove.EntangleAll(bidirectional: true);

        // Assert
        // 10 trees = C(10,2) = 45 tangles in a full mesh
        Assert.Equal(45, tangleCount);
    }

    [Fact]
    public void EntangleAll_Mesh_MixedTypes_OnlyEntanglesSameTypes()
    {
        // Arrange
        var grove = new Grove();

        // Plant 3 User trees
        for (int i = 0; i < 3; i++)
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            grove.Plant(tree);
        }

        // Plant 2 Product trees
        for (int i = 0; i < 2; i++)
        {
            var tree = new Tree<Product>(new MemoryTrunk<Product>());
            grove.Plant(tree);
        }

        // Act
        var tangleCount = grove.EntangleAll(bidirectional: true);

        // Assert
        // 3 User trees = C(3,2) = 3 tangles
        // 2 Product trees = C(2,2) = 1 tangle
        // Total = 4 tangles (User and Product trees don't interconnect)
        Assert.Equal(4, tangleCount);
    }

    [Fact]
    public void EntangleAll_Mesh_NoDuplicateTangles()
    {
        // Arrange
        var grove = new Grove();
        var tree1 = new Tree<User>(new MemoryTrunk<User>());
        var tree2 = new Tree<User>(new MemoryTrunk<User>());

        grove.Plant(tree1);
        grove.Plant(tree2);

        // Act - call EntangleAll twice
        var tangleCount1 = grove.EntangleAll(bidirectional: true);
        var tangleCount2 = grove.EntangleAll(bidirectional: true);

        // Assert - second call should not create new tangles (already connected)
        Assert.Equal(1, tangleCount1);
        Assert.Equal(0, tangleCount2); // No new tangles created
    }

    [Fact]
    public void EntangleAll_Mesh_DataSyncsAcrossTrees()
    {
        // Arrange
        var grove = new Grove();
        var tree1 = new Tree<User>(new MemoryTrunk<User>());
        var tree2 = new Tree<User>(new MemoryTrunk<User>());

        grove.Plant(tree1);
        grove.Plant(tree2);

        // Act - create mesh and stash data
        grove.EntangleAll(bidirectional: true);

        var user = new User { Id = "user1", Name = "Alice" };
        tree1.Stash(user);

        // Allow some time for sync (in-process should be immediate)
        System.Threading.Thread.Sleep(100);

        // Assert - data should sync to tree2
        var syncedUser = tree2.Crack("user1");
        Assert.NotNull(syncedUser);
        Assert.Equal("Alice", syncedUser.Name);
    }

    [Fact]
    public void EntangleAll_Mesh_TwentyTrees_LargeScale()
    {
        // Arrange
        var grove = new Grove();
        for (int i = 0; i < 20; i++)
        {
            var tree = new Tree<User>(new MemoryTrunk<User>());
            grove.Plant(tree);
        }

        // Act
        var tangleCount = grove.EntangleAll(bidirectional: true);

        // Assert
        // 20 trees = C(20,2) = 190 tangles
        Assert.Equal(190, tangleCount);
    }

    [Fact]
    public void DetangleAll_ClearsAllMeshTangles()
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

        // Verify tangles were created
        var initialTangles1 = tree1.GetTangles().Count();
        var initialTangles2 = tree2.GetTangles().Count();
        var initialTangles3 = tree3.GetTangles().Count();

        Assert.True(initialTangles1 > 0 || initialTangles2 > 0 || initialTangles3 > 0);

        // Act
        grove.DetangleAll();

        // Assert - grove-level tangles cleared (note: tree-level tangles are managed separately)
        // The grove's DetangleAll clears the grove's _tangles list
        var stats = grove.GetNutStats();
        Assert.Equal(0, stats.ActiveTangles); // All tangles should be cleared
    }

    [Fact]
    public void EntangleAll_Remote_ConnectsAllTreesToRemote()
    {
        // Arrange
        var grove = new Grove();
        var tree1 = new Tree<User>(new MemoryTrunk<User>());
        var tree2 = new Tree<User>(new MemoryTrunk<User>());

        grove.Plant(tree1);
        grove.Plant(tree2);

        // Act - connect all trees to a remote URL (star topology)
        grove.EntangleAll("http://localhost:5000");

        // Assert - this is just a smoke test since we can't verify HTTP without a server
        // The method should not throw
        Assert.Equal(2, grove.TreeCount);
    }
}
