# AcornDB Benchmark Coverage Report

## Overview

This document tracks which AcornDB trunk implementations have benchmark coverage and explains the testing strategy for each category of storage backend.

**Last Updated**: 2025-10-22

---

## Trunk Implementation Summary

### Core Trunks (Fully Tested ✅)

These trunks are part of the core AcornDB package and have comprehensive benchmark coverage:

| Trunk | Project | Status | Benchmark Coverage |
|-------|---------|--------|-------------------|
| **MemoryTrunk** | `AcornDB` | ✅ Fully Tested | All benchmark suites |
| **BTreeTrunk** | `AcornDB.Storage` | ✅ Fully Tested | All benchmark suites |
| **DocumentStoreTrunk** | `AcornDB.Storage` | ✅ Fully Tested | All benchmark suites |
| **FileTrunk** | `AcornDB.Storage` | ✅ Fully Tested | TrunkPerformanceBenchmarks |

**Why These Are Tested:**
- These are the primary storage backends users will choose from
- No external dependencies (databases, cloud services)
- Deterministic performance characteristics
- Can be tested in CI/CD without infrastructure

---

### Competitive Databases (Tested ✅)

These external databases are benchmarked for competitive analysis:

| Database | Type | Status | Benchmark Coverage |
|----------|------|--------|-------------------|
| **SQLite** | File-based RDBMS | ✅ Tested | CompetitiveBenchmarks |
| **SQLite :memory:** | In-memory RDBMS | ✅ Tested | CompetitiveBenchmarks |
| **LiteDB** | File-based NoSQL | ✅ Tested | CompetitiveBenchmarks |

**Why These Are Tested:**
- Industry-standard embedded databases
- Similar use cases (offline-first, embedded scenarios)
- No external server infrastructure required
- Critical for marketing and positioning

---

### Cloud Trunks (Untested ⚠️)

These trunks are implemented but **not benchmarked** due to external dependencies:

| Trunk | Project | Status | Reason Not Tested |
|-------|---------|--------|------------------|
| **AzureTrunk** | `AcornDB.Persistence.Cloud` | ⚠️ Not Benchmarked | Requires Azure Blob Storage account |
| **AzureTableTrunk** | `AcornDB.Persistence.Cloud` | ⚠️ Not Benchmarked | Requires Azure Table Storage account |
| **DynamoDbTrunk** | `AcornDB.Persistence.Cloud` | ⚠️ Not Benchmarked | Requires AWS account + DynamoDB setup |
| **CloudTrunk** | `AcornDB.Persistence.Cloud` | ⚠️ Not Benchmarked | Base abstraction (not directly used) |

**Why These Are NOT Tested:**

1. **Infrastructure Requirements**: Each trunk requires active cloud accounts with billing
2. **Environment Setup Complexity**: Credentials, connection strings, resource provisioning
3. **Cost Implications**: Running large-scale benchmarks (1M operations) could incur significant cloud storage costs
4. **Network Variability**: Performance highly dependent on network latency, region, throttling policies
5. **Non-Deterministic Results**: Cloud service performance varies by time of day, region load, etc.
6. **CI/CD Challenges**: Cannot run in standard CI/CD without credentials and active cloud subscriptions

**Testing Strategy for Cloud Trunks:**

- **Functional Tests Only**: `/AcornDB.Test/CapabilitiesTests.cs` validates basic functionality (when credentials available)
- **Manual Performance Testing**: Users test in their own cloud environment with their data patterns
- **Performance Guidance**: Document expected performance characteristics based on cloud provider specs:
  - Azure Blob Storage: ~100-300ms latency per operation (varies by region)
  - Azure Table Storage: ~50-200ms latency per operation
  - AWS S3/DynamoDB: ~100-500ms latency per operation (varies by region)

**Recommendation**: Users should benchmark cloud trunks in their own environment with representative data and workload patterns.

---

### RDBMS Trunks (Untested ⚠️)

These trunks are implemented but **not benchmarked** due to server dependencies:

