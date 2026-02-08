# 🔐 Policy Governance — Seal Your Rules, Guard Your Grove

The **Governance Ledger** is AcornDB's tamper-proof record book. Every policy rule you add gets **sealed** with a cryptographic stamp and chained to the one before it — like tree rings that can't be faked.

- **Seal** policy rules into an append-only ledger
- **Verify** the chain hasn't been tampered with
- **Query** which policy was active at any point in time
- **Prove** a single entry is legit without checking the whole chain

---

## 🎯 Why Governance?

### Without Governance (Trust-Based)
```csharp
// Anyone can register or unregister policies — no audit trail
var engine = new LocalPolicyEngine();
engine.RegisterPolicy(new MaxSizePolicy(1024));

// Did someone quietly remove this policy last Tuesday? Who knows.
engine.UnregisterPolicy("MaxSize");
// No record. No proof. Just gone.
```

### With Governance (Sealed)
```csharp
// Every policy gets sealed into a tamper-proof chain
var signer = new Sha256PolicySigner();
var log = new MemoryPolicyLog(signer);
var engine = new GovernedPolicyEngine(new LocalPolicyEngine(), log, signer);

engine.AppendPolicy(new MaxSizePolicy(1024), DateTime.UtcNow);
// ✅ Sealed: Index 0, linked to genesis

// Try to verify the chain at any time
var result = engine.VerifyChain();
// ✅ Chain intact: every seal checks out

// What policy was active last Tuesday at 3pm?
var policy = log.GetPolicyAt(lastTuesday);
```

---

## 🚀 Quick Start

### 1. Seal Your First Policy

```csharp
using AcornDB.Policy;
using AcornDB.Policy.Governance;
using AcornDB.Security;

// The stamp maker — SHA-256 needs no keys
var signer = new Sha256PolicySigner();

// The ledger — keeps sealed entries in memory
var log = new MemoryPolicyLog(signer);

// Wrap your engine with governance
var baseEngine = new LocalPolicyEngine();
var governed = new GovernedPolicyEngine(baseEngine, log, signer);

// Seal a policy into the ledger
governed.AppendPolicy(new MaxSizePolicy(1024), DateTime.UtcNow);

Console.WriteLine($"Sealed policies: {log.Count}");
// Output: Sealed policies: 1
```

### 2. Verify the Chain

```csharp
// Add a few more policies
governed.AppendPolicy(new TtlPolicy(TimeSpan.FromDays(30)), DateTime.UtcNow);
governed.AppendPolicy(new ReadOnlyPolicy(), DateTime.UtcNow);

// Check that nobody tampered with the ledger
var result = governed.VerifyChain();

if (result.IsValid)
    Console.WriteLine("Chain intact — all seals verified");
else
    Console.WriteLine($"Broken at seal #{result.BrokenAtIndex}: {result.Details}");
```

### 3. Time-Travel Through Policies

```csharp
// Which policy was active at a specific moment?
var activePolicy = log.GetPolicyAt(DateTime.UtcNow.AddHours(-2));

if (activePolicy != null)
    Console.WriteLine($"Active policy: {activePolicy.Name}");
else
    Console.WriteLine("No policy was active at that time");
```

---

## 🌲 How the Chain Works

Think of each sealed policy as a **tree ring**. Each ring contains the signature of the ring before it. Change any ring, and every ring after it cracks:

```
Ring 0 (Genesis):
  Previous = [all zeros]  ← no ring before me
  Stamp    = SHA256(policy + timestamp + index)

Ring 1:
  Previous = Stamp(Ring 0)  ← linked to the ring before
  Stamp    = SHA256(policy + timestamp + index + previous)

Ring 2:
  Previous = Stamp(Ring 1)  ← and so on...
  Stamp    = SHA256(policy + timestamp + index + previous)
```

Modify Ring 1? Ring 2's `Previous` won't match anymore. The chain breaks. `VerifyChain()` catches it immediately.

---

## 📦 Ledger Storage

### MemoryPolicyLog — Fast, Ephemeral

Perfect for testing and short-lived apps. Seals live in memory only:

```csharp
var log = new MemoryPolicyLog(signer);
// Gone when your app stops
```

### FilePolicyLog — Durable, Crash-Safe

Seals are written to a JSONL file (one JSON entry per line). If your app crashes mid-write, the ledger recovers automatically:

```csharp
var log = new FilePolicyLog("./data/governance.log", signer);
// Survives restarts, crashes, and power outages
```

---

## 🔧 Stamp Makers (Signers)

### Sha256PolicySigner — Simple, No Keys

Hash-based stamping. No keys to manage. Perfect for detecting tampering within your own system:

```csharp
var signer = new Sha256PolicySigner();
// ✅ No setup, no key files, just works
```

### Ed25519PolicySigner — Cryptographic Signatures

Asymmetric signatures for when you need to prove *who* sealed a policy. Only the private key holder can create stamps, but anyone with the public key can verify:

