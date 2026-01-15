using System;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Policy.Governance;
using Xunit;

namespace AcornDB.Test.Policy;

/// <summary>
/// Unit tests for <see cref="PolicyLogMetrics"/>.
/// </summary>
public class PolicyLogMetricsTests
{
    [Fact]
    public void NewMetrics_AllZero()
    {
        var metrics = new PolicyLogMetrics();

        Assert.Equal(0, metrics.ChainValidationCount);
        Assert.Equal(0, metrics.ChainValidationCacheHits);
        Assert.Equal(0, metrics.ChainValidationCacheMisses);
        Assert.Equal(0, metrics.PolicyLookupCount);
        Assert.Equal(0, metrics.AppendCount);
        Assert.Equal(0, metrics.EvaluationCacheHits);
        Assert.Equal(0, metrics.EvaluationCacheMisses);
        Assert.Equal(0, metrics.TotalSeals);
    }

    [Fact]
    public void RecordChainValidation_IncrementsCounters()
    {
        var metrics = new PolicyLogMetrics();

        metrics.RecordChainValidation(10, wasCached: false);
        metrics.RecordChainValidation(5, wasCached: true);
        metrics.RecordChainValidation(15, wasCached: true);

        Assert.Equal(3, metrics.ChainValidationCount);
        Assert.Equal(2, metrics.ChainValidationCacheHits);
        Assert.Equal(1, metrics.ChainValidationCacheMisses);
    }

    [Fact]
    public void ChainValidationAvgMs_CalculatesCorrectly()
    {
        var metrics = new PolicyLogMetrics();

        metrics.RecordChainValidation(10, wasCached: false);
        metrics.RecordChainValidation(20, wasCached: false);
        metrics.RecordChainValidation(30, wasCached: false);

        Assert.Equal(20.0, metrics.ChainValidationAvgMs);
    }

    [Fact]
    public void ChainValidationAvgMs_ReturnsZero_WhenNoValidations()
    {
        var metrics = new PolicyLogMetrics();

        Assert.Equal(0, metrics.ChainValidationAvgMs);
    }

    [Fact]
    public void ChainValidationCacheHitRate_CalculatesCorrectly()
    {
        var metrics = new PolicyLogMetrics();

        metrics.RecordChainValidation(10, wasCached: true);
        metrics.RecordChainValidation(10, wasCached: true);
        metrics.RecordChainValidation(10, wasCached: true);
        metrics.RecordChainValidation(10, wasCached: false);

        Assert.Equal(0.75, metrics.ChainValidationCacheHitRate);
    }

    [Fact]
    public void ChainValidationCacheHitRate_ReturnsZero_WhenNoData()
    {
        var metrics = new PolicyLogMetrics();

        Assert.Equal(0, metrics.ChainValidationCacheHitRate);
    }

    [Fact]
    public void RecordPolicyLookup_IncrementsCounters()
    {
        var metrics = new PolicyLogMetrics();

        metrics.RecordPolicyLookup(5);
        metrics.RecordPolicyLookup(10);

        Assert.Equal(2, metrics.PolicyLookupCount);
        Assert.Equal(7.5, metrics.PolicyLookupAvgMs);
    }

    [Fact]
    public void PolicyLookupAvgMs_ReturnsZero_WhenNoLookups()
    {
        var metrics = new PolicyLogMetrics();

        Assert.Equal(0, metrics.PolicyLookupAvgMs);
    }

    [Fact]
    public void RecordAppend_IncrementsCounters()
    {
        var metrics = new PolicyLogMetrics();

        metrics.RecordAppend(20);
        metrics.RecordAppend(30);
        metrics.RecordAppend(40);

        Assert.Equal(3, metrics.AppendCount);
        Assert.Equal(30.0, metrics.AppendAvgMs);
    }

    [Fact]
    public void AppendAvgMs_ReturnsZero_WhenNoAppends()
    {
        var metrics = new PolicyLogMetrics();

        Assert.Equal(0, metrics.AppendAvgMs);
    }

    [Fact]
    public void RecordEvaluationCache_IncrementsCounters()
    {
        var metrics = new PolicyLogMetrics();

        metrics.RecordEvaluationCache(wasHit: true);
        metrics.RecordEvaluationCache(wasHit: true);
        metrics.RecordEvaluationCache(wasHit: false);

        Assert.Equal(2, metrics.EvaluationCacheHits);
        Assert.Equal(1, metrics.EvaluationCacheMisses);
    }

    [Fact]
    public void EvaluationCacheHitRate_CalculatesCorrectly()
    {
        var metrics = new PolicyLogMetrics();

        metrics.RecordEvaluationCache(wasHit: true);
        metrics.RecordEvaluationCache(wasHit: false);

        Assert.Equal(0.5, metrics.EvaluationCacheHitRate);
    }

    [Fact]
    public void EvaluationCacheHitRate_ReturnsZero_WhenNoData()
    {
        var metrics = new PolicyLogMetrics();

        Assert.Equal(0, metrics.EvaluationCacheHitRate);
    }

