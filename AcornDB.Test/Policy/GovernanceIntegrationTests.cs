using System;
using System.Text;
using AcornDB.Policy;
using AcornDB.Policy.Governance;
using AcornDB.Security;
using AcornDB.Storage;
using AcornDB.Storage.Roots;
using Xunit;

namespace AcornDB.Test.Policy;

/// <summary>
/// Integration tests for the governance policy system including LocalPolicyEngine
/// and PolicyEnforcementRoot integration with IPolicyLog.
/// </summary>
public class GovernanceIntegrationTests
{
    private readonly IPolicySigner _signer = new Sha256PolicySigner();

    [Fact]
    public void LocalPolicyEngine_LoadsPoliciesFromLog()
    {
        // Arrange
        using var log = new MemoryPolicyLog(_signer);
        var time = DateTime.UtcNow;
        log.Append(new TestGovernancePolicy("GovPolicy1"), time);
        log.Append(new TestGovernancePolicy("GovPolicy2"), time.AddMinutes(1));

        // Act
        var engine = new LocalPolicyEngine(new LocalPolicyEngineOptions(), log);

        // Assert
        var policies = engine.GetPolicies();
        Assert.Contains(policies, p => p.Name == "GovPolicy1");
        Assert.Contains(policies, p => p.Name == "GovPolicy2");
        Assert.NotNull(engine.PolicyLog);
    }

