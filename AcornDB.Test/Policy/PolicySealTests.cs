using System;
using AcornDB.Policy;
using AcornDB.Policy.Governance;
using AcornDB.Security;
using Xunit;

namespace AcornDB.Test.Policy;

public class PolicySealTests
{
    private readonly IPolicySigner _signer = new Sha256PolicySigner();

    [Fact]
    public void Create_GeneratesValidSignature()
    {
        var policy = new TestPolicy("Test");
        var seal = PolicySeal.Create(policy, DateTime.UtcNow, null, _signer);

        Assert.NotNull(seal.Signature);
        Assert.Equal(32, seal.Signature.Length);
    }

    [Fact]
    public void Create_LinksToGenesis_WhenFirstEntry()
    {
        var policy = new TestPolicy("Test");
        var seal = PolicySeal.Create(policy, DateTime.UtcNow, null, _signer);

        Assert.Equal(0, seal.Index);
        Assert.Equal(new byte[32], seal.PreviousHash);
    }

    [Fact]
    public void Create_LinksToPrevious_WhenSubsequentEntry()
    {
        var policy1 = new TestPolicy("First");
        var policy2 = new TestPolicy("Second");
        var time1 = DateTime.UtcNow;
        var time2 = time1.AddMinutes(1);

        var seal1 = PolicySeal.Create(policy1, time1, null, _signer);
        var seal2 = PolicySeal.Create(policy2, time2, seal1, _signer);

        Assert.Equal(1, seal2.Index);
        Assert.Equal(seal1.Signature, seal2.PreviousHash);
    }

    [Fact]
    public void Create_ThrowsIfEffectiveAtBeforePrevious()
    {
        var policy1 = new TestPolicy("First");
        var policy2 = new TestPolicy("Second");
        var time1 = DateTime.UtcNow;
        var time2 = time1.AddMinutes(-1);

        var seal1 = PolicySeal.Create(policy1, time1, null, _signer);

        Assert.Throws<ArgumentException>(() =>
            PolicySeal.Create(policy2, time2, seal1, _signer));
    }

    private class TestPolicy : IPolicyRule
    {
        public TestPolicy(string name) => Name = name;
        public string Name { get; }
        public string Description => "Test policy";
        public int Priority => 50;
        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
            => PolicyEvaluationResult.Success();
    }
}