```csharp
// Signing (private key holder)
var signer = new Ed25519PolicySigner(privateKeyBytes);

// Verification only (public key holder)
var verifier = new Ed25519PolicySigner(publicKeyBytes, verifyOnly: true);
```

> **Key management:** Never hardcode keys. Inject them from secure storage (Azure Key Vault, AWS KMS, environment variables).

---

## 🌳 Integration with the Root Pipeline

The `PolicyEnforcementRoot` sits in the Root pipeline and enforces policies on every Stash and Crack:

```csharp
var signer = new Sha256PolicySigner();
var log = new MemoryPolicyLog(signer);
var baseEngine = new LocalPolicyEngine();
var governed = new GovernedPolicyEngine(baseEngine, log, signer);

// The root enforces policies during Stash/Crack operations
var root = new PolicyEnforcementRoot(governed);

// Add roots to your tree pipeline
var tree = new Acorn<User>()
    .WithRoot(root)
    .Sprout();

// Now every Stash and Crack passes through policy enforcement
tree.Stash(new User { Name = "Alice" });
// ✅ Policies evaluated, access validated
```

---

## 🔍 Tag-Based Access Control

AcornDB uses **tags** for access control — like labels on your nuts that say who can touch them:

```csharp
var engine = new LocalPolicyEngine();

// Grant "admin" role access to nuts tagged "sensitive"
engine.GrantTagAccess("sensitive", "admin");
engine.GrantTagAccess("public", "guest");

// Check access
var sensitiveNut = new TaggedDocument { Tags = new[] { "sensitive" } };

engine.ValidateAccess(sensitiveNut, "admin");  // ✅ Allowed
engine.ValidateAccess(sensitiveNut, "guest");  // ❌ Denied
```

Wrap it with `GovernedPolicyEngine` and every access rule gets sealed into the ledger too.

---

## 🌿 Merkle Proofs (Advanced)

Got a huge ledger? Merkle proofs let you verify a single seal in O(log n) time — like proving one leaf belongs to a tree without examining every branch:

```csharp
// Build a Merkle tree from all seals
var seals = log.GetAllSeals();
var merkle = MerkleTree.FromSeals(seals);

// Generate a proof for seal #42
var proof = merkle.GenerateProof(42);

// Verify it — only needs O(log n) hashes, not the full chain
var isLegit = proof.Verify();
// ✅ Seal #42 is part of the tree
```

### When to use Merkle proofs:
- **Large ledgers** (1,000+ sealed policies)
- **Auditors** who need to verify specific entries
- **Distributed systems** where you can't share the whole chain

---

## 🧪 Testing with Governance

```csharp
[Fact]
public void Policies_AreSealedAndVerifiable()
{
    var signer = new Sha256PolicySigner();
    using var log = new MemoryPolicyLog(signer);
    var governed = new GovernedPolicyEngine(
        new LocalPolicyEngine(), log, signer);

    governed.AppendPolicy(new TestPolicy("Rule1"), DateTime.UtcNow);
    governed.AppendPolicy(new TestPolicy("Rule2"), DateTime.UtcNow);

    var result = governed.VerifyChain();
    Assert.True(result.IsValid);
    Assert.Equal(2, log.Count);
}
```

---

## ⚡ Performance

| Operation | Speed | Measured (10K policies) |
|-----------|-------|------------------------|
| Seal a policy | O(1) | ~0.003ms P95 |
| Find active policy at timestamp | O(log n) binary search | ~0.001ms P95 |
| Verify full chain | O(n) first time, O(1) cached | ~40ms uncached, ~0ms cached |
| Merkle proof generation + verification | O(log n) | ~0.004ms P95 |
| Memory footprint | — | ~6 MB for 10K seals |

### Observability

Track ledger performance with `PolicyLogMetrics`:

```csharp
var metrics = new PolicyLogMetrics();
var log = new MemoryPolicyLog(signer, metrics);

// ... seal policies, verify chains ...

Console.WriteLine($"Avg seal time: {metrics.AppendAvgMs}ms");
Console.WriteLine($"Cache hit rate: {metrics.ChainValidationCacheHitRate:P0}");
```

---

## 🛡️ Security Notes

- **Tamper detection:** Modify any seal and every seal after it breaks. `VerifyChain()` catches it.
- **Timing-safe comparison:** Signature verification uses constant-time comparison to prevent timing attacks.
- **No hardcoded keys:** Ed25519 keys are always injected, never stored in code.
- **Thread-safe:** Both ledger types use `ReaderWriterLockSlim` — many readers, one writer.

---

## 📖 Summary

The Governance Ledger provides:

- **Sealed policies** — every rule gets a tamper-proof stamp
- **Chain integrity** — each seal links to the previous, like tree rings
- **Time-travel queries** — find which policy was active at any moment
- **Merkle proofs** — verify individual seals in O(log n) time
- **Two stamp makers** — SHA-256 (simple) and Ed25519 (signatures)
- **Two ledger types** — Memory (testing) and File (production)
- **Root pipeline integration** — enforce policies on every Stash and Crack

---

**Keep your grove honest.** 🔐🌳
