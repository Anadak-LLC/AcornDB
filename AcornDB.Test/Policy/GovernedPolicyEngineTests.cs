using System;
using AcornDB.Policy;
using AcornDB.Policy.Governance;
using AcornDB.Security;
using Xunit;

namespace AcornDB.Test.Policy;

/// <summary>
/// Tests for the GovernedPolicyEngine decorator.
/// </summary>
public class GovernedPolicyEngineTests
{
    private readonly IPolicySigner _signer = new Sha256PolicySigner();

    [Fact]
    public void Constructor_LoadsPoliciesFromLog()
    {
        var log = new MemoryPolicyLog(_signer);
        var policy = new TestPolicy("Preloaded");
        log.Append(policy, DateTime.UtcNow);

        var inner = new LocalPolicyEngine();
        var governed = new GovernedPolicyEngine(inner, log, _signer);

        var policies = governed.GetPolicies();
        Assert.Contains(policies, p => p.Name == "Preloaded");
    }

    [Fact]
    public void Constructor_VerifiesChainIntegrity()
    {
        var log = new MemoryPolicyLog(_signer);
        var inner = new LocalPolicyEngine();

        var governed = new GovernedPolicyEngine(inner, log, _signer);

        Assert.True(governed.IsChainVerified);
    }

    [Fact]
    public void AppendPolicy_AddsToLogAndEngine()
    {
        var log = new MemoryPolicyLog(_signer);
        var inner = new LocalPolicyEngine();
        var governed = new GovernedPolicyEngine(inner, log, _signer);

        var policy = new TestPolicy("NewPolicy");
        var seal = governed.AppendPolicy(policy, DateTime.UtcNow);

        Assert.Equal(0, seal.Index);
        Assert.Contains(governed.GetPolicies(), p => p.Name == "NewPolicy");
        Assert.Equal(1, log.Count);
    }

    [Fact]
    public void AppendPolicy_CreatesValidChain()
    {
        var log = new MemoryPolicyLog(_signer);
        var inner = new LocalPolicyEngine();
        var governed = new GovernedPolicyEngine(inner, log, _signer);

        governed.AppendPolicy(new TestPolicy("First"), DateTime.UtcNow);
        governed.AppendPolicy(new TestPolicy("Second"), DateTime.UtcNow.AddMinutes(1));

        var result = governed.VerifyChain();
        Assert.True(result.IsValid);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void PolicyLog_ExposesUnderlyingLog()
    {
        var log = new MemoryPolicyLog(_signer);
        var inner = new LocalPolicyEngine();
        var governed = new GovernedPolicyEngine(inner, log, _signer);

        Assert.Same(log, governed.PolicyLog);
    }

    [Fact]
    public void InnerEngine_ExposesUnderlyingEngine()
    {
        var log = new MemoryPolicyLog(_signer);
        var inner = new LocalPolicyEngine();
        var governed = new GovernedPolicyEngine(inner, log, _signer);

        Assert.Same(inner, governed.InnerEngine);
    }

    [Fact]
    public void Dispose_DisposesLog()
    {
        var log = new MemoryPolicyLog(_signer);
        var inner = new LocalPolicyEngine();
        var governed = new GovernedPolicyEngine(inner, log, _signer);

        governed.Dispose();

        // After dispose, operations should throw
        Assert.Throws<ObjectDisposedException>(() => governed.GetPolicies());
    }

    [Fact]
    public void ValidateAccess_DelegatesToInnerEngine()
    {
        var log = new MemoryPolicyLog(_signer);
        var inner = new LocalPolicyEngine();
        inner.GrantTagAccess("admin-tag", "admin");
        var governed = new GovernedPolicyEngine(inner, log, _signer);

        var entity = new TaggedEntity { Tags = new[] { "admin-tag" } };

        Assert.True(governed.ValidateAccess(entity, "admin"));
        Assert.False(governed.ValidateAccess(entity, "guest"));
    }

    [Fact]
    public void Constructor_SkipsVerification_WhenVerifyOnStartupFalse()
    {
        var log = new MemoryPolicyLog(_signer);
        var inner = new LocalPolicyEngine();

        var governed = new GovernedPolicyEngine(inner, log, _signer, verifyOnStartup: false);

        Assert.False(governed.IsChainVerified);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var log = new MemoryPolicyLog(_signer);
        var inner = new LocalPolicyEngine();
        var governed = new GovernedPolicyEngine(inner, log, _signer);

        governed.Dispose();
        governed.Dispose(); // Should not throw
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

    private class TaggedEntity : IPolicyTaggable
    {
        public string[] Tags { get; set; } = Array.Empty<string>();
        IEnumerable<string> IPolicyTaggable.Tags => Tags;
        public bool HasTag(string tag) => Array.Exists(Tags, t => t == tag);
    }
}
