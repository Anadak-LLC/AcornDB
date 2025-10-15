# AcornDB.Persistence.DataLake

**Apache Parquet integration for AcornDB** - Bidirectional data lake interoperability with columnar storage, partitioning, cloud storage, and advanced caching.

## Features

- 📊 **Columnar Storage** - Apache Parquet format optimized for analytics
- 🗂️ **Intelligent Partitioning** - Date-based, value-based, and composite partitioning strategies
- ☁️ **Cloud Storage** - Native S3 and Azure Data Lake support
- 🔄 **Bidirectional Sync** - Import from and export to data lakes
- 🗜️ **Compression** - Snappy, GZip, LZ4 compression support
- 🎯 **Interoperability** - Works with Spark, Athena, Synapse Analytics, Databricks
- 💾 **Multi-Level Caching** - Near/far caching for distributed applications
- 🔥❄️ **Hot/Cold Tiering** - Automatic data aging and cost optimization

## Installation

```bash
dotnet add package AcornDB.Persistence.DataLake
```

## Quick Start

### Local Parquet Files

```csharp
using AcornDB;
using AcornDB.Persistence.DataLake;

// Create Parquet trunk for local files
var parquetTrunk = new ParquetTrunk<User>("./datalake/users");
var tree = new Tree<User>(parquetTrunk);

// Use like any other trunk
tree.Stash(new User { Id = "user1", Name = "Alice" });
tree.Stash(new User { Id = "user2", Name = "Bob" });

var user = tree.Crack("user1");
```

### Cloud Data Lakes (S3)

```csharp
using AcornDB.Persistence.Cloud;
using AcornDB.Persistence.DataLake;

// S3 data lake with partitioning
var s3Provider = new S3Provider("my-data-lake-bucket");
var parquetTrunk = new ParquetTrunk<Event>(
    "events/",
    s3Provider,
    new ParquetOptions
    {
        PartitionStrategy = PartitionStrategy.ByYearMonthDay(),
        CompressionMethod = CompressionMethod.Snappy
    }
);

var tree = new Tree<Event>(parquetTrunk);
tree.Stash(new Event { Id = "evt1", Type = "click", Timestamp = DateTime.UtcNow });

// Data is written to: s3://my-bucket/events/year=2025/month=10/day=15/Event.parquet
```

### Azure Data Lake

```csharp
using AcornDB.Persistence.Cloud;
using AcornDB.Persistence.DataLake;

// Azure Data Lake Storage Gen2
var azureProvider = new AzureBlobProvider(connectionString, "datalake");
var parquetTrunk = new ParquetTrunk<Product>(
    "products/",
    azureProvider,
    new ParquetOptions
    {
        PartitionStrategy = PartitionStrategy.ByValue<Product>(p => p.Category),
        CompressionMethod = CompressionMethod.Gzip
    }
);

var tree = new Tree<Product>(parquetTrunk);
```

## Partitioning Strategies

### Date-Based Partitioning

```csharp
// Partition by date (uses Nut.Timestamp)
var options = new ParquetOptions
{
    PartitionStrategy = PartitionStrategy.ByDate("yyyy/MM/dd")
};

// Creates: 2025/10/15/TypeName.parquet
```

### Hive-Style Partitioning

```csharp
// Hive-compatible partitioning
var options = new ParquetOptions
{
    PartitionStrategy = PartitionStrategy.ByYearMonthDay()
};

// Creates: year=2025/month=10/day=15/TypeName.parquet
// Compatible with AWS Athena, Spark, Hive
```

### Value-Based Partitioning

```csharp
// Partition by payload property
var options = new ParquetOptions
{
    PartitionStrategy = PartitionStrategy.ByValue<Event>(e => e.EventType)
};

// Creates: click/Event.parquet, view/Event.parquet, etc.
```

### Composite Partitioning

```csharp
// Combine multiple partition strategies
var options = new ParquetOptions
{
    PartitionStrategy = PartitionStrategy.Composite(
        PartitionStrategy.ByYearMonthDay(),
        PartitionStrategy.ByValue<Event>(e => e.Region)
    )
};

// Creates: year=2025/month=10/day=15/us-west/Event.parquet
```

## Compression Options

```csharp
// Snappy (default) - Fast compression/decompression
var options = new ParquetOptions
{
    CompressionMethod = CompressionMethod.Snappy
};

// GZip - Better compression ratio
var options = new ParquetOptions
{
    CompressionMethod = CompressionMethod.Gzip
};

// LZ4 - Fastest compression
var options = new ParquetOptions
{
    CompressionMethod = CompressionMethod.Lz4
};

// No compression
var options = new ParquetOptions
{
    CompressionMethod = CompressionMethod.None
};
```