    [Fact]
    public void SetTotalSeals_UpdatesValue()
    {
        var metrics = new PolicyLogMetrics();

        metrics.SetTotalSeals(42);

        Assert.Equal(42, metrics.TotalSeals);

        metrics.SetTotalSeals(100);

        Assert.Equal(100, metrics.TotalSeals);
    }

    [Fact]
    public void StartTimer_ReturnsRunningStopwatch()
    {
        var stopwatch = PolicyLogMetrics.StartTimer();

        Assert.True(stopwatch.IsRunning);
        Thread.Sleep(10);
        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds >= 0);
    }

    [Fact]
    public void Reset_ClearsAllMetrics()
    {
        var metrics = new PolicyLogMetrics();

        metrics.RecordChainValidation(10, wasCached: true);
        metrics.RecordPolicyLookup(5);
        metrics.RecordAppend(20);
        metrics.RecordEvaluationCache(wasHit: true);
        metrics.SetTotalSeals(50);

        metrics.Reset();

        Assert.Equal(0, metrics.ChainValidationCount);
        Assert.Equal(0, metrics.ChainValidationCacheHits);
        Assert.Equal(0, metrics.ChainValidationCacheMisses);
        Assert.Equal(0, metrics.PolicyLookupCount);
        Assert.Equal(0, metrics.AppendCount);
        Assert.Equal(0, metrics.EvaluationCacheHits);
        Assert.Equal(0, metrics.EvaluationCacheMisses);
        Assert.Equal(0, metrics.TotalSeals);
    }

    [Fact]
    public void Snapshot_CapturesCurrentState()
    {
        var metrics = new PolicyLogMetrics();

        metrics.RecordChainValidation(10, wasCached: true);
        metrics.RecordChainValidation(20, wasCached: false);
        metrics.RecordPolicyLookup(5);
        metrics.RecordAppend(15);
        metrics.RecordEvaluationCache(wasHit: true);
        metrics.SetTotalSeals(42);

        var snapshot = metrics.Snapshot();

        Assert.Equal(2, snapshot.ChainValidationCount);
        Assert.Equal(15.0, snapshot.ChainValidationAvgMs);
        Assert.Equal(1, snapshot.ChainValidationCacheHits);
        Assert.Equal(1, snapshot.ChainValidationCacheMisses);
        Assert.Equal(0.5, snapshot.ChainValidationCacheHitRate);
        Assert.Equal(1, snapshot.PolicyLookupCount);
        Assert.Equal(5.0, snapshot.PolicyLookupAvgMs);
        Assert.Equal(1, snapshot.AppendCount);
        Assert.Equal(15.0, snapshot.AppendAvgMs);
        Assert.Equal(1, snapshot.EvaluationCacheHits);
        Assert.Equal(0, snapshot.EvaluationCacheMisses);
        Assert.Equal(1.0, snapshot.EvaluationCacheHitRate);
        Assert.Equal(42, snapshot.TotalSeals);
    }

    [Fact]
    public void Snapshot_IsImmutable()
    {
        var metrics = new PolicyLogMetrics();
        metrics.RecordChainValidation(10, wasCached: false);

        var snapshot = metrics.Snapshot();

        metrics.RecordChainValidation(20, wasCached: false);
        metrics.RecordChainValidation(30, wasCached: false);

        // Snapshot should still show original values
        Assert.Equal(1, snapshot.ChainValidationCount);
        Assert.Equal(10.0, snapshot.ChainValidationAvgMs);

        // But metrics should be updated
        Assert.Equal(3, metrics.ChainValidationCount);
    }

    [Fact]
    public void ThreadSafety_ConcurrentRecords()
    {
        var metrics = new PolicyLogMetrics();
        const int iterations = 1000;
        const int threads = 10;

        Parallel.For(0, threads, _ =>
        {
            for (var i = 0; i < iterations; i++)
            {
                metrics.RecordChainValidation(1, wasCached: i % 2 == 0);
                metrics.RecordPolicyLookup(1);
                metrics.RecordAppend(1);
                metrics.RecordEvaluationCache(wasHit: i % 2 == 0);
            }
        });

        var expectedCount = threads * iterations;
        Assert.Equal(expectedCount, metrics.ChainValidationCount);
        Assert.Equal(expectedCount, metrics.PolicyLookupCount);
        Assert.Equal(expectedCount, metrics.AppendCount);
        Assert.Equal(expectedCount, metrics.EvaluationCacheHits + metrics.EvaluationCacheMisses);
    }

    [Fact]
    public void ThreadSafety_ConcurrentSetTotalSeals()
    {
        var metrics = new PolicyLogMetrics();
        const int iterations = 1000;

        Parallel.For(0, iterations, i =>
        {
            metrics.SetTotalSeals(i);
        });

        // Just verify it doesn't crash and has a valid value
        Assert.True(metrics.TotalSeals >= 0 && metrics.TotalSeals < iterations);
    }
}
