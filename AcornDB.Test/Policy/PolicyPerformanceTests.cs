using System;
using System.Diagnostics;
using AcornDB.Policy;
using AcornDB.Policy.Governance;
using AcornDB.Security;
using Xunit;
using Xunit.Abstractions;

namespace AcornDB.Test.Policy;

/// <summary>
/// Performance validation tests for Policy Governance components.
/// Verifies operations meet the targets defined in the v0.6.0 spec:
///   - Chain validation (10K entries): &lt;100ms p95
///   - Policy lookup: &lt;10ms p95
///   - Append operation: &lt;5ms p95
///   - Memory (10K policies): &lt;50MB
/// </summary>
public class PolicyPerformanceTests
{
    private readonly ITestOutputHelper _output;
    private readonly IPolicySigner _signer = new Sha256PolicySigner();

    public PolicyPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Append_MeetsPerformanceTarget()
    {
        // Target: <5ms per append
        const int warmupCount = 50;
        const int measureCount = 200;
        using var log = new MemoryPolicyLog(_signer);
        var baseTime = DateTime.UtcNow;

        // Warmup
        for (var i = 0; i < warmupCount; i++)
            log.Append(new PerfPolicy($"Warmup-{i}"), baseTime.AddMilliseconds(i));

        // Measure
        var timings = new double[measureCount];
        var sw = new Stopwatch();

        for (var i = 0; i < measureCount; i++)
        {
            sw.Restart();
            log.Append(new PerfPolicy($"Measure-{i}"), baseTime.AddMilliseconds(warmupCount + i));
            sw.Stop();
            timings[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(timings);
        var p50 = timings[measureCount / 2];
        var p95 = timings[(int)(measureCount * 0.95)];
        var p99 = timings[(int)(measureCount * 0.99)];
        var avg = Average(timings);

        _output.WriteLine($"Append Performance ({measureCount} ops, log size {warmupCount}+):");
        _output.WriteLine($"  Avg: {avg:F3}ms | P50: {p50:F3}ms | P95: {p95:F3}ms | P99: {p99:F3}ms");

        Assert.True(p95 < 5.0, $"Append P95 ({p95:F3}ms) exceeded 5ms target");
    }

    [Fact]
    public void GetPolicyAt_MeetsPerformanceTarget()
    {
        // Target: <10ms per lookup (binary search)
        const int policyCount = 10_000;
        const int measureCount = 500;
        using var log = new MemoryPolicyLog(_signer);
        var baseTime = DateTime.UtcNow;

        // Populate
        for (var i = 0; i < policyCount; i++)
            log.Append(new PerfPolicy($"Policy-{i}"), baseTime.AddMinutes(i));

        // Measure lookups at various points
        var timings = new double[measureCount];
        var sw = new Stopwatch();
        var rng = new Random(42);

        for (var i = 0; i < measureCount; i++)
        {
            var queryTime = baseTime.AddMinutes(rng.Next(0, policyCount));
            sw.Restart();
            var result = log.GetPolicyAt(queryTime);
            sw.Stop();
            timings[i] = sw.Elapsed.TotalMilliseconds;
            Assert.NotNull(result);
        }

        Array.Sort(timings);
        var p50 = timings[measureCount / 2];
        var p95 = timings[(int)(measureCount * 0.95)];
        var p99 = timings[(int)(measureCount * 0.99)];
        var avg = Average(timings);

        _output.WriteLine($"GetPolicyAt Performance ({measureCount} lookups, {policyCount} entries):");
        _output.WriteLine($"  Avg: {avg:F3}ms | P50: {p50:F3}ms | P95: {p95:F3}ms | P99: {p99:F3}ms");

        Assert.True(p95 < 10.0, $"GetPolicyAt P95 ({p95:F3}ms) exceeded 10ms target");
    }

    [Fact]
    public void VerifyChain_MeetsPerformanceTarget_10K()
    {
        // Target: <100ms for 10K entries (first verification, uncached)
        const int policyCount = 10_000;
        const int measureCount = 5;
        using var log = new MemoryPolicyLog(_signer);
        var baseTime = DateTime.UtcNow;

        // Populate
        for (var i = 0; i < policyCount; i++)
            log.Append(new PerfPolicy($"Policy-{i}"), baseTime.AddMinutes(i));

        // Measure uncached chain verification
        // To avoid cache hits, we append a new entry between verifications
        var timings = new double[measureCount];
        var sw = new Stopwatch();

        for (var i = 0; i < measureCount; i++)
        {
            // Invalidate cache by appending
            log.Append(new PerfPolicy($"Invalidate-{i}"), baseTime.AddMinutes(policyCount + i));

            sw.Restart();
            var result = log.VerifyChain();
            sw.Stop();

            Assert.True(result.IsValid);
            timings[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(timings);
        var p95 = timings[(int)(measureCount * 0.95)];
        var avg = Average(timings);
        var max = timings[measureCount - 1];

        _output.WriteLine($"VerifyChain Performance ({policyCount}+ entries, uncached):");
        _output.WriteLine($"  Avg: {avg:F1}ms | P95: {p95:F1}ms | Max: {max:F1}ms");

        Assert.True(max < 100.0, $"VerifyChain max ({max:F1}ms) exceeded 100ms target for {policyCount} entries");
    }

    [Fact]
    public void VerifyChain_CachedIsFast()
    {
        // Cached verification should be near-instant
        const int policyCount = 10_000;
        using var log = new MemoryPolicyLog(_signer);
        var baseTime = DateTime.UtcNow;

        for (var i = 0; i < policyCount; i++)
            log.Append(new PerfPolicy($"Policy-{i}"), baseTime.AddMinutes(i));

        // First call populates cache
        log.VerifyChain();

        // Cached calls should be <1ms
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
            log.VerifyChain();
        sw.Stop();

        var avgCachedMs = sw.Elapsed.TotalMilliseconds / 1000;

        _output.WriteLine($"VerifyChain Cached ({policyCount} entries, 1000 calls):");
        _output.WriteLine($"  Avg per call: {avgCachedMs:F4}ms");

        Assert.True(avgCachedMs < 1.0, $"Cached VerifyChain ({avgCachedMs:F4}ms) exceeded 1ms");
    }

    [Fact]
    public void Memory_10KPolicies_UnderTarget()
    {
        // Target: <50MB for 10K policies
        const int policyCount = 10_000;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memBefore = GC.GetTotalMemory(true);

        using var log = new MemoryPolicyLog(_signer);
        var baseTime = DateTime.UtcNow;

        for (var i = 0; i < policyCount; i++)
            log.Append(new PerfPolicy($"Policy-{i}"), baseTime.AddMinutes(i));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memAfter = GC.GetTotalMemory(true);

        var usedMb = (memAfter - memBefore) / (1024.0 * 1024.0);

        _output.WriteLine($"Memory Usage ({policyCount} policies):");
        _output.WriteLine($"  Before: {memBefore / (1024.0 * 1024.0):F2} MB");
        _output.WriteLine($"  After:  {memAfter / (1024.0 * 1024.0):F2} MB");
        _output.WriteLine($"  Delta:  {usedMb:F2} MB");

        Assert.True(usedMb < 50.0, $"Memory usage ({usedMb:F2} MB) exceeded 50MB target for {policyCount} policies");
    }

    [Fact]
    public void MerkleProof_MeetsPerformanceTarget()
    {
        // Merkle proof verification should be O(log n)
        const int leafCount = 10_000;
        const int measureCount = 100;
        var tree = new MerkleTree();

        var data = new byte[64];
        for (var i = 0; i < leafCount; i++)
        {
            BitConverter.TryWriteBytes(data, i);
            tree.AddLeaf(data);
        }

        var timings = new double[measureCount];
        var sw = new Stopwatch();
        var rng = new Random(42);

        for (var i = 0; i < measureCount; i++)
        {
            var leafIndex = rng.Next(0, leafCount);
            sw.Restart();
            var proof = tree.GenerateProof(leafIndex);
            var valid = proof.Verify();
            sw.Stop();

            Assert.True(valid);
            timings[i] = sw.Elapsed.TotalMilliseconds;
        }

        Array.Sort(timings);
        var p50 = timings[measureCount / 2];
        var p95 = timings[(int)(measureCount * 0.95)];
        var avg = Average(timings);

        _output.WriteLine($"MerkleProof Generate+Verify ({leafCount} leaves, {measureCount} proofs):");
        _output.WriteLine($"  Avg: {avg:F3}ms | P50: {p50:F3}ms | P95: {p95:F3}ms");

        Assert.True(p95 < 10.0, $"MerkleProof P95 ({p95:F3}ms) exceeded 10ms target");
    }

    private static double Average(double[] values)
    {
        var sum = 0.0;
        for (var i = 0; i < values.Length; i++)
            sum += values[i];
        return sum / values.Length;
    }

    private sealed class PerfPolicy : IPolicyRule
    {
        public PerfPolicy(string name) => Name = name;
        public string Name { get; }
        public string Description => "Performance test policy";
        public int Priority => 50;
        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
            => PolicyEvaluationResult.Success();
    }
}