## Preset Options

```csharp
// Analytics workload (GZip compression, large row groups, immutable)
var trunk = new ParquetTrunk<T>(path, ParquetOptions.Analytics);

// Streaming workload (Snappy compression, small row groups, append mode)
var trunk = new ParquetTrunk<T>(path, ParquetOptions.Streaming);

// Default (Snappy compression, 100K row groups, append mode)
var trunk = new ParquetTrunk<T>(path, ParquetOptions.Default);
```

## Extension Methods

### Export to Parquet

```csharp
// Export existing trunk data to Parquet
var btreeTrunk = new BTreeTrunk<User>("./data");
await btreeTrunk.ExportToParquet("./analytics/users.parquet");

// Export to S3
var s3Provider = new S3Provider("analytics-bucket");
await btreeTrunk.ExportToParquet(
    "users/",
    s3Provider,
    new ParquetOptions
    {
        PartitionStrategy = PartitionStrategy.ByYearMonthDay(),
        CompressionMethod = CompressionMethod.Gzip
    }
);
```

### Import from Parquet

```csharp
// Import Parquet data into any trunk
var btreeTrunk = new BTreeTrunk<User>("./data");
await btreeTrunk.ImportFromParquet("./datalake/users.parquet");

// Import from S3
var s3Provider = new S3Provider("datalake-bucket");
await btreeTrunk.ImportFromParquet("users/", s3Provider);
```

### Bidirectional Sync

```csharp
// Sync local tree with data lake
var localTree = new Tree<User>(new BTreeTrunk<User>("./local"));

var tangle = localTree.SyncWithDataLake(
    "s3://datalake/users/",
    s3Provider,
    new ParquetOptions
    {
        PartitionStrategy = PartitionStrategy.ByYearMonthDay()
    }
);

// Changes flow both ways
localTree.Stash(new User { Id = "user1", Name = "Alice" });
localTree.Shake(); // Syncs to data lake
```

## Use Cases

### Analytics Export

```csharp
// Production database -> Data lake for analytics
var prodTrunk = new PostgreSqlTrunk<Order>(connectionString);
var s3Provider = new S3Provider("analytics-datalake");

await prodTrunk.ExportToParquet(
    "orders/",
    s3Provider,
    new ParquetOptions
    {
        PartitionStrategy = PartitionStrategy.ByYearMonthDay(),
        CompressionMethod = CompressionMethod.Gzip
    }
);

// Now queryable with Athena, Spark, Presto, etc.
```

### Cold Storage Archive

```csharp
// Archive old data to Parquet
var hotData = new BTreeTrunk<Event>("./hot");
var coldStorage = new ParquetTrunk<Event>(
    "s3://archive/events/",
    s3Provider,
    new ParquetOptions
    {
        PartitionStrategy = PartitionStrategy.ByDate("yyyy/MM"),
        CompressionMethod = CompressionMethod.Gzip,
        AppendMode = false // Immutable archives
    }
);

// Move old data to cold storage
var oldEvents = hotData.LoadAll()
    .Where(e => e.Timestamp < DateTime.UtcNow.AddMonths(-6));

await coldStorage.ImportChangesAsync(oldEvents);
```

### Data Lake Ingestion

```csharp
// Ingest data from S3 data lake into AcornDB
var s3Provider = new S3Provider("company-datalake");
var parquetTrunk = new ParquetTrunk<Customer>(
    "customers/",
    s3Provider
);

var tree = new Tree<Customer>(parquetTrunk);

// Query data lake data through AcornDB
var customers = tree.LoadAll();
var vipCustomers = customers.Where(c => c.TotalSpend > 10000);
```

### Spark/Databricks Integration

```csharp
// Write data compatible with Spark
var parquetTrunk = new ParquetTrunk<Transaction>(
    "s3://datalake/transactions/",
    s3Provider,
    new ParquetOptions
    {
        // Hive-style partitioning for Spark compatibility
        PartitionStrategy = PartitionStrategy.Composite(
            PartitionStrategy.ByYearMonthDay(),
            PartitionStrategy.ByValue<Transaction>(t => t.Region)
        ),
        CompressionMethod = CompressionMethod.Snappy
    }
);

var tree = new Tree<Transaction>(parquetTrunk);
tree.Stash(new Transaction { Id = "tx1", Amount = 100, Region = "us-west" });

// Now readable in Spark:
// spark.read.parquet("s3://datalake/transactions/year=2025/month=10/day=15/us-west/")
```

