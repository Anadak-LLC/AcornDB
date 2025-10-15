# AcornDB Production Features Demo (v0.4-v0.6)

This demo showcases the production-ready features implemented in AcornDB v0.4 through v0.6.

## Features Demonstrated

### 1. Branch Batching (v0.4)
**Purpose**: Optimize network sync by grouping multiple operations into batches

**Benefits**:
- Reduces network overhead from N requests to N/batch_size requests
- Configurable batch size and auto-flush timeout
- Fire-and-forget async batching

**Usage**:
```csharp
var branch = new Branch("http://localhost:5000")
    .WithBatching(batchSize: 10, batchTimeoutMs: 100);

// Operations are automatically queued and sent in batches
tree.Entangle(branch);
tree.Stash("user1", new User("Alice"));
tree.Stash("user2", new User("Bob"));
// ... more operations

// Manually flush remaining operations
branch.FlushBatch();
```

**Demo Output**:
- Shows how 12 operations are batched into 3 HTTP requests (5+5+2)
- Demonstrates automatic flushing on batch size threshold
- Displays batch statistics

---

### 2. ResilientTrunk - Retry Logic (v0.5)
**Purpose**: Automatic retry with exponential backoff for transient failures

**Features**:
- Configurable retry count (default: 3)
- Exponential backoff with jitter
- Smart exception detection (retryable vs permanent)
- Thread-safe operation

**Usage**:
```csharp
var resilientTrunk = trunk.WithResilience(new ResilienceOptions
{
    MaxRetries = 3,
    BaseRetryDelayMs = 100,
    RetryStrategy = RetryStrategy.ExponentialBackoff,
    UseJitter = true
});

var tree = new Tree<User>(resilientTrunk);
tree.Stash("user1", new User("Alice")); // Automatically retries on failure
```

**Demo Output**:
- Simulates 30% failure rate
- Shows automatic retry attempts
- Displays retry statistics

---

### 3. ResilientTrunk - Fallback (v0.5)
**Purpose**: Graceful degradation by falling back to secondary trunk when primary fails

**Features**:
- Seamless failover to fallback trunk
- Application continues functioning despite primary failure
- Tracks fallback usage statistics

**Usage**:
```csharp
var primaryTrunk = new FileTrunk<User>("remote/path");
var fallbackTrunk = new MemoryTrunk<User>();

var resilientTrunk = primaryTrunk.WithFallback(
    fallbackTrunk,
    ResilienceOptions.Conservative
);

var tree = new Tree<User>(resilientTrunk);
// Automatically uses fallback if primary fails
```

**Demo Output**:
- Simulates complete primary trunk failure
- Shows automatic fallback activation
- Verifies data is saved and retrievable via fallback

---

### 4. Circuit Breaker Pattern (v0.5)
**Purpose**: Prevent cascading failures by "opening" circuit after repeated failures

**States**:
- **Closed**: Normal operation, all requests flow through
- **Open**: Too many failures detected, requests blocked, fallback used
- **HalfOpen**: Testing recovery, single request allowed

**Features**:
- Configurable failure threshold (default: 5)
- Automatic state transitions
- Recovery timeout (default: 30 seconds)
- Health monitoring

**Usage**:
```csharp
var resilientTrunk = primaryTrunk.WithFallback(
    fallbackTrunk,
    new ResilienceOptions
    {
        EnableCircuitBreaker = true,
        CircuitBreakerThreshold = 3,
        CircuitBreakerTimeout = TimeSpan.FromSeconds(5)
    }
);

// Monitor circuit state
var stats = resilientTrunk.GetStats();
Console.WriteLine($"Circuit: {stats.CircuitState}");
```

**Demo Output**:
- Shows circuit state transitions (Closed → Open)
- Displays failure count and threshold
- Demonstrates automatic fallback when circuit opens

---

### 5. Prometheus/OpenTelemetry Metrics (v0.6)
**Purpose**: Comprehensive observability with industry-standard metrics formats

**Metrics Collected**:
- **Operations**: stash, crack, toss, squabble counts
- **Sync**: push, pull, conflict counts
- **Cache**: hit rate, evictions
- **Resilience**: retries, fallbacks, circuit breaker trips
- **Latency**: P50, P95, P99 percentiles
- **Per-Tree**: individual tree statistics

**Formats**:
- **Prometheus**: Text format (default) - compatible with Prometheus scraping
- **JSON**: OpenTelemetry-compatible JSON format

