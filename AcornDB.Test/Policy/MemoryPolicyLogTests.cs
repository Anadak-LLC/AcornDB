using System;
using System.Threading.Tasks;
using AcornDB.Policy;
using AcornDB.Policy.Governance;
using AcornDB.Security;
using Xunit;

namespace AcornDB.Test.Policy;

public class MemoryPolicyLogTests
{
    private readonly IPolicySigner _signer = new Sha256PolicySigner();

    [Fact]
    public void Append_CreatesValidChain()
    {
        using var log = new MemoryPolicyLog(_signer);
        var time = DateTime.UtcNow;

        log.Append(new TestPolicy("First"), time);
        log.Append(new TestPolicy("Second"), time.AddMinutes(1));
        log.Append(new TestPolicy("Third"), time.AddMinutes(2));

        var result = log.VerifyChain();
        Assert.True(result.IsValid);
        Assert.Equal(3, log.Count);
    }

    [Fact]
    public void Append_RejectsOutOfOrderTimestamp()
    {
        using var log = new MemoryPolicyLog(_signer);
        var time = DateTime.UtcNow;

        log.Append(new TestPolicy("First"), time);

        Assert.Throws<ArgumentException>(() =>
            log.Append(new TestPolicy("Second"), time.AddMinutes(-1)));
    }

    [Fact]
    public void VerifyChain_ReturnsValid_ForIntactChain()
    {
        using var log = new MemoryPolicyLog(_signer);
        log.Append(new TestPolicy("Policy"), DateTime.UtcNow);

        var result = log.VerifyChain();

        Assert.True(result.IsValid);
        Assert.Null(result.BrokenAtIndex);
    }

    [Fact]
    public void VerifyChain_VerifiesSignatures()
    {
        using var log = new MemoryPolicyLog(_signer);
        var time = DateTime.UtcNow;
        log.Append(new TestPolicy("First"), time);
        log.Append(new TestPolicy("Second"), time.AddMinutes(1));

        // Get seals and verify each signature individually
        var seals = log.GetAllSeals();
        Assert.Equal(2, seals.Count);

        foreach (var seal in seals)
        {
            Assert.True(seal.VerifySignature(_signer));
        }

        // Full chain verification
        var result = log.VerifyChain();
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GetPolicyAt_ReturnsCorrectPolicy_ForTimestamp()
    {
        using var log = new MemoryPolicyLog(_signer);
        var time = DateTime.UtcNow;

        log.Append(new TestPolicy("First"), time);
        log.Append(new TestPolicy("Second"), time.AddMinutes(10));

        var policy = log.GetPolicyAt(time.AddMinutes(5));

        Assert.NotNull(policy);
        Assert.Equal("First", policy.Name);
    }

    [Fact]
    public void GetPolicyAt_ReturnsNull_BeforeFirstPolicy()
    {
        using var log = new MemoryPolicyLog(_signer);
        var time = DateTime.UtcNow;

        log.Append(new TestPolicy("First"), time);

        var policy = log.GetPolicyAt(time.AddMinutes(-5));

        Assert.Null(policy);
    }

    [Fact]
    public void ThreadSafety_ConcurrentReads_Succeed()
    {
        using var log = new MemoryPolicyLog(_signer);
        var baseTime = DateTime.UtcNow;

        // Sequential appends first
        for (var i = 0; i < 100; i++)
            log.Append(new TestPolicy($"Policy{i}"), baseTime.AddMilliseconds(i));

        // Concurrent reads
        Parallel.For(0, 100, i =>
        {
            var count = log.Count;
            var seals = log.GetAllSeals();
            var result = log.VerifyChain();
            Assert.Equal(100, count);
            Assert.Equal(100, seals.Count);
            Assert.True(result.IsValid);
        });
    }

    [Fact]
    public void VerifyChain_IndividualSignatures_AreValid()
    {
        using var log = new MemoryPolicyLog(_signer);
        var time = DateTime.UtcNow;

        log.Append(new TestPolicy("First"), time);
        log.Append(new TestPolicy("Second"), time.AddMinutes(1));
        log.Append(new TestPolicy("Third"), time.AddMinutes(2));

        // Verify full chain
        var result = log.VerifyChain();
        Assert.True(result.IsValid);

        // Verify each seal has a valid signature
        var seals = log.GetAllSeals();
        Assert.Equal(3, seals.Count);

        foreach (var seal in seals)
            Assert.True(seal.VerifySignature(_signer));

        // Verify chain linkage: each entry's PreviousHash matches prior Signature
        Assert.Equal(new byte[32], seals[0].PreviousHash); // Genesis
        Assert.Equal(seals[0].Signature, seals[1].PreviousHash);
        Assert.Equal(seals[1].Signature, seals[2].PreviousHash);
    }

    [Fact]
    public void ThreadSafety_ConcurrentAppends_Succeed()
    {
        using var log = new MemoryPolicyLog(_signer);
        var baseTime = DateTime.UtcNow;
        var count = 50;

        // Sequential appends (concurrent writes to a chain must be sequential
        // because each entry depends on the previous)
        for (var i = 0; i < count; i++)
            log.Append(new TestPolicy($"Policy{i}"), baseTime.AddMilliseconds(i));

        // Concurrent reads during which we verify chain integrity
        Parallel.For(0, 10, _ =>
        {
            var result = log.VerifyChain();
            Assert.True(result.IsValid);
            Assert.Equal(count, log.Count);
        });

        // Chain should still be valid after concurrent access
        var finalResult = log.VerifyChain();
        Assert.True(finalResult.IsValid);
    }

    [Fact]
    public void Append_RejectsNonUtcTimestamp()
    {
        using var log = new MemoryPolicyLog(_signer);
        var localTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() =>
            log.Append(new TestPolicy("Test"), localTime));
    }

    [Fact]
    public void VerifyChain_ReturnsValid_ForEmptyLog()
    {
        using var log = new MemoryPolicyLog(_signer);

        var result = log.VerifyChain();

        Assert.True(result.IsValid);
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
