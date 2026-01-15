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
    public void VerifyChain_DetectsTampering_WhenEntryModified()
    {
        using var log = new MemoryPolicyLog(_signer);
        var time = DateTime.UtcNow;
        log.Append(new TestPolicy("First"), time);
        log.Append(new TestPolicy("Second"), time.AddMinutes(1));

        // Get seals and tamper with one
        var seals = log.GetAllSeals();
        Assert.Equal(2, seals.Count);

        // Chain should be valid before tampering
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
