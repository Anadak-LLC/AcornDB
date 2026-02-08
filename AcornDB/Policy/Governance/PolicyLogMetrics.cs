using System;
using System.Diagnostics;
using System.Threading;

namespace AcornDB.Policy.Governance;

/// <summary>
/// Collects and exposes metrics for PolicyLog operations.
/// Thread-safe using atomic operations.
/// </summary>
/// <remarks>
/// Tracked metrics:
/// - Chain validation duration (ms)
/// - Policy lookup duration (ms)
/// - Total seals in log
/// - Chain validation cache hits/misses
/// - Evaluation cache hits/misses (if caching enabled)
/// </remarks>
public sealed class PolicyLogMetrics
{
    private long _chainValidationCount;
    private long _chainValidationTotalMs;
    private long _chainValidationCacheHits;
    private long _chainValidationCacheMisses;

    private long _policyLookupCount;
    private long _policyLookupTotalMs;

    private long _appendCount;
    private long _appendTotalMs;

    private long _evaluationCacheHits;
    private long _evaluationCacheMisses;

    private int _totalSeals;

    /// <summary>Total chain validations performed.</summary>
    public long ChainValidationCount => Interlocked.Read(ref _chainValidationCount);

    /// <summary>Average chain validation time in milliseconds.</summary>
    public double ChainValidationAvgMs => _chainValidationCount > 0
        ? (double)Interlocked.Read(ref _chainValidationTotalMs) / _chainValidationCount
        : 0;

    /// <summary>Chain validation cache hit count.</summary>
    public long ChainValidationCacheHits => Interlocked.Read(ref _chainValidationCacheHits);

    /// <summary>Chain validation cache miss count.</summary>
    public long ChainValidationCacheMisses => Interlocked.Read(ref _chainValidationCacheMisses);

    /// <summary>Chain validation cache hit rate (0-1).</summary>
    public double ChainValidationCacheHitRate
    {
        get
        {
            var total = _chainValidationCacheHits + _chainValidationCacheMisses;
            return total > 0 ? (double)_chainValidationCacheHits / total : 0;
        }
    }

    /// <summary>Total policy lookups performed.</summary>
    public long PolicyLookupCount => Interlocked.Read(ref _policyLookupCount);

    /// <summary>Average policy lookup time in milliseconds.</summary>
    public double PolicyLookupAvgMs => _policyLookupCount > 0
        ? (double)Interlocked.Read(ref _policyLookupTotalMs) / _policyLookupCount
        : 0;

    /// <summary>Total append operations performed.</summary>
    public long AppendCount => Interlocked.Read(ref _appendCount);

    /// <summary>Average append time in milliseconds.</summary>
    public double AppendAvgMs => _appendCount > 0
        ? (double)Interlocked.Read(ref _appendTotalMs) / _appendCount
        : 0;

    /// <summary>Evaluation cache hit count.</summary>
    public long EvaluationCacheHits => Interlocked.Read(ref _evaluationCacheHits);

    /// <summary>Evaluation cache miss count.</summary>
    public long EvaluationCacheMisses => Interlocked.Read(ref _evaluationCacheMisses);

    /// <summary>Evaluation cache hit rate (0-1).</summary>
    public double EvaluationCacheHitRate
    {
        get
        {
            var total = _evaluationCacheHits + _evaluationCacheMisses;
            return total > 0 ? (double)_evaluationCacheHits / total : 0;
        }
    }

    /// <summary>Current number of seals in the log.</summary>
    public int TotalSeals => Volatile.Read(ref _totalSeals);

    /// <summary>Records a chain validation operation.</summary>
    /// <param name="durationMs">Duration in milliseconds.</param>
    /// <param name="wasCached">True if result was from cache.</param>
    public void RecordChainValidation(long durationMs, bool wasCached)
    {
        Interlocked.Increment(ref _chainValidationCount);
        Interlocked.Add(ref _chainValidationTotalMs, durationMs);

        if (wasCached)
            Interlocked.Increment(ref _chainValidationCacheHits);
        else
            Interlocked.Increment(ref _chainValidationCacheMisses);
    }

