using AcornDB;
using AcornDB.Storage;
using AcornDB.Metrics;
using AcornDB.Sync;

namespace AcornDB.Demo;

/// <summary>
/// Comprehensive demo of AcornDB v0.4-v0.6 production features:
/// - Branch batching for optimized network sync
/// - ResilientTrunk with retry/fallback/circuit breaker
/// - Prometheus/OpenTelemetry metrics integration
/// </summary>
public static class ProductionFeaturesDemo
{
    public static async Task RunAllDemos()
    {
        Console.WriteLine("🚀 AcornDB Production Features Demo (v0.4-v0.6)");
        Console.WriteLine("================================================\n");

        // Start metrics server first
        var metricsServer = StartMetricsServer();

        try
        {
            await Demo1_BranchBatching();
            await Demo2_ResilientTrunkRetry();
            await Demo3_ResilientTrunkFallback();
            await Demo4_CircuitBreaker();
            await Demo5_MetricsCollection();
            Demo6_ViewMetrics();
        }
        finally
        {
            metricsServer?.Stop();
            metricsServer?.Dispose();
        }

        Console.WriteLine("\n✅ All production features demos complete!");
    }

    private static MetricsServer? StartMetricsServer()
    {
        try
        {
            Console.WriteLine("📊 Starting Metrics Server");
            Console.WriteLine("---------------------------");
            var server = new MetricsServer(port: 9090);
            server.Start();
            Console.WriteLine();
            return server;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Could not start metrics server: {ex.Message}");
            Console.WriteLine("   (This is OK - metrics will still be collected)\n");
            return null;
        }
    }

    private static async Task Demo1_BranchBatching()
    {
        Console.WriteLine("📦 Demo 1: Branch Batching (Optimized Network Sync)");
        Console.WriteLine("---------------------------------------------------");
        Console.WriteLine("Batching groups multiple push/delete operations to reduce network overhead.\n");

        // Note: This demo shows the API. Real sync requires AcornSyncServer running.
        var branch = new Branch("http://localhost:5000")
            .WithBatching(batchSize: 5, batchTimeoutMs: 100)
            .WithSyncMode(SyncMode.PushOnly);

        Console.WriteLine("  ✓ Branch configured with batching (batch size: 5, timeout: 100ms)");
        Console.WriteLine("  ℹ️ Operations are queued and sent in batches automatically");
        Console.WriteLine("  ℹ️ Reduces network calls from N requests to N/batchSize requests\n");

        // Simulate multiple operations
        var tree = new Tree<DemoUser>(new MemoryTrunk<DemoUser>());
        tree.Entangle(branch);

        for (int i = 0; i < 12; i++)
        {
            tree.Stash($"user{i}", new DemoUser($"User {i}", $"user{i}@example.com"));
        }

        Console.WriteLine($"  ✓ Queued 12 stash operations");
        Console.WriteLine($"  ✓ Will be sent as 3 batches (5 + 5 + 2) instead of 12 individual requests");

        // Flush remaining operations
        branch.FlushBatch();
        Console.WriteLine($"  ✓ Flushed remaining batched operations\n");

        var stats = branch.GetStats();
        Console.WriteLine($"  📊 Branch Stats:");
        Console.WriteLine($"     - Total operations: {stats.TotalOperations}");
        Console.WriteLine($"     - Batching: Enabled\n");

        branch.Dispose();
    }