    [Fact]
    public void LocalPolicyEngine_ThrowsOnInvalidChain()
    {
        // Arrange - Create a log with tampered chain (simulated via manual seal list)
        using var log = new TamperedPolicyLog(_signer);

        // Act & Assert
        var ex = Assert.Throws<ChainIntegrityException>(() =>
            new LocalPolicyEngine(new LocalPolicyEngineOptions(), log));
        Assert.Contains("integrity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalPolicyEngine_WorksWithoutLog_BackwardCompat()
    {
        // Arrange & Act
        var engine = new LocalPolicyEngine(new LocalPolicyEngineOptions());

        // Assert - Should have default policies, no log
        Assert.Null(engine.PolicyLog);
        var policies = engine.GetPolicies();
        Assert.True(policies.Count >= 2);
    }

    [Fact]
    public void PolicyEnforcementRoot_VerifiesChain_OnFirstOperation()
    {
        // Arrange
        using var log = new MemoryPolicyLog(_signer);
        log.Append(new TestGovernancePolicy("TestPolicy"), DateTime.UtcNow);

        var engine = new LocalPolicyEngine();
        var root = new PolicyEnforcementRoot(engine, log);
        var context = new RootProcessingContext { DocumentId = "test-doc" };
        var data = Encoding.UTF8.GetBytes("{\"Value\":\"test\"}");

        // Act
        root.OnStash(data, context);

        // Assert
        Assert.NotNull(context.ChainState);
        Assert.True(context.ChainState.IsValid);
    }

    [Fact]
    public void PolicyEnforcementRoot_CachesChainValidation()
    {
        // Arrange
        using var log = new MemoryPolicyLog(_signer);
        log.Append(new TestGovernancePolicy("TestPolicy"), DateTime.UtcNow);

        var engine = new LocalPolicyEngine();
        var root = new PolicyEnforcementRoot(engine, log);
        var data = Encoding.UTF8.GetBytes("{\"Value\":\"test\"}");

        // Act - Multiple operations should use cached chain state
        var context1 = new RootProcessingContext { DocumentId = "doc1" };
        var context2 = new RootProcessingContext { DocumentId = "doc2" };

        root.OnStash(data, context1);
        root.OnCrack(data, context2);

        // Assert - Both contexts should have chain state
        Assert.NotNull(context1.ChainState);
        Assert.NotNull(context2.ChainState);
        Assert.True(context1.ChainState.IsValid);
        Assert.True(context2.ChainState.IsValid);
    }

    [Fact]
    public void PolicyEnforcementRoot_WorksWithoutLog_BackwardCompat()
    {
        // Arrange
        var engine = new LocalPolicyEngine();
        var root = new PolicyEnforcementRoot(engine); // No policy log
        var context = new RootProcessingContext { DocumentId = "test-doc" };
        var data = Encoding.UTF8.GetBytes("{\"Value\":\"test\"}");

        // Act
        var result = root.OnStash(data, context);

        // Assert - Should work without exception, ChainState remains null
        Assert.Null(context.ChainState);
        Assert.Null(root.PolicyLog);
        Assert.Equal(data, result);
    }

    [Fact]
    public void EndToEnd_PolicyFromLog_EnforcedInRoot()
    {
        // Arrange - Create policy log with a denying policy
        using var log = new MemoryPolicyLog(_signer);
        log.Append(new DenyAllPolicy(), DateTime.UtcNow);

        // Create engine from log
        var engine = new LocalPolicyEngine(
            new LocalPolicyEngineOptions { EnforceAllPolicies = true },
            log);

        var root = new PolicyEnforcementRoot(
            engine,
            log,
            options: new PolicyEnforcementOptions { ThrowOnPolicyViolation = true });

        var context = new RootProcessingContext { DocumentId = "test-doc" };
        var data = Encoding.UTF8.GetBytes("{\"Value\":\"test\"}");

        // Act & Assert - Policy from log should be enforced
        var ex = Assert.Throws<PolicyViolationException>(() => root.OnStash(data, context));
        Assert.Contains("DenyAllPolicy", ex.Message);
    }

    [Fact]
    public void PolicyEnforcementRoot_InvalidateChainCache_ForcesRevalidation()
    {
        // Arrange
        using var log = new MemoryPolicyLog(_signer);
        log.Append(new TestGovernancePolicy("Initial"), DateTime.UtcNow);

        var engine = new LocalPolicyEngine();
        var root = new PolicyEnforcementRoot(engine, log);
        var data = Encoding.UTF8.GetBytes("{\"Value\":\"test\"}");

        // First operation to cache chain state
        var context1 = new RootProcessingContext { DocumentId = "doc1" };
        root.OnStash(data, context1);
        Assert.NotNull(context1.ChainState);

        // Act - Invalidate cache and run again
        root.InvalidateChainCache();
        var context2 = new RootProcessingContext { DocumentId = "doc2" };
        root.OnStash(data, context2);

        // Assert - Chain should be re-validated
        Assert.NotNull(context2.ChainState);
        Assert.True(context2.ChainState.IsValid);
    }

    // Helper test classes
    private class TestGovernancePolicy : IPolicyRule
    {
        public TestGovernancePolicy(string name) => Name = name;
        public string Name { get; }
        public string Description => "Test governance policy";
        public int Priority => 50;

        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
            => PolicyEvaluationResult.Success();
    }

    private class DenyAllPolicy : IPolicyRule
    {
        public string Name => "DenyAllPolicy";
        public string Description => "Denies all operations";
        public int Priority => 100;

        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
            => PolicyEvaluationResult.Failure("DenyAllPolicy: Access denied");
    }

    /// <summary>
    /// A policy log that simulates a tampered/invalid chain.
    /// </summary>
    private class TamperedPolicyLog : IPolicyLog, IDisposable
    {
        private readonly IPolicySigner _signer;

        public TamperedPolicyLog(IPolicySigner signer) => _signer = signer;

        public int Count => 1;

        public PolicySeal Append(IPolicyRule policy, DateTime effectiveAt)
            => throw new NotSupportedException();

        public IPolicyRule? GetPolicyAt(DateTime timestamp) => null;

        public System.Collections.Generic.IReadOnlyList<PolicySeal> GetAllSeals()
            => Array.Empty<PolicySeal>();

        public ChainValidationResult VerifyChain()
            => ChainValidationResult.Invalid(0, "Chain integrity compromised - tampering detected");

        public void Dispose() { }
    }
}
