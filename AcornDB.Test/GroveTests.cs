
using Xunit;
using AcornDB;
using AcornDB.Models;
using AcornDB.Sync;
using System;

public class GroveTests
{
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
}
