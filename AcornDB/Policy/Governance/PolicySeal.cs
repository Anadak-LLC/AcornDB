using System;
using System.Text;
using AcornDB.Security;
using Newtonsoft.Json;

namespace AcornDB.Policy.Governance;

/// <summary>
/// Immutable, cryptographically sealed policy entry.
/// Once created, cannot be modified without breaking chain.
/// </summary>
public sealed record PolicySeal
{
    /// <summary>SHA-256 hash of (Content + Version + Timestamp).</summary>
    public required byte[] Signature { get; init; }

    /// <summary>When this policy became effective.</summary>
    public required DateTime EffectiveAt { get; init; }

    /// <summary>Hash of the previous entry (0x00... for genesis).</summary>
    public required byte[] PreviousHash { get; init; }

    /// <summary>The sealed policy content.</summary>
    public required IPolicyRule Policy { get; init; }

    /// <summary>Sequential index in the chain (0-based).</summary>
    public required int Index { get; init; }

    /// <summary>
    /// Create a new PolicySeal linked to the chain.
    /// </summary>
    public static PolicySeal Create(
        IPolicyRule policy,
        DateTime effectiveAt,
        PolicySeal? previous,
        IPolicySigner signer)
    {
        if (policy is null)
            throw new ArgumentException("Policy cannot be null.", nameof(policy));
        if (signer is null)
            throw new ArgumentException("Signer cannot be null.", nameof(signer));

        var index = previous?.Index + 1 ?? 0;
        var previousHash = previous?.Signature ?? new byte[32];

        if (previous is not null && effectiveAt < previous.EffectiveAt)
            throw new ArgumentException(
                "EffectiveAt must be >= previous entry's EffectiveAt.",
                nameof(effectiveAt));

        var dataToSign = BuildSignatureData(policy, effectiveAt, previousHash, index);
        var signature = signer.Sign(dataToSign);

        return new PolicySeal
        {
            Signature = signature,
            EffectiveAt = effectiveAt,
            PreviousHash = previousHash,
            Policy = policy,
            Index = index
        };
    }

    private static byte[] BuildSignatureData(
        IPolicyRule policy,
        DateTime effectiveAt,
        byte[] previousHash,
        int index)
    {
        var json = JsonConvert.SerializeObject(new
        {
            PolicyName = policy.Name,
            PolicyDescription = policy.Description,
            PolicyPriority = policy.Priority,
            EffectiveAt = effectiveAt.ToString("O"),
            PreviousHash = Convert.ToBase64String(previousHash),
            Index = index
        });
        return Encoding.UTF8.GetBytes(json);
    }
}
