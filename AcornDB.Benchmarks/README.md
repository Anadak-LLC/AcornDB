# 🌰 AcornDB Performance Benchmarks

Performance benchmarking suite for AcornDB using [BenchmarkDotNet](https://benchmarkdotnet.org/).

## Overview

This project contains comprehensive benchmarks for measuring AcornDB's performance across different scenarios:

- **Basic Operations** - Throughput for Stash, Crack, and Toss operations
- **Memory Usage** - Memory consumption with different cache strategies
- **Sync Performance** - In-process synchronization performance
- **Conflict Resolution** - Squabble (conflict resolution) overhead
- **Competitive Benchmarks** - AcornDB vs LiteDB, SQLite (1K/10K/50K docs)
- **Delta Sync** - Incremental sync efficiency
- **Redis Comparison** - AcornDB vs Redis cache performance

## Running Benchmarks

### Run all benchmarks (default)
```bash
cd AcornDB.Benchmarks
dotnet run -c Release
```

### Run specific benchmark suite
```bash
dotnet run -c Release basic        # Basic operations
dotnet run -c Release memory       # Memory usage
dotnet run -c Release sync         # Sync performance
dotnet run -c Release conflict     # Conflict resolution
dotnet run -c Release competitive  # vs LiteDB, SQLite
dotnet run -c Release delta        # Delta sync
dotnet run -c Release redis        # vs Redis cache
```

### Get help
```bash
dotnet run -c Release --help
```

## Benchmark Suites

### 1. BasicOperationsBenchmarks

Measures throughput for core Tree operations:

- `Stash_MemoryTrunk_1000Items` - Write performance with MemoryTrunk
- `Stash_FileTrunk_1000Items` - Write performance with FileTrunk (disk I/O)
- `Crack_MemoryTrunk_1000Items` - Read performance
- `Toss_MemoryTrunk_1000Items` - Delete performance
- `StashAndCrack_Mixed_1000Operations` - Realistic mixed workload

**What it measures**: Operations per second, memory allocations

### 2. MemoryBenchmarks

Compares memory usage under different cache strategies:

- `MemoryUsage_LRU_Cache` - LRU cache with 10k item limit
- `MemoryUsage_Unlimited_Cache` - No eviction (baseline)
- `MemoryUsage_NoEviction_Strategy` - NoEvictionStrategy
- `LRU_EvictionPerformance_100k_Items` - Eviction overhead at scale

**What it measures**: Memory allocations, Gen0/Gen1/Gen2 collections, eviction performance

**Parameterized**: Tests with 10k, 50k, and 100k items

### 3. SyncBenchmarks

Measures synchronization performance:

- `InProcessSync_Push` - One-way sync performance
- `InProcessSync_Bidirectional` - Two-way sync
- `ExportChanges_Performance` - Change export overhead
- `SquabbleResolution_Performance` - Conflict resolution during sync

**What it measures**: Sync latency, throughput

**Parameterized**: Tests with 100, 500, and 1000 items

### 4. ConflictResolutionBenchmarks

Measures Squabble (conflict resolution) overhead:

- `Squabble_LocalWins` - Local version always wins
- `Squabble_IncomingWins` - Incoming version always wins
- `Squabble_MixedResults` - 50/50 split
- `Squabble_WithHistoryRetrieval` - Includes history lookups
- `NoConflict_DirectStash` - Baseline (no conflicts)

**What it measures**: Conflict resolution overhead vs. direct writes

**Parameterized**: Tests with 100, 500, and 1000 conflicts

### 5. CompetitiveBenchmarks

Compares AcornDB against embedded database alternatives:

- **vs SQLite** - File-based and in-memory modes
- **vs LiteDB** - .NET document database
- **Operations**: Insert, Read, Update, Delete, Scan, Mixed workloads

**What it measures**: Relative performance, memory efficiency

**Parameterized**: Tests with 1,000, 10,000, and 50,000 documents

### 6. RedisCacheBenchmarks

Direct cache-to-cache comparison with Redis:

- **Single Operations**: Insert, Read, Update, Delete
- **Mixed Workloads**: 70% read, 30% write
- **Access Patterns**: Hot spot (90/10), random, batch
- **Advanced**: Complex objects, TTL/expiration

**Prerequisites**:
- Redis server running on localhost:6379
- Quick setup: `./start-redis.sh` or `start-redis.cmd`
- Or use docker-compose: `docker-compose -f docker-compose.redis.yml up -d`

**What it measures**: In-process vs client-server overhead, serialization cost

**See**: [REDIS_BENCHMARK_GUIDE.md](REDIS_BENCHMARK_GUIDE.md) for detailed setup

**Parameterized**: Tests with 1,000 and 10,000 operations

## Understanding Results

BenchmarkDotNet produces detailed reports including:

- **Mean** - Average execution time
- **Error** - Half of 99.9% confidence interval
- **StdDev** - Standard deviation
- **Gen0/Gen1/Gen2** - Garbage collection counts
- **Allocated** - Total memory allocated

Results are saved to: `BenchmarkDotNet.Artifacts/results/`

## Expected Performance

Based on initial testing (exact numbers TBD):

| Operation | Expected Throughput |
|-----------|---------------------|
| Stash (Memory) | ~100k ops/sec |
| Crack (Memory) | ~500k ops/sec |
| Toss (Memory) | ~100k ops/sec |
| Stash (File) | ~10k ops/sec |

LRU cache eviction overhead: < 5% when under limit, ~10-15% during eviction

## Notes

- Always run in **Release mode** (`-c Release`) for accurate results
- Benchmarks may take several minutes to complete
- File-based benchmarks create temporary data in `./data/` directory
- BenchmarkDotNet automatically warms up, runs multiple iterations, and calculates statistics

## Adding New Benchmarks

1. Create a new class in this project
2. Add `[MemoryDiagnoser]` and job configuration attributes
3. Implement benchmark methods with `[Benchmark]` attribute
4. Add to `Program.cs` switch statement
5. Run and verify

Example:
```csharp
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class MyBenchmarks
{
    [Benchmark]
    public void MyOperation()
    {
        // Code to benchmark
    }
}
```

## References

- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/articles/overview.html)
- [Best Practices](https://benchmarkdotnet.org/articles/guides/good-practices.html)
- [Memory Diagnoser](https://benchmarkdotnet.org/articles/configs/diagnosers.html#memory-diagnoser)
