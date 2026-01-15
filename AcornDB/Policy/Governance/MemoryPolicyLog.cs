using System;
using System.Collections.Generic;
using System.Threading;
using AcornDB.Security;

namespace AcornDB.Policy.Governance;

/// <summary>
/// In-memory implementation of IPolicyLog with thread-safe operations.
/// </summary>
public sealed class MemoryPolicyLog : IPolicyLog, IDisposable
{
    private readonly List<PolicySeal> _seals = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly IPolicySigner _signer;
    private ChainValidationResult? _cachedValidation;

    public MemoryPolicyLog(IPolicySigner signer)
    {
        _signer = signer ?? throw new ArgumentException("Signer cannot be null.", nameof(signer));
    }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _seals.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public PolicySeal Append(IPolicyRule policy, DateTime effectiveAt)
    {
        _lock.EnterWriteLock();
        try
        {
            var previous = _seals.Count > 0 ? _seals[^1] : null;
            var seal = PolicySeal.Create(policy, effectiveAt, previous, _signer);
            _seals.Add(seal);
            _cachedValidation = null; // Invalidate cache
            return seal;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public IPolicyRule? GetPolicyAt(DateTime timestamp)
    {
        _lock.EnterReadLock();
        try
        {
            if (_seals.Count == 0) return null;

            var index = BinarySearchPolicyAt(timestamp);
            return index >= 0 ? _seals[index].Policy : null;
        }
        finally { _lock.ExitReadLock(); }
    }

    private int BinarySearchPolicyAt(DateTime timestamp)
    {
        int left = 0, right = _seals.Count - 1, result = -1;
        while (left <= right)
        {
            var mid = left + (right - left) / 2;
            if (_seals[mid].EffectiveAt <= timestamp)
            {
                result = mid;
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }
        return result;
    }

    public IReadOnlyList<PolicySeal> GetAllSeals()
    {
        _lock.EnterReadLock();
        try
        {
            var copy = new List<PolicySeal>(_seals.Count);
            copy.AddRange(_seals);
            return copy.AsReadOnly();
        }
        finally { _lock.ExitReadLock(); }
    }

    public ChainValidationResult VerifyChain()
    {
        _lock.EnterUpgradeableReadLock();
        try
        {
            // Check cache first
            var cached = _cachedValidation;
            if (cached is not null)
                return cached;

            // Validate chain
            var result = ValidateChainInternal();

            // Cache result if valid (under write lock)
            if (result.IsValid)
            {
                _lock.EnterWriteLock();
                try { _cachedValidation = result; }
                finally { _lock.ExitWriteLock(); }
            }

            return result;
        }
        finally { _lock.ExitUpgradeableReadLock(); }
    }

    private ChainValidationResult ValidateChainInternal()
    {
        if (_seals.Count == 0)
            return ChainValidationResult.Valid();

        for (var i = 0; i < _seals.Count; i++)
        {
            var seal = _seals[i];

            // Verify index
            if (seal.Index != i)
                return ChainValidationResult.Invalid(i, "Index mismatch");

            // Verify previous hash linkage
            var expectedPrevHash = i == 0 ? new byte[32] : _seals[i - 1].Signature;
            if (!seal.PreviousHashMatches(expectedPrevHash))
                return ChainValidationResult.Invalid(i, "PreviousHash mismatch");

            // Verify signature
            if (!seal.VerifySignature(_signer))
                return ChainValidationResult.Invalid(i, "Signature verification failed");
        }

        return ChainValidationResult.Valid();
    }

    public void Dispose() => _lock.Dispose();
}
