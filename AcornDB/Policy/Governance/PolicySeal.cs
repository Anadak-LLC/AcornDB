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
    private readonly byte[] _signature;
    private readonly byte[] _previousHash;
    private readonly byte[] _rootChainHash;

    /// <summary>SHA-256 hash of (Content + Version + Timestamp).</summary>
    public byte[] Signature => (byte[])_signature.Clone();

    /// <summary>When this policy became effective.</summary>
    public DateTime EffectiveAt { get; }

    /// <summary>Hash of the previous entry (0x00... for genesis).</summary>
    public byte[] PreviousHash => (byte[])_previousHash.Clone();

    /// <summary>The sealed policy content.</summary>
    public IPolicyRule Policy { get; }

    /// <summary>Sequential index in the chain (0-based).</summary>
    public int Index { get; }

    /// <summary>
    /// Hash of the Root pipeline configuration at time of sealing.
    /// Records which transformations (compression, encryption) were active.
    /// </summary>
    public byte[] RootChainHash => (byte[])_rootChainHash.Clone();

    private PolicySeal(byte[] signature, DateTime effectiveAt, byte[] previousHash, IPolicyRule policy, int index, byte[] rootChainHash)
    {
        _signature = (byte[])signature.Clone();
        EffectiveAt = effectiveAt;
        _previousHash = (byte[])previousHash.Clone();
        Policy = policy;
        Index = index;
        _rootChainHash = (byte[])rootChainHash.Clone();
    }

    /// <summary>
    /// Create a new PolicySeal linked to the chain.
    /// </summary>
    /// <param name="policy">The policy rule to seal.</param>
    /// <param name="effectiveAt">When the policy becomes effective (UTC).</param>
    /// <param name="previous">Previous seal in the chain (null for genesis).</param>
    /// <param name="signer">Cryptographic signer for the seal.</param>
    /// <param name="rootChainHash">Hash of Root pipeline configuration (optional, defaults to zeros).</param>
    public static PolicySeal Create(
        IPolicyRule policy,
        DateTime effectiveAt,
        PolicySeal? previous,
        IPolicySigner signer,
        byte[]? rootChainHash = null)
    {
        if (policy is null)
            throw new ArgumentException("Policy cannot be null.", nameof(policy));
        if (signer is null)
            throw new ArgumentException("Signer cannot be null.", nameof(signer));

        var index = previous?.Index + 1 ?? 0;
        var previousHash = previous?._signature ?? new byte[32];
        var chainHash = rootChainHash ?? new byte[32]; // Default to zeros if not provided

        if (previous is not null && effectiveAt < previous.EffectiveAt)
            throw new ArgumentException(
                "EffectiveAt must be >= previous entry's EffectiveAt.",
                nameof(effectiveAt));

        var dataToSign = BuildSignatureData(policy, effectiveAt, previousHash, index, chainHash);
        var signature = signer.Sign(dataToSign);

        return new PolicySeal(signature, effectiveAt, previousHash, policy, index, chainHash);
    }

    /// <summary>
    /// Verify this seal's signature is valid.
    /// </summary>
    public bool VerifySignature(IPolicySigner signer)
    {
        if (signer is null)
            throw new ArgumentException("Signer cannot be null.", nameof(signer));

        var dataToSign = BuildSignatureData(Policy, EffectiveAt, _previousHash, Index, _rootChainHash);
        return signer.Verify(dataToSign, _signature);
    }

    /// <summary>
    /// Check if this seal's PreviousHash matches the given signature.
    /// </summary>
    internal bool PreviousHashMatches(byte[] expectedPreviousHash)
    {
        if (expectedPreviousHash.Length != _previousHash.Length)
            return false;

        for (var i = 0; i < _previousHash.Length; i++)
        {
            if (_previousHash[i] != expectedPreviousHash[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Reconstruct a PolicySeal from persisted data (internal use only).
    /// Does not validate signature - caller must verify chain integrity.
    /// </summary>
    internal static PolicySeal Reconstruct(
        byte[] signature,
        DateTime effectiveAt,
        byte[] previousHash,
        IPolicyRule policy,
        int index,
        byte[]? rootChainHash = null)
    {
        return new PolicySeal(signature, effectiveAt, previousHash, policy, index, rootChainHash ?? new byte[32]);
    }

    private static byte[] BuildSignatureData(
        IPolicyRule policy,
        DateTime effectiveAt,
        byte[] previousHash,
        int index,
        byte[] rootChainHash)
    {
        var json = JsonConvert.SerializeObject(new
        {
            PolicyType = policy.GetType().AssemblyQualifiedName,
            PolicyName = policy.Name,
            PolicyDescription = policy.Description,
            PolicyPriority = policy.Priority,
            EffectiveAt = effectiveAt.ToString("O"),
            PreviousHash = Convert.ToBase64String(previousHash),
            Index = index,
            RootChainHash = Convert.ToBase64String(rootChainHash)
        });
        return Encoding.UTF8.GetBytes(json);
    }
}