**Usage**:
```csharp
// Configure labels (optional)
MetricsConfiguration.ConfigureLabels(
    environment: "production",
    region: "us-east-1",
    instance: "app-server-1"
);

// Record operations
MetricsCollector.Instance.RecordStash("Users", durationMs: 2.5);
MetricsCollector.Instance.RecordCrack("Users", durationMs: 0.3, cacheHit: true);

// Start HTTP server
var server = new MetricsServer(port: 9090);
server.Start();
```

**Endpoints**:
- `http://localhost:9090/metrics` - Prometheus text format
- `http://localhost:9090/metrics?format=json` - JSON format
- `http://localhost:9090/health` - Health check

**Demo Output**:
- Shows Prometheus text format sample
- Shows OpenTelemetry JSON format sample
- Lists available HTTP endpoints

---

## Resilience Options

### Pre-configured Strategies

**Default**: Balanced approach
- 3 retries
- 100ms base delay, 5000ms max
- Exponential backoff with jitter
- Circuit breaker enabled (5 failures, 30s timeout)

**Aggressive**: High reliability networks
- 5 retries
- 50ms base delay, 2000ms max
- Fast backoff
- Circuit breaker (10 failures)

**Conservative**: Unreliable networks
- 2 retries
- 200ms base delay, 10000ms max
- Slow backoff
- Circuit breaker (3 failures, 60s timeout)

**Circuit Breaker Only**: Fast failure detection
- 0 retries
- Circuit breaker (3 failures, 10s timeout)

---

## Running the Demo

```bash
cd AcornDB.Demo
dotnet run
```

Choose option:
1. Basic trunk abstraction demos
2. **Production features (v0.4-v0.6)** ← This demo
3. Run all demos

---

## Integration with Existing Code

All production features are designed to work seamlessly with existing AcornDB code:

```csharp
// Before: Basic setup
var tree = new Tree<User>(new FileTrunk<User>("data/users"));

// After: Production-ready with batching, resilience, and metrics
var primaryTrunk = new FileTrunk<User>("data/users");
var fallbackTrunk = new MemoryTrunk<User>();
var resilientTrunk = primaryTrunk
    .WithFallback(fallbackTrunk, ResilienceOptions.Default);

var tree = new Tree<User>(resilientTrunk);

var branch = new Branch("http://backup-server:5000")
    .WithBatching(batchSize: 10, batchTimeoutMs: 100);

tree.Entangle(branch);

// Start metrics server
var metricsServer = new MetricsServer(port: 9090);
metricsServer.Start();

// Your existing code works unchanged!
tree.Stash("alice", new User("Alice"));
var user = tree.Crack("alice");
```

---

## Production Deployment Checklist

✅ **Resilience**
- [ ] Primary trunk configured with retry logic
- [ ] Fallback trunk configured for critical data
- [ ] Circuit breaker thresholds tuned for your network
- [ ] Health monitoring in place

✅ **Sync Optimization**
- [ ] Branch batching enabled with appropriate batch size
- [ ] Batch timeout configured based on latency requirements
- [ ] Manual flush called before application shutdown

✅ **Observability**
- [ ] Metrics server running and accessible
- [ ] Prometheus/OTEL collector configured
- [ ] Metrics labels set (environment, region, instance)
- [ ] Dashboards configured (Grafana, etc.)
- [ ] Alerts configured for circuit breaker trips, high error rates

✅ **Monitoring**
- [ ] Track cache hit rates
- [ ] Monitor retry/fallback counts
- [ ] Watch circuit breaker state
- [ ] Alert on elevated latencies (P95, P99)

---

## Troubleshooting

**Metrics server fails to start**
- Check if port 9090 is already in use
- Verify firewall allows HTTP traffic on chosen port
- Try different port: `new MetricsServer(port: 9091)`

**Circuit breaker opens frequently**
- Increase `CircuitBreakerThreshold`
- Increase `CircuitBreakerTimeout`
- Consider using `ResilienceOptions.Conservative`
- Check network stability

**High retry counts**
- Review network reliability
- Consider increasing `BaseRetryDelayMs`
- Check if exceptions are truly transient
- Consider using `ResilienceOptions.Conservative`

**Batch operations not flushing**
- Ensure `FlushBatch()` called before shutdown
- Check batch timeout is appropriate for your use case
- Verify network connectivity to sync server

---

## Next Steps

1. **Integrate**: Add production features to your AcornDB application
2. **Monitor**: Set up Prometheus/Grafana dashboards
3. **Tune**: Adjust resilience options based on your environment
4. **Alert**: Configure alerts for critical metrics
5. **Scale**: Deploy with confidence knowing your data is resilient

For more information, see the main AcornDB documentation.
