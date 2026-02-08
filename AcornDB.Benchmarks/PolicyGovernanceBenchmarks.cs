using BenchmarkDotNet.Attributes;
using AcornDB.Policy;
using AcornDB.Policy.Governance;
using AcornDB.Security;

namespace AcornDB.Benchmarks;

/// <summary>
/// Benchmarks for Policy Governance components: PolicyLog, MerkleTree, PolicySigner
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class PolicyGovernanceBenchmarks
{
    private Sha256PolicySigner _sha256Signer = null!;
    private Ed25519PolicySigner _ed25519Signer = null!;
    private MemoryPolicyLog _policyLog = null!;
    private MerkleTree _merkleTree = null!;
    private byte[] _testData = null!;
    private byte[] _sha256Signature = null!;
    private byte[] _ed25519Signature = null!;

    private static readonly byte[] TestPrivateKey = new byte[32]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
    };

    [Params(100, 1000, 10000)]
    public int PolicyCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _sha256Signer = new Sha256PolicySigner();
        _ed25519Signer = new Ed25519PolicySigner(TestPrivateKey);
        _policyLog = new MemoryPolicyLog(_sha256Signer);
        _merkleTree = new MerkleTree();
        _testData = new byte[256];
        Random.Shared.NextBytes(_testData);
        _sha256Signature = _sha256Signer.Sign(_testData);
        _ed25519Signature = _ed25519Signer.Sign(_testData);

        // Pre-populate policy log and merkle tree
        var baseTime = DateTime.UtcNow;
        for (var i = 0; i < PolicyCount; i++)
        {
            var policy = new BenchmarkPolicy($"Policy-{i}");
            _policyLog.Append(policy, baseTime.AddMinutes(i));
            _merkleTree.AddLeaf(_testData);
        }
    }

    // === Signer Benchmarks ===

    [Benchmark]
    public byte[] SHA256_Sign() => _sha256Signer.Sign(_testData);

    [Benchmark]
    public bool SHA256_Verify() => _sha256Signer.Verify(_testData, _sha256Signature);

    [Benchmark]
    public byte[] Ed25519_Sign() => _ed25519Signer.Sign(_testData);

    [Benchmark]
    public bool Ed25519_Verify() => _ed25519Signer.Verify(_testData, _ed25519Signature);

    // === PolicyLog Benchmarks ===

    [Benchmark]
    public PolicySeal PolicyLog_Append()
    {
        var policy = new BenchmarkPolicy("NewPolicy");
        return _policyLog.Append(policy, DateTime.UtcNow.AddYears(1));
    }

    [Benchmark]
    public IPolicyRule? PolicyLog_GetPolicyAt()
    {
        // Query for policy in the middle of the log
        var midTime = DateTime.UtcNow.AddMinutes(PolicyCount / 2);
        return _policyLog.GetPolicyAt(midTime);
    }

    [Benchmark]
    public ChainValidationResult PolicyLog_VerifyChain_Cached()
    {
        // Second call should hit cache
        _policyLog.VerifyChain();
        return _policyLog.VerifyChain();
    }

    // === MerkleTree Benchmarks ===

    [Benchmark]
    public int MerkleTree_AddLeaf() => _merkleTree.AddLeaf(_testData);

    [Benchmark]
    public byte[]? MerkleTree_GetRootHash() => _merkleTree.RootHash;

    [Benchmark]
    public MerkleProof MerkleTree_GenerateProof()
    {
        // Generate proof for middle leaf
        return _merkleTree.GenerateProof(PolicyCount / 2);
    }

    [Benchmark]
    public bool MerkleProof_Verify()
    {
        var proof = _merkleTree.GenerateProof(PolicyCount / 2);
        return proof.Verify();
    }

    /// <summary>
    /// Simple policy implementation for benchmarking.
    /// </summary>
    private sealed class BenchmarkPolicy : IPolicyRule
    {
        public string Name { get; }
        public string Description => "Benchmark policy";
        public int Priority => 50;

        public BenchmarkPolicy(string name) => Name = name;

        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
            => PolicyEvaluationResult.Success();
    }
}