| Trunk | Project | Status | Reason Not Tested |
|-------|---------|--------|------------------|
| **SqliteTrunk** | `AcornDB.Persistence.RDBMS` | ⚠️ Not Benchmarked | Requires ADO.NET setup (differs from competitive SQLite benchmark) |
| **MySqlTrunk** | `AcornDB.Persistence.RDBMS` | ⚠️ Not Benchmarked | Requires MySQL server installation |
| **PostgreSqlTrunk** | `AcornDB.Persistence.RDBMS` | ⚠️ Not Benchmarked | Requires PostgreSQL server installation |
| **SqlServerTrunk** | `AcornDB.Persistence.RDBMS` | ⚠️ Not Benchmarked | Requires SQL Server instance |

**Why These Are NOT Tested:**

1. **Server Infrastructure**: Each trunk requires a running database server
2. **Complex Setup**: Installation, configuration, schema creation, credentials
3. **Environment Variability**: Performance depends on server configuration, hardware, network
4. **CI/CD Complexity**: Requires Docker containers or external database services
5. **Different Use Case**: RDBMS trunks are used when integrating AcornDB with existing database infrastructure

**Testing Strategy for RDBMS Trunks:**

- **Manual Testing Only**: Users test against their own database servers
- **Integration Focus**: These trunks are for integration scenarios, not primary storage
- **Performance Guidance**: Document expected overhead:
  - Local Database: +10-50ms overhead vs BTreeTrunk (network + query execution)
  - Remote Database: +50-500ms overhead (network latency dominates)

**Recommendation**: RDBMS trunks should be tested by users in their specific environment with their database configuration.

---

### Data Lake Trunks (Untested ⚠️)

These specialized trunks are **not benchmarked**:

| Trunk | Project | Status | Reason Not Tested |
|-------|---------|--------|------------------|
| **ParquetTrunk** | `AcornDB.Persistence.DataLake` | ⚠️ Not Benchmarked | Specialized analytics use case |
| **TieredTrunk** | `AcornDB.Persistence.DataLake` | ⚠️ Not Benchmarked | Hybrid storage strategy (complex setup) |

**Why These Are NOT Tested:**

1. **Specialized Use Cases**: Not typical embedded database scenarios
2. **Complex Configuration**: Tiered storage requires multiple trunk configurations
3. **Different Performance Goals**: Parquet optimized for analytics, not CRUD operations
4. **Low Adoption Expected**: Advanced features for specific scenarios

**Testing Strategy for Data Lake Trunks:**

- **Future Work**: May add benchmarks if adoption increases
- **User-Driven**: Users should benchmark in their specific analytics workload

---

## Benchmark Suite Coverage Matrix

| Benchmark Suite | MemoryTrunk | BTreeTrunk | DocumentStoreTrunk | FileTrunk | Cloud Trunks | RDBMS Trunks | Data Lake |
|-----------------|-------------|------------|--------------------|-----------|--------------|--------------|-----------|
| **TrunkPerformanceBenchmarks** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| **RootPipelineBenchmarks** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **ConcurrencyBenchmarks** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **RealWorldWorkloadBenchmarks** | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **ScalabilityBenchmarks** | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **DurabilityBenchmarks** | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **QueryPerformanceBenchmarks** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **CacheEffectivenessBenchmarks** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **SyncProtocolBenchmarks** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **LifecycleBenchmarks** | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **CompetitiveBenchmarks** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |

---

## Why FileTrunk Has Limited Coverage

**FileTrunk** is only tested in `TrunkPerformanceBenchmarks` because:

1. **Performance Characteristics**: FileTrunk is the slowest option (50-100ms for 1K docs)
2. **Limited Use Cases**: Only useful for debugging/inspection scenarios
3. **Not Recommended for Production**: BTreeTrunk is superior in every measurable way
4. **Benchmark Focus**: TrunkPerformanceBenchmarks provides sufficient comparison data

---

## Testing Philosophy

### What We Test (Core Trunks)