    private static async Task Demo2_ResilientTrunkRetry()
    {
        Console.WriteLine("🔄 Demo 2: ResilientTrunk with Retry Logic");
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("Automatic retry with exponential backoff for transient failures.\n");

        // Create a trunk that might fail (simulated with unreliable trunk)
        var primaryTrunk = new UnreliableTrunk<DemoUser>(failureRate: 0.3);
        var resilientTrunk = primaryTrunk.WithResilience(new ResilienceOptions
        {
            MaxRetries = 3,
            BaseRetryDelayMs = 50,
            RetryStrategy = RetryStrategy.ExponentialBackoff,
            EnableCircuitBreaker = false // Disable for this demo
        });

        var tree = new Tree<DemoUser>(resilientTrunk);

        Console.WriteLine("  ℹ️ Simulating 30% failure rate with automatic retry...\n");

        for (int i = 0; i < 5; i++)
        {
            try
            {
                tree.Stash($"user{i}", new DemoUser($"Resilient User {i}", $"user{i}@example.com"));
                Console.WriteLine($"  ✓ Successfully saved user{i}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Failed to save user{i}: {ex.Message}");
            }
        }

        var stats = resilientTrunk.GetStats();
        Console.WriteLine($"\n  📊 Resilience Stats:");
        Console.WriteLine($"     - Total retries: {stats.TotalRetries}");
        Console.WriteLine($"     - Circuit state: {stats.CircuitState}");
        Console.WriteLine($"     - Healthy: {stats.IsHealthy}\n");

        resilientTrunk.Dispose();
    }

    private static async Task Demo3_ResilientTrunkFallback()
    {
        Console.WriteLine("🔀 Demo 3: ResilientTrunk with Fallback");
        Console.WriteLine("---------------------------------------");
        Console.WriteLine("Graceful degradation: falls back to secondary trunk if primary fails.\n");

        // Primary trunk (might fail) with fallback to in-memory trunk
        var primaryTrunk = new UnreliableTrunk<DemoUser>(failureRate: 1.0); // Always fails
        var fallbackTrunk = new MemoryTrunk<DemoUser>();

        var resilientTrunk = primaryTrunk.WithFallback(
            fallbackTrunk,
            ResilienceOptions.Conservative
        );

        var tree = new Tree<DemoUser>(resilientTrunk);

        Console.WriteLine("  ℹ️ Primary trunk configured to always fail...");
        Console.WriteLine("  ℹ️ Fallback to in-memory trunk enabled\n");

        tree.Stash("alice", new DemoUser("Alice", "alice@example.com"));
        tree.Stash("bob", new DemoUser("Bob", "bob@example.com"));

        Console.WriteLine($"  ✓ Data saved via fallback trunk");
        Console.WriteLine($"  ✓ Application continues to function despite primary failure");

        var alice = tree.Crack("alice");
        Console.WriteLine($"  ✓ Retrieved: {alice?.Name} ({alice?.Email})\n");

        var stats = resilientTrunk.GetStats();
        Console.WriteLine($"  📊 Resilience Stats:");
        Console.WriteLine($"     - Total fallbacks: {stats.TotalFallbacks}");
        Console.WriteLine($"     - System remains operational: ✓\n");

        resilientTrunk.Dispose();
    }

    private static async Task Demo4_CircuitBreaker()
    {
        Console.WriteLine("⚡ Demo 4: Circuit Breaker Pattern");
        Console.WriteLine("----------------------------------");
        Console.WriteLine("Prevents cascading failures by 'opening' after repeated failures.\n");

        var primaryTrunk = new UnreliableTrunk<DemoUser>(failureRate: 0.8); // High failure rate
        var fallbackTrunk = new MemoryTrunk<DemoUser>();

        var resilientTrunk = primaryTrunk.WithFallback(
            fallbackTrunk,
            new ResilienceOptions
            {
                MaxRetries = 1,
                EnableCircuitBreaker = true,
                CircuitBreakerThreshold = 3, // Open after 3 failures
                CircuitBreakerTimeout = TimeSpan.FromSeconds(5)
            }
        );

        var tree = new Tree<DemoUser>(resilientTrunk);

        Console.WriteLine("  ℹ️ Circuit breaker threshold: 3 failures");
        Console.WriteLine("  ℹ️ Simulating 80% failure rate...\n");

        // Trigger multiple failures to open circuit
        for (int i = 0; i < 8; i++)
        {
            try
            {
                tree.Stash($"user{i}", new DemoUser($"User {i}", $"user{i}@example.com"));
                var stats = resilientTrunk.GetStats();
                Console.WriteLine($"  [{i+1}] Circuit: {stats.CircuitState} | Failures: {stats.FailureCount} | Using: {(stats.TotalFallbacks > 0 ? "Fallback" : "Primary")}");
            }
            catch { }

            await Task.Delay(50); // Small delay to observe state changes
        }

        var finalStats = resilientTrunk.GetStats();
        Console.WriteLine($"\n  📊 Final Circuit Breaker State:");
        Console.WriteLine($"     - State: {finalStats.CircuitState}");
        Console.WriteLine($"     - Total failures: {finalStats.FailureCount}");
        Console.WriteLine($"     - Circuit breaker trips: {finalStats.CircuitBreakerTrips}");
        Console.WriteLine($"     - Fallback activations: {finalStats.TotalFallbacks}");

        if (finalStats.CircuitState == CircuitBreakerState.Open)
        {
            Console.WriteLine($"     ⚡ Circuit OPEN - automatically using fallback to prevent cascading failures\n");
        }

        resilientTrunk.Dispose();
    }

