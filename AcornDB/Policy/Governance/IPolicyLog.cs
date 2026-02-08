using System;
using System.Collections.Generic;

namespace AcornDB.Policy.Governance;

/// <summary>
/// Append-only, hash-chained policy storage with cryptographic integrity.
/// Provides tamper-evident audit trail for policy changes.
/// </summary>
public interface IPolicyLog
{
    /// <summary>Append a new policy to the log. Returns sealed entry.</summary>
    PolicySeal Append(IPolicyRule policy, DateTime effectiveAt);

    /// <summary>Get the policy that was active at a specific timestamp.</summary>
    IPolicyRule? GetPolicyAt(DateTime timestamp);

    /// <summary>Get all policy seals in chronological order.</summary>
    IReadOnlyList<PolicySeal> GetAllSeals();

    /// <summary>Validate the entire chain integrity.</summary>
    ChainValidationResult VerifyChain();

    /// <summary>Number of entries in the log.</summary>
    int Count { get; }
}