    /// <summary>Records a policy lookup operation.</summary>
    /// <param name="durationMs">Duration in milliseconds.</param>
    public void RecordPolicyLookup(long durationMs)
    {
        Interlocked.Increment(ref _policyLookupCount);
        Interlocked.Add(ref _policyLookupTotalMs, durationMs);
    }

    /// <summary>Records an append operation.</summary>
    /// <param name="durationMs">Duration in milliseconds.</param>
    public void RecordAppend(long durationMs)
    {
        Interlocked.Increment(ref _appendCount);
        Interlocked.Add(ref _appendTotalMs, durationMs);
    }

    /// <summary>Records an evaluation cache result.</summary>
    /// <param name="wasHit">True if evaluation was cached.</param>
    public void RecordEvaluationCache(bool wasHit)
    {
        if (wasHit)
            Interlocked.Increment(ref _evaluationCacheHits);
        else
            Interlocked.Increment(ref _evaluationCacheMisses);
    }

    /// <summary>Updates the total seal count.</summary>
    /// <param name="count">New seal count.</param>
    public void SetTotalSeals(int count)
    {
        Volatile.Write(ref _totalSeals, count);
    }

    /// <summary>Creates a stopwatch for timing operations.</summary>
    /// <returns>A started Stopwatch instance.</returns>
    public static Stopwatch StartTimer() => Stopwatch.StartNew();

    /// <summary>Resets all metrics to zero.</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _chainValidationCount, 0);
        Interlocked.Exchange(ref _chainValidationTotalMs, 0);
        Interlocked.Exchange(ref _chainValidationCacheHits, 0);
        Interlocked.Exchange(ref _chainValidationCacheMisses, 0);
        Interlocked.Exchange(ref _policyLookupCount, 0);
        Interlocked.Exchange(ref _policyLookupTotalMs, 0);
        Interlocked.Exchange(ref _appendCount, 0);
        Interlocked.Exchange(ref _appendTotalMs, 0);
        Interlocked.Exchange(ref _evaluationCacheHits, 0);
        Interlocked.Exchange(ref _evaluationCacheMisses, 0);
        Volatile.Write(ref _totalSeals, 0);
    }

    /// <summary>Creates a snapshot of current metrics.</summary>
    /// <returns>A readonly snapshot of metrics values.</returns>
    public PolicyLogMetricsSnapshot Snapshot() => new(
        ChainValidationCount,
        ChainValidationAvgMs,
        ChainValidationCacheHits,
        ChainValidationCacheMisses,
        PolicyLookupCount,
        PolicyLookupAvgMs,
        AppendCount,
        AppendAvgMs,
        EvaluationCacheHits,
        EvaluationCacheMisses,
        TotalSeals
    );
}

/// <summary>
/// Immutable snapshot of PolicyLog metrics at a point in time.
/// </summary>
public sealed record PolicyLogMetricsSnapshot(
    long ChainValidationCount,
    double ChainValidationAvgMs,
    long ChainValidationCacheHits,
    long ChainValidationCacheMisses,
    long PolicyLookupCount,
    double PolicyLookupAvgMs,
    long AppendCount,
    double AppendAvgMs,
    long EvaluationCacheHits,
    long EvaluationCacheMisses,
    int TotalSeals
)
{
    /// <summary>Chain validation cache hit rate (0-1).</summary>
    public double ChainValidationCacheHitRate
    {
        get
        {
            var total = ChainValidationCacheHits + ChainValidationCacheMisses;
            return total > 0 ? (double)ChainValidationCacheHits / total : 0;
        }
    }

    /// <summary>Evaluation cache hit rate (0-1).</summary>
    public double EvaluationCacheHitRate
    {
        get
        {
            var total = EvaluationCacheHits + EvaluationCacheMisses;
            return total > 0 ? (double)EvaluationCacheHits / total : 0;
        }
    }
}