    private static async Task Demo5_MetricsCollection()
    {
        Console.WriteLine("📊 Demo 5: Metrics Collection");
        Console.WriteLine("-----------------------------");
        Console.WriteLine("Comprehensive observability with Prometheus/OpenTelemetry metrics.\n");

        // Configure metrics labels
        MetricsConfiguration.ConfigureLabels(
            environment: "production",
            region: "us-east-1",
            instance: "demo-instance-1"
        );

        Console.WriteLine("  ✓ Configured metrics labels (environment, region, instance)");

        // Perform operations to generate metrics
        var tree = new Tree<DemoUser>(new MemoryTrunk<DemoUser>());

        Console.WriteLine("  ℹ️ Performing operations to generate metrics...\n");

        for (int i = 0; i < 10; i++)
        {
            var startTime = DateTime.UtcNow;
            tree.Stash($"user{i}", new DemoUser($"User {i}", $"user{i}@example.com"));
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            MetricsCollector.Instance.RecordStash("DemoUser", duration);
        }

        Console.WriteLine($"  ✓ Recorded 10 stash operations");

        for (int i = 0; i < 10; i++)
        {
            var startTime = DateTime.UtcNow;
            var user = tree.Crack($"user{i}");
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var cacheHit = user != null;

            MetricsCollector.Instance.RecordCrack("DemoUser", duration, cacheHit);
        }

        Console.WriteLine($"  ✓ Recorded 10 crack operations");
        Console.WriteLine($"  ✓ Recorded cache hit/miss statistics\n");

        // Display current metrics
        Console.WriteLine("  📈 Current Metrics Summary:");
        Console.WriteLine("     Operations:");
        Console.WriteLine($"       - Stash: 10");
        Console.WriteLine($"       - Crack: 10");
        Console.WriteLine($"       - Cache hits: 10");
        Console.WriteLine($"       - Cache hit rate: 100%");
        Console.WriteLine();
    }

    private static void Demo6_ViewMetrics()
    {
        Console.WriteLine("🔍 Demo 6: View Metrics Output");
        Console.WriteLine("------------------------------");
        Console.WriteLine("Metrics available in multiple formats:\n");

        Console.WriteLine("  📊 Prometheus Format (text):");
        Console.WriteLine("  " + new string('-', 60));
        var prometheusMetrics = MetricsCollector.Instance.ExportPrometheus();
        var prometheusLines = prometheusMetrics.Split('\n').Take(10);
        foreach (var line in prometheusLines)
        {
            if (!string.IsNullOrWhiteSpace(line))
                Console.WriteLine($"  {line}");
        }
        Console.WriteLine("  ... (truncated for brevity) ...");
        Console.WriteLine();

        Console.WriteLine("  📊 OpenTelemetry Format (JSON):");
        Console.WriteLine("  " + new string('-', 60));
        var jsonMetrics = MetricsCollector.Instance.ExportJson();
        var formattedJson = System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonSerializer.Deserialize<object>(jsonMetrics),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
        );
        var jsonLines = formattedJson.Split('\n').Take(15);
        foreach (var line in jsonLines)
        {
            Console.WriteLine($"  {line}");
        }
        Console.WriteLine("  ... (truncated for brevity) ...");
        Console.WriteLine();

