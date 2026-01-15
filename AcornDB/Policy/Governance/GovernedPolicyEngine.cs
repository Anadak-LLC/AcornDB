using System;
using System.Collections.Generic;
using AcornDB.Logging;
using AcornDB.Security;

namespace AcornDB.Policy.Governance;

/// <summary>
/// Decorator that adds governance capabilities (hash-chained audit trail) to any IPolicyEngine.
/// Follows the Decorator pattern to keep LocalPolicyEngine clean and governance optional.
/// </summary>
/// <remarks>
/// <para>
/// Usage:
/// <code>
/// var baseEngine = new LocalPolicyEngine(options);
/// var signer = new Sha256PolicySigner();
/// var log = new MemoryPolicyLog(signer);
/// var governed = new GovernedPolicyEngine(baseEngine, log, signer);
/// </code>
/// </para>
/// </remarks>
public sealed class GovernedPolicyEngine : IPolicyEngine, IDisposable
{
    private readonly IPolicyEngine _innerEngine;
    private readonly IPolicyLog _policyLog;
    private readonly IPolicySigner _signer;
    private readonly bool _verifyOnStartup;
    private bool _chainVerified;
    private bool _disposed;

    /// <summary>
    /// Creates a governed policy engine wrapping the specified engine.
    /// </summary>
    /// <param name="innerEngine">The base policy engine to decorate.</param>
    /// <param name="policyLog">Hash-chained policy log for governance.</param>
    /// <param name="signer">Cryptographic signer for chain operations.</param>
    /// <param name="verifyOnStartup">If true, verifies chain integrity on first operation.</param>
    /// <exception cref="ArgumentNullException">If any required parameter is null.</exception>
    public GovernedPolicyEngine(
        IPolicyEngine innerEngine,
        IPolicyLog policyLog,
        IPolicySigner signer,
        bool verifyOnStartup = true)
    {
        _innerEngine = innerEngine ?? throw new ArgumentNullException(nameof(innerEngine));
        _policyLog = policyLog ?? throw new ArgumentNullException(nameof(policyLog));
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _verifyOnStartup = verifyOnStartup;

        LoadPoliciesFromLog();
    }

    /// <summary>Gets the underlying policy log.</summary>
    public IPolicyLog PolicyLog => _policyLog;

    /// <summary>Gets the underlying policy engine.</summary>
    public IPolicyEngine InnerEngine => _innerEngine;

    /// <summary>Gets whether the chain has been verified.</summary>
    public bool IsChainVerified => _chainVerified;

    private void LoadPoliciesFromLog()
    {
        if (_verifyOnStartup)
            EnsureChainVerified();

        foreach (var seal in _policyLog.GetAllSeals())
            _innerEngine.RegisterPolicy(seal.Policy);

        AcornLog.Info($"🔐 [GOVERNED] Loaded {_policyLog.Count} policies from governance ledger");
    }

    private void EnsureChainVerified()
    {
        if (_chainVerified) return;

        var result = _policyLog.VerifyChain();
        if (!result.IsValid)
        {
            throw new ChainIntegrityException(
                result.Details ?? "Policy log chain integrity verification failed",
                result.BrokenAtIndex ?? -1);
        }

        _chainVerified = true;
        AcornLog.Info("🔐 [GOVERNED] Chain integrity verified");
    }

    /// <summary>
    /// Append a new policy to the governance log and register it with the engine.
    /// </summary>
    /// <param name="policy">Policy rule to add.</param>
    /// <param name="effectiveAt">When the policy becomes effective (UTC).</param>
    /// <returns>The sealed policy entry.</returns>
    public PolicySeal AppendPolicy(IPolicyRule policy, DateTime effectiveAt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var seal = _policyLog.Append(policy, effectiveAt);
        _innerEngine.RegisterPolicy(policy);

        AcornLog.Info($"🔐 [GOVERNED] Policy '{policy.Name}' appended (Index: {seal.Index})");
        return seal;
    }

    /// <summary>Verify chain integrity on demand.</summary>
    public ChainValidationResult VerifyChain()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = _policyLog.VerifyChain();
        _chainVerified = result.IsValid;
        return result;
    }

    /// <inheritdoc />
    public void ApplyPolicies<T>(T entity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureChainVerified();
        _innerEngine.ApplyPolicies(entity);
    }

    /// <inheritdoc />
    public bool ValidateAccess<T>(T entity, string userRole)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureChainVerified();
        return _innerEngine.ValidateAccess(entity, userRole);
    }

    /// <inheritdoc />
    public void EnforceTTL<T>(IEnumerable<T> entities)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureChainVerified();
        _innerEngine.EnforceTTL(entities);
    }

    /// <inheritdoc />
    public void RegisterPolicy(IPolicyRule policyRule)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AcornLog.Warning("🔐 [GOVERNED] RegisterPolicy bypasses audit trail - use AppendPolicy");
        _innerEngine.RegisterPolicy(policyRule);
    }

    /// <inheritdoc />
    public bool UnregisterPolicy(string policyName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AcornLog.Warning($"🔐 [GOVERNED] UnregisterPolicy '{policyName}' - remains in log");
        return _innerEngine.UnregisterPolicy(policyName);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IPolicyRule> GetPolicies()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _innerEngine.GetPolicies();
    }

    /// <inheritdoc />
    public PolicyValidationResult Validate<T>(T entity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureChainVerified();
        return _innerEngine.Validate(entity);
    }

    /// <summary>Disposes the governed policy engine.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        if (_policyLog is IDisposable disposableLog)
            disposableLog.Dispose();
        _disposed = true;
    }
}
