# Security Policy Engine

AcornDB v0.6.0 introduces a **Security Policy Engine** with cryptographic verification. This guide covers the governance ledger, hash chains, and integration with the existing policy infrastructure.

---

## Overview

The Security Policy Engine provides:

- **Append-only PolicyLog** - Tamper-evident policy storage
- **Hash-chained entries** - Each entry links to the previous via SHA-256
- **Cryptographic signatures** - Detect any modification to historical policies
- **Time-based queries** - Find which policy was active at any timestamp
- **Thread-safe operations** - Safe for concurrent access

---

## Quick Start

```csharp
using AcornDB.Policy.Governance;
using AcornDB.Security;

// Create a signer (SHA-256 based)
var signer = new Sha256PolicySigner();

// Create an in-memory policy log
var log = new MemoryPolicyLog(signer);

// Append policies to the chain (use any IPolicyRule implementation)
log.Append(myCustomPolicyRule, DateTime.UtcNow);
log.Append(anotherPolicyRule, DateTime.UtcNow.AddHours(1));

// Verify chain integrity
var result = log.VerifyChain();
Console.WriteLine(result.IsValid ? "Chain intact" : $"Broken at {result.BrokenAtIndex}");

// Query active policy at a specific time
var policy = log.GetPolicyAt(DateTime.UtcNow);
```

---

## Core Concepts

### PolicySeal

A `PolicySeal` is an immutable, cryptographically sealed policy entry:

| Property | Description |
|----------|-------------|
| `Signature` | SHA-256 hash of content + timestamp + index |
| `PreviousHash` | Hash of previous entry (creates chain) |
| `EffectiveAt` | When the policy became active |
| `Policy` | The actual `IPolicyRule` |
| `Index` | Sequential position in chain (0-based) |

### Hash Chain Structure

```
Genesis (Index 0):
  PreviousHash = 0x000...000 (32 zero bytes)
  Signature = SHA256(Policy + EffectiveAt + Index)

Entry N:
  PreviousHash = Hash(Entry N-1)
  Signature = SHA256(Policy + EffectiveAt + Index)
```

Any modification to an entry breaks all subsequent hashes.

### IPolicySigner

The `IPolicySigner` interface abstracts cryptographic operations:

```csharp
public interface IPolicySigner
{
    byte[] Sign(byte[] data);
    bool Verify(byte[] data, byte[] signature);
    string Algorithm { get; }
}
```

Built-in implementations:
- `Sha256PolicySigner` - SHA-256 hash-based (no keys required)
- `Ed25519PolicySigner` - Ed25519 signatures (requires key pair)

---

## Storage Options

### MemoryPolicyLog

In-memory storage, ideal for testing and short-lived applications:

```csharp
var log = new MemoryPolicyLog(new Sha256PolicySigner());
```

### FilePolicyLog

File-based storage with crash recovery:

```csharp
// Basic usage
var log = new FilePolicyLog("./data/governance.log", new Sha256PolicySigner());

// With optional metrics collector
var metrics = new PolicyLogMetrics();
var log = new FilePolicyLog("./data/governance.log", new Sha256PolicySigner(), metrics);
```

File format: JSON Lines (one JSON object per line).

---

## Integration with LocalPolicyEngine

The `LocalPolicyEngine` can load policies from a `PolicyLog`:

```csharp
var signer = new Sha256PolicySigner();
var log = new MemoryPolicyLog(signer);
log.Append(myPolicyRule, DateTime.UtcNow);

var options = new LocalPolicyEngineOptions();
var engine = new LocalPolicyEngine(options, policyLog: log);

// Policies are loaded and chain-verified automatically
```

---

## Integration with Root Pipeline

The `PolicyEnforcementRoot` verifies chain integrity during operations:

```csharp
var log = new MemoryPolicyLog(signer);
var engine = new LocalPolicyEngine(options, log);

var root = new PolicyEnforcementRoot(engine, policyLog: log);
// Chain verified on first Stash/Crack operation
```

---

## Chain Validation

### Verify Entire Chain

```csharp
var result = log.VerifyChain();

if (!result.IsValid)
{
    Console.WriteLine($"Chain broken at index {result.BrokenAtIndex}");
    Console.WriteLine($"Details: {result.Details}");
}
```

### Validation Caching

Chain validation is cached after the first call. The cache invalidates automatically when new entries are appended.

---

## Merkle Trees (Advanced)

For large policy logs, use `MerkleTree` for efficient proofs:

```csharp
var tree = MerkleTree.Build(log.GetAllSeals().Select(s => s.Signature).ToList());

// Generate proof for a specific entry
var proof = tree.GenerateProof(entryIndex);

// Verify proof without full chain
var isValid = tree.VerifyProof(proof, entrySignature, tree.RootHash);
```

---

## Thread Safety

Both `MemoryPolicyLog` and `FilePolicyLog` are thread-safe:

- Multiple readers allowed concurrently
- Single writer with exclusive lock
- Uses `ReaderWriterLockSlim` internally

---

## Performance Considerations

| Operation | Time Complexity |
|-----------|-----------------|
| `Append` | O(1) |
| `GetPolicyAt` | O(log n) binary search |
| `VerifyChain` | O(n) first call, O(1) cached |
| `GetAllSeals` | O(n) |

### Metrics

Track performance with `PolicyLogMetrics`:

```csharp
var metrics = log.GetMetrics();
Console.WriteLine($"Append time: {metrics.LastAppendMs}ms");
Console.WriteLine($"Validation time: {metrics.LastValidationMs}ms");
```

---

## Security Considerations

### Tamper Detection

The hash chain guarantees:
- Modifying any entry breaks all subsequent hashes
- Inserting entries is impossible without breaking the chain
- Removing entries is detectable via missing links

### Timing Attack Prevention

`Sha256PolicySigner.Verify()` uses `CryptographicOperations.FixedTimeEquals()` for constant-time comparison.

### Key Management (Ed25519)

For `Ed25519PolicySigner`:
- Keys are injected via constructor
- Never store keys in code or config files
- Use secure key storage (Azure Key Vault, AWS KMS, etc.)

---

## Error Handling

| Exception | When Thrown |
|-----------|-------------|
| `ChainIntegrityException` | Hash chain verification fails |
| `ArgumentException` | Invalid policy or timestamp |
| `InvalidOperationException` | Append with out-of-order timestamp |

---

## API Reference

### IPolicyLog

```csharp
public interface IPolicyLog
{
    PolicySeal Append(IPolicyRule policy, DateTime effectiveAt);
    IPolicyRule? GetPolicyAt(DateTime timestamp);
    IReadOnlyList<PolicySeal> GetAllSeals();
    ChainValidationResult VerifyChain();
    int Count { get; }
}
```

### PolicySeal

```csharp
public sealed record PolicySeal
{
    public required byte[] Signature { get; init; }
    public required DateTime EffectiveAt { get; init; }
    public required byte[] PreviousHash { get; init; }
    public required IPolicyRule Policy { get; init; }
    public required int Index { get; init; }
}
```

### ChainValidationResult

```csharp
public sealed record ChainValidationResult
{
    public bool IsValid { get; init; }
    public int? BrokenAtIndex { get; init; }
    public string? Details { get; init; }
}
```

---

## Navigation

- **Previous:** [[Concepts]] - Core AcornDB terminology
- **Related:** [[Storage]] - Trunk implementations
- **Advanced:** [[Conflict Resolution]] - Handling sync conflicts