        Console.WriteLine("  🌐 Live Endpoints:");
        Console.WriteLine("     - Prometheus: http://localhost:9090/metrics");
        Console.WriteLine("     - JSON:       http://localhost:9090/metrics?format=json");
        Console.WriteLine("     - Health:     http://localhost:9090/health");
        Console.WriteLine();
    }
}

// Demo models
public record DemoUser(string Name, string Email);

/// <summary>
/// Simulated unreliable trunk for testing resilience features
/// </summary>
internal class UnreliableTrunk<T> : ITrunk<T>, ITrunkCapabilities, IDisposable where T : class
{
    private readonly MemoryTrunk<T> _innerTrunk = new();
    private readonly double _failureRate;
    private readonly Random _random = new();

    public UnreliableTrunk(double failureRate)
    {
        _failureRate = Math.Clamp(failureRate, 0.0, 1.0);
    }

    private void SimulateFailure(string operation)
    {
        if (_random.NextDouble() < _failureRate)
        {
            throw new IOException($"Simulated transient failure in {operation}");
        }
    }

    public void Stash(string id, Nut<T> nut)
    {
        SimulateFailure("Stash");
        _innerTrunk.Stash(id, nut);
    }

    [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
    public void Save(string id, Nut<T> nut) => Stash(id, nut);

    public Nut<T>? Crack(string id)
    {
        SimulateFailure("Crack");
        return _innerTrunk.Crack(id);
    }

    [Obsolete("Use Crack() instead. This method will be removed in a future version.")]
    public Nut<T>? Load(string id) => Crack(id);

    public void Toss(string id)
    {
        SimulateFailure("Toss");
        _innerTrunk.Toss(id);
    }

    [Obsolete("Use Toss() instead. This method will be removed in a future version.")]
    public void Delete(string id) => Toss(id);

    public IEnumerable<Nut<T>> CrackAll()
    {
        SimulateFailure("CrackAll");
        return _innerTrunk.CrackAll();
    }

    [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
    public IEnumerable<Nut<T>> LoadAll() => CrackAll();

    public IReadOnlyList<Nut<T>> GetHistory(string id) => _innerTrunk.GetHistory(id);
    public IEnumerable<Nut<T>> ExportChanges() => _innerTrunk.ExportChanges();
    public void ImportChanges(IEnumerable<Nut<T>> incoming) => _innerTrunk.ImportChanges(incoming);
    public ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
    {
        SupportsHistory = false,
        SupportsSync = true,
        IsDurable = false,
        SupportsAsync = false,
        SupportsNativeIndexes = false,
        SupportsFullTextSearch = false,
        SupportsComputedIndexes = false,
        TrunkType = "UnreliableTrunk (Demo)"
    };

    // ITrunkCapabilities
    public bool SupportsHistory => false;
    public bool SupportsSync => true;
    public bool IsDurable => false;
    public bool SupportsAsync => false;
    public bool SupportsNativeIndexes => false;
    public bool SupportsFullTextSearch => false;
    public bool SupportsComputedIndexes => false;
    public string TrunkType => "UnreliableTrunk (Demo)";

    public void Dispose()
    {
        // MemoryTrunk doesn't implement IDisposable, so nothing to dispose
    }

    // IRoot support - stub implementation (to be fully implemented later)
    public IReadOnlyList<IRoot> Roots => Array.Empty<IRoot>();
    public void AddRoot(IRoot root) { /* TODO: Implement root support */ }
    public bool RemoveRoot(string name) => false;
}