**Criteria for Inclusion in Benchmark Suite:**
1. ✅ **No External Dependencies**: Can run on any developer machine
2. ✅ **Deterministic Performance**: Results are consistent across environments
3. ✅ **Primary Use Cases**: Most users will evaluate these options
4. ✅ **Marketing Critical**: Needed for competitive positioning
5. ✅ **CI/CD Compatible**: Can run in automated pipelines

### What We Don't Test (Cloud/RDBMS/Data Lake)

**Criteria for Exclusion from Benchmark Suite:**
1. ❌ **External Infrastructure Required**: Needs cloud accounts, database servers, etc.
2. ❌ **Non-Deterministic Performance**: Network latency, server load, throttling
3. ❌ **Cost Implications**: Running benchmarks could incur significant costs
4. ❌ **Environment-Specific**: Performance varies by user's infrastructure
5. ❌ **Integration Use Cases**: Not primary storage backends

---

## Guidance for Users

### Choosing a Trunk Based on Benchmark Results

**Start Here (Fully Benchmarked):**

1. **Need persistence?**
   - NO → `MemoryTrunk` (fastest, ephemeral)
   - YES → Continue...

2. **Need version history?**
   - YES → `DocumentStoreTrunk` (append-only log, versioning)
   - NO → Continue...

3. **Performance or simplicity?**
   - PERFORMANCE → `BTreeTrunk` ⭐ **RECOMMENDED** (balanced, fast cold start)
   - SIMPLICITY → `FileTrunk` (slowest, easy to debug)

**Advanced Scenarios (Not Benchmarked - Test Yourself):**

4. **Need cloud storage?**
   - Azure → `AzureTrunk` or `AzureTableTrunk`
   - AWS → `DynamoDbTrunk`
   - **⚠️ Benchmark in your own environment!**

5. **Integrating with existing database?**
   - MySQL → `MySqlTrunk`
   - PostgreSQL → `PostgreSqlTrunk`
   - SQL Server → `SqlServerTrunk`
   - SQLite (ADO.NET) → `SqliteTrunk`
   - **⚠️ Performance depends on your database server!**

6. **Analytics/Data Lake scenarios?**
   - Column storage → `ParquetTrunk`
   - Hot/Cold tiering → `TieredTrunk`
   - **⚠️ Specialized use cases, test with your workload!**

---

## Future Benchmark Work

### Potential Additions

1. **Docker-Based RDBMS Benchmarks** (Low Priority)
   - Use Docker containers for MySQL, PostgreSQL
   - Provides baseline performance comparison
   - Challenge: Still environment-dependent (Docker performance varies)

2. **Cloud Emulator Benchmarks** (Low Priority)
   - Azure Storage Emulator (Azurite)
   - AWS LocalStack (DynamoDB local)
   - Challenge: Emulators don't reflect real cloud performance

3. **Parquet Analytics Benchmark** (Future)
   - If analytics use case gains traction
   - Would focus on columnar query performance vs row-based

### Not Planned

- **Live Cloud Benchmarks**: Too costly, environment-specific
- **Remote Database Benchmarks**: Network latency dominates, not meaningful

---

## Conclusion

**Benchmark Coverage Philosophy:**

✅ **We benchmark what matters to most users**: Core trunks with no external dependencies

⚠️ **We document but don't benchmark**: Cloud/RDBMS/Data Lake trunks due to infrastructure complexity

📊 **Users should benchmark**: Cloud and RDBMS trunks in their own environment with their specific configuration

This approach provides:
- **Credible Performance Claims**: Based on reproducible, deterministic benchmarks
- **Clear Guidance**: Users know which trunks are tested and which require self-testing
- **Honest Marketing**: No inflated claims about untested trunks
- **Practical Testing Strategy**: Focus resources on benchmarks that provide value

---

## Questions?

For questions about benchmarking strategy or trunk selection, please file an issue on GitHub.

**See Also:**
- [BENCHMARK_SUMMARY.md](./BENCHMARK_SUMMARY.md) - Detailed benchmark results and analysis
- [README.md](./README.md) - How to run benchmarks