## Performance Considerations

### Batch Operations

Parquet is optimized for batch writes. Single-item operations are inefficient:

```csharp
// ❌ Inefficient - rewrites entire file for each item
for (int i = 0; i < 1000; i++)
{
    parquetTrunk.Save($"id-{i}", nut);
}

// ✅ Efficient - batch write
var nuts = Enumerable.Range(0, 1000)
    .Select(i => new Nut<T> { Id = $"id-{i}", Payload = data });

await parquetTrunk.ImportChangesAsync(nuts);
```

### Point Lookups

Parquet doesn't support indexed point lookups. For frequent ID-based queries:

```csharp
// ❌ Slow - scans all partitions
var user = parquetTrunk.Load("user123");

// ✅ Better - use Parquet for analytics, BTree for OLTP
var hotTrunk = new BTreeTrunk<User>("./hot"); // Fast point queries
var coldTrunk = new ParquetTrunk<User>("./analytics"); // Analytics

// Tiered architecture
var tieredTrunk = new TieredTrunk<User>(
    hot: hotTrunk,
    cold: coldTrunk,
    archiveAfter: TimeSpan.FromDays(30)
);
```

### Partitioning Benefits

Proper partitioning dramatically improves query performance:

```csharp
// Without partitioning: scans all data
var options1 = new ParquetOptions(); // Single file

// With partitioning: scans only relevant partitions
var options2 = new ParquetOptions
{
    PartitionStrategy = PartitionStrategy.ByYearMonthDay()
};

// Query for Oct 15, 2025 only reads: year=2025/month=10/day=15/
// Skips all other dates (partition pruning)
```

## Compatibility

### Query Engines

ParquetTrunk output is compatible with:

- ✅ **AWS Athena** - Serverless SQL queries on S3
- ✅ **Apache Spark** - Distributed data processing
- ✅ **Databricks** - Unified analytics platform
- ✅ **Presto/Trino** - Distributed SQL engine
- ✅ **Azure Synapse Analytics** - Analytics service
- ✅ **Google BigQuery** - Data warehouse (via load from GCS)
- ✅ **Pandas** - Python data analysis
- ✅ **DuckDB** - Embedded analytical database

### Example: AWS Athena Query

```sql
-- After writing with ParquetTrunk
CREATE EXTERNAL TABLE users (
    Id STRING,
    Version INT,
    Timestamp BIGINT,
    Payload STRING,
    ExpiresAt BIGINT
)
PARTITIONED BY (year INT, month INT, day INT)
STORED AS PARQUET
LOCATION 's3://my-bucket/users/';

-- Discover partitions
MSCK REPAIR TABLE users;

-- Query efficiently (partition pruning)
SELECT * FROM users
WHERE year = 2025 AND month = 10 AND day = 15;
```

## Limitations

1. **No Point Query Optimization** - Parquet scans files; not optimized for ID lookups
2. **No History Support** - Parquet is append-only/immutable; no versioning
3. **Batch Write Optimized** - Single-item writes rewrite entire files (slow)
4. **No ACID Deletes** - Deletes require read-filter-rewrite (use Delta Lake for ACID)

For OLTP workloads, use BTreeTrunk or RDBMS trunks. Use ParquetTrunk for:
- Analytics export
- Cold storage archival
- Data lake integration
- Batch data processing

## Caching Architectures

### Simple In-Memory Cache

Wrap any trunk with write-through in-memory caching:

```csharp
// Add cache to any trunk
var dbTrunk = new PostgreSqlTrunk<Product>(connectionString);
var cachedTrunk = dbTrunk.WithCache(CacheOptions.Default);

// Fast reads from cache, writes update both cache and database
var tree = new Tree<Product>(cachedTrunk);
tree.Stash(new Product { Id = "p1", Name = "Widget" });

// Check cache statistics
var stats = cachedTrunk.GetCacheStats();
Console.WriteLine($"Cached: {stats.ActiveItemCount} items");
```

### Cache Configuration

```csharp
// Short-lived cache (1min TTL, 1K items)
var shortCache = dbTrunk.WithCache(CacheOptions.ShortLived);

// Long-lived cache (1 hour TTL, 100K items)
var longCache = dbTrunk.WithCache(CacheOptions.LongLived);

// Aggressive caching (infinite TTL, unlimited size)
// Use only for read-only or append-only data
var aggressiveCache = dbTrunk.WithCache(CacheOptions.Aggressive);

// Custom configuration
var customCache = dbTrunk.WithCache(new CacheOptions
{
    TimeToLive = TimeSpan.FromMinutes(15),
    MaxCacheSize = 5_000,
    WarmCacheOnLoadAll = true
});
```

