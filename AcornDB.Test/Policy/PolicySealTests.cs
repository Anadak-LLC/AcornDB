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
        Assert.True(seal.VerifySignature(_signer));
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

    [Fact]
    public void Signature_ReturnsClonedArray()
    {
        var policy = new TestPolicy("Test");
        var seal = PolicySeal.Create(policy, DateTime.UtcNow, null, _signer);

        var sig1 = seal.Signature;
        var sig2 = seal.Signature;

        Assert.NotSame(sig1, sig2);
        Assert.Equal(sig1, sig2);

        // Modifying returned array doesn't affect seal
        sig1[0] ^= 0xFF;
        Assert.NotEqual(sig1, seal.Signature);
    }

    [Fact]
    public void PreviousHash_ReturnsClonedArray()
    {
        var policy = new TestPolicy("Test");
        var seal = PolicySeal.Create(policy, DateTime.UtcNow, null, _signer);

        var hash1 = seal.PreviousHash;
        var hash2 = seal.PreviousHash;

        Assert.NotSame(hash1, hash2);
        Assert.Equal(hash1, hash2);

        // Modifying returned array doesn't affect seal
        hash1[0] ^= 0xFF;
        Assert.NotEqual(hash1, seal.PreviousHash);
    }

    [Fact]
    public void Create_DifferentTypes_WithSameMetadata_ProduceDifferentSignatures()
    {
        // GOV-003: Verify that policy type is included in signature
        // Two different policy classes with identical Name/Description/Priority
        // should produce different signatures to prevent type-swapping attacks
        var policyA = new TestPolicy("Same");
        var policyB = new AlternateTestPolicy("Same");
        var timestamp = DateTime.UtcNow;

        var sealA = PolicySeal.Create(policyA, timestamp, null, _signer);
        var sealB = PolicySeal.Create(policyB, timestamp, null, _signer);

        // Signatures must differ because types are different
        Assert.NotEqual(sealA.Signature, sealB.Signature);
    }

    [Fact]
    public void Create_WithRootChainHash_IncludesInSignature()
    {
        // GAP-001: RootChainAtActivation must be included in signature
        var policy = new TestPolicy("Test");
        var timestamp = DateTime.UtcNow;
        var rootChainHashA = new byte[32];
        var rootChainHashB = new byte[32];
        rootChainHashB[0] = 0xFF; // Different root chain

        var sealA = PolicySeal.Create(policy, timestamp, null, _signer, rootChainHashA);
        var sealB = PolicySeal.Create(policy, timestamp, null, _signer, rootChainHashB);

        // Signatures must differ because root chain hashes differ
        Assert.NotEqual(sealA.Signature, sealB.Signature);
        Assert.Equal(rootChainHashA, sealA.RootChainHash);
        Assert.Equal(rootChainHashB, sealB.RootChainHash);
    }

    [Fact]
    public void RootChainHash_ReturnsClonedArray()
    {
        var policy = new TestPolicy("Test");
        var rootChainHash = new byte[32];
        rootChainHash[0] = 0xAB;
        var seal = PolicySeal.Create(policy, DateTime.UtcNow, null, _signer, rootChainHash);

        var hash1 = seal.RootChainHash;
        var hash2 = seal.RootChainHash;

        Assert.NotSame(hash1, hash2);
        Assert.Equal(hash1, hash2);

        // Modifying returned array doesn't affect seal
        hash1[0] ^= 0xFF;
        Assert.NotEqual(hash1, seal.RootChainHash);
    }

    [Fact]
    public void Create_WithoutRootChainHash_DefaultsToZeros()
    {
        var policy = new TestPolicy("Test");
        var seal = PolicySeal.Create(policy, DateTime.UtcNow, null, _signer);

        Assert.Equal(new byte[32], seal.RootChainHash);
    }

    [Fact]
    public void Create_ThrowsArgumentException_ForNullPolicy()
    {
        Assert.Throws<ArgumentException>(() =>
            PolicySeal.Create(null!, DateTime.UtcNow, null, _signer));
    }

    [Fact]
    public void Create_ThrowsArgumentException_ForNullSigner()
    {
        Assert.Throws<ArgumentException>(() =>
            PolicySeal.Create(new TestPolicy("Test"), DateTime.UtcNow, null, null!));
    }

    [Fact]
    public void Create_AcceptsEqualTimestamps()
    {
        var policy1 = new TestPolicy("First");
        var policy2 = new TestPolicy("Second");
        var time = DateTime.UtcNow;

        var seal1 = PolicySeal.Create(policy1, time, null, _signer);
        var seal2 = PolicySeal.Create(policy2, time, seal1, _signer);

        Assert.Equal(1, seal2.Index);
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

    /// <summary>
    /// Alternate policy implementation with identical metadata for testing GOV-003.
    /// </summary>
    private class AlternateTestPolicy : IPolicyRule
    {
        public AlternateTestPolicy(string name) => Name = name;
        public string Name { get; }
        public string Description => "Test policy";
        public int Priority => 50;
        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
            => PolicyEvaluationResult.Success();
    }
}
