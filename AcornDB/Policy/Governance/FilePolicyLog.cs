using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AcornDB.Logging;
using AcornDB.Security;
using Newtonsoft.Json;

namespace AcornDB.Policy.Governance;

/// <summary>
/// File-based append-only policy log with crash recovery.
/// Format: One JSON line per PolicySeal (JSONL format).
/// </summary>
public sealed class FilePolicyLog : IPolicyLog, IDisposable
{
    private readonly string _filePath;
    private readonly IPolicySigner _signer;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly List<PolicySeal> _seals = new();
    private readonly JsonSerializerSettings _jsonSettings;
    private ChainValidationResult? _cachedValidation;
    private bool _disposed;

    /// <summary>
    /// Creates a FilePolicyLog with the specified file path and signer.
    /// </summary>
    /// <param name="filePath">Path to the policy log file.</param>
    /// <param name="signer">Cryptographic signer for chain integrity.</param>
    public FilePolicyLog(string filePath, IPolicySigner signer)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        _signer = signer ?? throw new ArgumentException("Signer cannot be null.", nameof(signer));

        _filePath = filePath;
        _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore
        };

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        LoadFromFile();
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _seals.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    /// <inheritdoc />
    public PolicySeal Append(IPolicyRule policy, DateTime effectiveAt)
    {
        _lock.EnterWriteLock();
        try
        {
            var previous = _seals.Count > 0 ? _seals[^1] : null;
            var seal = PolicySeal.Create(policy, effectiveAt, previous, _signer);

            PersistSeal(seal);
            _seals.Add(seal);
            _cachedValidation = null;
            return seal;
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public IReadOnlyList<PolicySeal> GetAllSeals()
    {
        _lock.EnterReadLock();
        try { return _seals.ToList().AsReadOnly(); }
        finally { _lock.ExitReadLock(); }
    }

    /// <inheritdoc />
    public ChainValidationResult VerifyChain()
    {
        _lock.EnterUpgradeableReadLock();
        try
        {
            var cached = _cachedValidation;
            if (cached is not null)
                return cached;

            var result = ValidateChainInternal();

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

            if (seal.Index != i)
                return ChainValidationResult.Invalid(i, "Index mismatch");

            var expectedPrevHash = i == 0 ? new byte[32] : _seals[i - 1].Signature;
            if (!seal.PreviousHashMatches(expectedPrevHash))
                return ChainValidationResult.Invalid(i, "PreviousHash mismatch");

            if (!seal.VerifySignature(_signer))
                return ChainValidationResult.Invalid(i, "Signature verification failed");
        }

        return ChainValidationResult.Valid();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
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

    private void PersistSeal(PolicySeal seal)
    {
        var entry = new PolicySealEntry
        {
            Signature = Convert.ToBase64String(seal.Signature),
            EffectiveAt = seal.EffectiveAt,
            PreviousHash = Convert.ToBase64String(seal.PreviousHash),
            Index = seal.Index,
            Policy = seal.Policy
        };

        var json = JsonConvert.SerializeObject(entry, _jsonSettings);
        using var stream = new FileStream(
            _filePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096);
        var bytes = Encoding.UTF8.GetBytes(json + Environment.NewLine);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    private void LoadFromFile()
    {
        if (!File.Exists(_filePath))
            return;

        var lines = File.ReadAllLines(_filePath);
        var validLines = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var entry = JsonConvert.DeserializeObject<PolicySealEntry>(line, _jsonSettings);
                if (entry?.Policy == null)
                {
                    AcornLog.Info($"Truncating policy log at line {_seals.Count}: null entry");
                    break;
                }

                var seal = PolicySeal.Reconstruct(
                    Convert.FromBase64String(entry.Signature),
                    entry.EffectiveAt,
                    Convert.FromBase64String(entry.PreviousHash),
                    entry.Policy,
                    entry.Index);

                // Validate chain integrity during load
                var expectedPrevHash = _seals.Count == 0 ? new byte[32] : _seals[^1].Signature;
                if (!seal.PreviousHashMatches(expectedPrevHash))
                {
                    AcornLog.Info($"Truncating policy log at index {seal.Index}: chain integrity broken");
                    break;
                }

                if (seal.Index != _seals.Count)
                {
                    AcornLog.Info($"Truncating policy log at index {seal.Index}: index mismatch");
                    break;
                }

                _seals.Add(seal);
                validLines.Add(line);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                AcornLog.Info($"Truncating policy log at line {_seals.Count}: {ex.Message}");
                break;
            }
        }

        // Rewrite file with only valid entries if truncation occurred
        if (validLines.Count < lines.Length)
        {
            var content = string.Join(Environment.NewLine, validLines);
            if (validLines.Count > 0)
                content += Environment.NewLine;
            File.WriteAllText(_filePath, content);
        }

        _cachedValidation = ChainValidationResult.Valid();
    }

    /// <summary>
    /// Internal DTO for JSON serialization of PolicySeal.
    /// </summary>
    private sealed class PolicySealEntry
    {
        public string Signature { get; set; } = string.Empty;
        public DateTime EffectiveAt { get; set; }
        public string PreviousHash { get; set; } = string.Empty;
        public int Index { get; set; }
        public IPolicyRule Policy { get; set; } = null!;
    }
}
