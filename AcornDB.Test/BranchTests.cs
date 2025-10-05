
using Xunit;
using AcornDB;
using System;
using AcornDB.Sync;

public class BranchTests
{
    [Fact]
    public void Can_Create_Branch()
    {
        var branch = new Branch("http://localhost:5000");
        Assert.Equal("http://localhost:5000", branch.RemoteUrl);
    }
}