### Near/Far Distributed Caching

Multi-level caching for distributed applications:

```csharp
// Setup: Near (local memory) → Far (Redis) → Database
var dbTrunk = new PostgreSqlTrunk<User>(connectionString);
var redisTrunk = new RedisTrunk<User>(redisConnection);

var distributedCache = dbTrunk.WithNearFarCache(
    redisTrunk,
    NearFarOptions.Default
);

// Reads: Check near cache → Check Redis → Database → Populate caches
var user = distributedCache.Load("user123");

// Writes: Update database → Invalidate caches (consistency)
distributedCache.Save("user123", nut);
```

### Near/Far Strategies

```csharp
// Write-through: Update caches immediately on write
var writeThrough = dbTrunk.WithNearFarCache(
    redisTrunk,
    NearFarOptions.WriteThrough
);

// Invalidate: Clear caches on write (safest for consistency)
var invalidate = dbTrunk.WithNearFarCache(
    redisTrunk,
    NearFarOptions.Default // Uses Invalidate strategy
);

// Write-around: Bypass cache on writes (for write-heavy workloads)
var writeAround = dbTrunk.WithNearFarCache(
    redisTrunk,
    NearFarOptions.WriteAround
);
```

### Cache Management

```csharp
// Clear all caches
distributedCache.ClearAllCaches();

// Clear specific cache tier
distributedCache.ClearNearCache(); // Local only
distributedCache.ClearFarCache();  // Redis only

// Get statistics
var stats = distributedCache.GetStats();
Console.WriteLine($"Near cache: {stats.NearCacheCount} items");
Console.WriteLine($"Far cache: {stats.FarCacheCount} items");
Console.WriteLine($"Database: {stats.BackingStoreCount} items");
```

### Full-Stack Architecture

Combine caching + tiering for ultimate performance and cost efficiency:

```csharp
// Architecture:
// Near Cache (Memory) → Far Cache (Redis) → Hot Tier (BTree) → Cold Tier (Parquet)

var fullStack = CachingExtensions.CreateFullStack<Product>(
    farCache: new RedisTrunk<Product>(redisConnection),
    hotPath: "./data/hot",
    coldPath: "./data/cold",
    nearFarOptions: NearFarOptions.WriteThrough,
    tieringOptions: TieringOptions<Product>.AutoArchive30Days
);

var tree = new Tree<Product>(fullStack);

// Ultra-fast reads from near cache
// Shared cache layer for multiple instances (Redis)
// Hot data in fast BTree storage
// Old data automatically archived to Parquet
// 99%+ cost savings on storage
```

### Use Cases

**Simple Cache** - Single-instance applications, reduce database load
```csharp
var cached = sqlTrunk.WithCache();
```

**Near/Far Cache** - Distributed applications, microservices
```csharp
var distributed = dbTrunk.WithNearFarCache(redisTrunk);
```

**Full Stack** - Enterprise applications with analytics requirements
```csharp
var enterprise = CachingExtensions.CreateFullStack<T>(
    redisTrunk, "./hot", "./cold"
);
```

### Performance Benefits

| Architecture | Read Latency | Write Latency | Cost Savings |
|--------------|--------------|---------------|--------------|
| Database Only | 10-50ms | 10-50ms | Baseline |
| Simple Cache | <1ms (hit) | 10-50ms | 50-80% DB load |
| Near/Far | <0.1ms (near) | 10-50ms | 90% DB load |
| Full Stack | <0.1ms (near) | 10-50ms | 99% storage cost |

## Advanced: Delta Lake (Future)

For ACID operations on data lakes, consider Delta Lake format (future enhancement):

```csharp
// Future API
var deltaLakeTrunk = new DeltaLakeTrunk<T>(
    "s3://datalake/table/",
    s3Provider
);

// ACID updates, deletes, time travel
deltaLakeTrunk.Save("id", nut); // Update in place
deltaLakeTrunk.Delete("id"); // ACID delete
var history = deltaLakeTrunk.GetHistory("id"); // Time travel
```

## Support

- 📖 [AcornDB Documentation](https://github.com/Anadak-LLC/AcornDB)
- 🐛 [Report Issues](https://github.com/Anadak-LLC/AcornDB/issues)
- 💬 [Discussions](https://github.com/Anadak-LLC/AcornDB/discussions)

## License

MIT License - see [LICENSE](../LICENSE) file for details.
