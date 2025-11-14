using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Sync;
using AcornDB.Metrics;

namespace AcornDB.Test
{
    /// <summary>
    /// Integration tests for production features (v0.4-v0.6):
    /// - Branch batching
    /// - ResilientTrunk (retry, fallback, circuit breaker)
    /// - Metrics collection and export
    /// </summary>
    public class ProductionFeaturesTests
    {
        // ===== Branch Batching Tests =====

        [Fact]
        public void BranchBatching_Configuration_Success()
        {
            // Arrange & Act
            var branch = new Branch("http://localhost:5000")
                .WithBatching(batchSize: 5, batchTimeoutMs: 100);

            // Assert
            var stats = branch.GetStats();
            Assert.Equal("http://localhost:5000", stats.RemoteUrl);

            branch.Dispose();
        }

        [Fact]
        public void BranchBatching_FlushBatch_DoesNotThrow()
        {
            // Arrange
            var branch = new Branch("http://localhost:5000")
                .WithBatching(batchSize: 10, batchTimeoutMs: 100);

            // Act & Assert (should not throw even if server is not running)
            var exception = Record.Exception(() => branch.FlushBatch());
            Assert.Null(exception); // Should not throw

            branch.Dispose();
        }

        [Fact]
        public void BranchBatching_MultipleConfigurations_Success()
        {
            // Arrange & Act
            var branch = new Branch("http://localhost:5000")
                .WithBatching(batchSize: 20, batchTimeoutMs: 50)
                .WithSyncMode(SyncMode.PushOnly)
                .WithConflictDirection(ConflictDirection.PreferLocal);

            // Assert
            var stats = branch.GetStats();
            Assert.Equal(SyncMode.PushOnly, stats.SyncMode);
            Assert.Equal(ConflictDirection.PreferLocal, stats.ConflictDirection);

            branch.Dispose();
        }

        // ===== ResilientTrunk - Retry Tests =====

        [Fact]
        public void ResilientTrunk_WithResilience_CreatesWrapper()
        {
            // Arrange
            var trunk = new MemoryTrunk<TestUser>();

            // Act
            var resilientTrunk = trunk.WithResilience();

            // Assert
            Assert.NotNull(resilientTrunk);
            var caps = resilientTrunk.GetCapabilities();
            Assert.Contains("Resilient", caps.TrunkType);

            resilientTrunk.Dispose();
        }

        [Fact]
        public void ResilientTrunk_SuccessfulOperation_NoRetry()
        {
            // Arrange
            var trunk = new MemoryTrunk<TestUser>();
            var resilientTrunk = trunk.WithResilience(new ResilienceOptions
            {
                MaxRetries = 3,
                EnableCircuitBreaker = false
            });

            var tree = new Tree<TestUser>(resilientTrunk);

            // Act
            tree.Stash("user1", new TestUser { Name = "Alice", Email = "alice@example.com" });
            var result = tree.Crack("user1");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Alice", result.Name);

            var stats = resilientTrunk.GetStats();
            Assert.Equal(0, stats.TotalRetries);
            Assert.True(stats.IsHealthy);

            resilientTrunk.Dispose();
        }

        [Fact]
        public void ResilientTrunk_WithFallback_CreatesWrapper()
        {
            // Arrange
            var primaryTrunk = new MemoryTrunk<TestUser>();
            var fallbackTrunk = new MemoryTrunk<TestUser>();

            // Act
            var resilientTrunk = primaryTrunk.WithFallback(fallbackTrunk);

            // Assert
            Assert.NotNull(resilientTrunk);
            var caps = resilientTrunk.GetCapabilities();
            Assert.Contains("Fallback", caps.TrunkType);

            resilientTrunk.Dispose();
        }

        [Fact]
        public void ResilientTrunk_PreConfiguredStrategies_AllWork()
        {
            // Arrange
            var trunk1 = new MemoryTrunk<TestUser>();
            var trunk2 = new MemoryTrunk<TestUser>();
            var trunk3 = new MemoryTrunk<TestUser>();

            // Act & Assert - Aggressive
            var aggressive = trunk1.WithAggressiveRetry();
            Assert.NotNull(aggressive);
            Assert.Contains("Resilient", aggressive.GetCapabilities().TrunkType);
            aggressive.Dispose();

            // Act & Assert - Conservative
            var conservative = trunk2.WithConservativeRetry();
            Assert.NotNull(conservative);
            Assert.Contains("Resilient", conservative.GetCapabilities().TrunkType);
            conservative.Dispose();

            // Act & Assert - Circuit Breaker Only
            var circuitOnly = trunk3.WithCircuitBreaker();
            Assert.NotNull(circuitOnly);
            Assert.Contains("Resilient", circuitOnly.GetCapabilities().TrunkType);
            circuitOnly.Dispose();
        }

        [Fact]
        public void ResilientTrunk_StatisticsTracking_Works()
        {
            // Arrange
            var trunk = new MemoryTrunk<TestUser>();
            var resilientTrunk = trunk.WithResilience(new ResilienceOptions
            {
                MaxRetries = 2,
                EnableCircuitBreaker = true,
                CircuitBreakerThreshold = 5
            });

            var tree = new Tree<TestUser>(resilientTrunk);

            // Act
            tree.Stash("user1", new TestUser { Name = "Alice", Email = "alice@example.com" });
            tree.Stash("user2", new TestUser { Name = "Bob", Email = "bob@example.com" });
            var user1 = tree.Crack("user1");
            var user2 = tree.Crack("user2");

            // Assert
            var stats = resilientTrunk.GetStats();
            Assert.Equal(CircuitBreakerState.Closed, stats.CircuitState);
            Assert.True(stats.IsHealthy);
            Assert.Equal(0, stats.TotalRetries);
            Assert.Equal(0, stats.TotalFallbacks);
            Assert.Equal(0, stats.CircuitBreakerTrips);

            resilientTrunk.Dispose();
        }

        [Fact]
        public void ResilientTrunk_ResetCircuitBreaker_ResetsState()
        {
            // Arrange
            var trunk = new MemoryTrunk<TestUser>();
            var resilientTrunk = trunk.WithResilience(new ResilienceOptions
            {
                EnableCircuitBreaker = true
            });

            // Act
            resilientTrunk.ResetCircuitBreaker();

            // Assert
            var stats = resilientTrunk.GetStats();
            Assert.Equal(CircuitBreakerState.Closed, stats.CircuitState);
            Assert.Equal(0, stats.FailureCount);

            resilientTrunk.Dispose();
        }

        // ===== Circuit Breaker Tests =====

        [Fact]
        public void CircuitBreaker_DefaultState_Closed()
        {
            // Arrange
            var trunk = new MemoryTrunk<TestUser>();
            var resilientTrunk = trunk.WithResilience(new ResilienceOptions
            {
                EnableCircuitBreaker = true
            });

            // Act
            var stats = resilientTrunk.GetStats();

            // Assert
            Assert.Equal(CircuitBreakerState.Closed, stats.CircuitState);
            Assert.True(stats.IsHealthy);

            resilientTrunk.Dispose();
        }

        // ===== Metrics Collection Tests =====

        [Fact]
        public void MetricsCollector_Singleton_ReturnsSameInstance()
        {
            // Arrange & Act
            var instance1 = MetricsCollector.Instance;
            var instance2 = MetricsCollector.Instance;

            // Assert
            Assert.Same(instance1, instance2);
        }

        [Fact]
        public void MetricsCollector_RecordStash_Increments()
        {
            // Arrange
            var collector = MetricsCollector.Instance;
            collector.Reset(); // Clear any previous data

            // Act
            collector.RecordStash("TestTree", 1.5);
            collector.RecordStash("TestTree", 2.0);
            collector.RecordStash("TestTree", 1.8);

            // Assert - Can't directly access counters but can export and verify
            var prometheus = collector.ExportPrometheus();
            Assert.Contains("acorndb_stash_total", prometheus);
        }

        [Fact]
        public void MetricsCollector_RecordCrack_TracksHitsAndMisses()
        {
            // Arrange
            var collector = MetricsCollector.Instance;
            collector.Reset();

            // Act
            collector.RecordCrack("TestTree", 0.5, cacheHit: true);
            collector.RecordCrack("TestTree", 1.5, cacheHit: true);
            collector.RecordCrack("TestTree", 2.0, cacheHit: false);

            // Assert
            var prometheus = collector.ExportPrometheus();
            Assert.Contains("acorndb_crack_total", prometheus);
            Assert.Contains("acorndb_cache_hit_total", prometheus);
            Assert.Contains("acorndb_cache_miss_total", prometheus);
        }

        [Fact]
        public void MetricsCollector_AddLabel_AppearsInExport()
        {
            // Arrange
            var collector = MetricsCollector.Instance;
            collector.Reset();

            // Act
            collector.AddLabel("test_env", "unittest");
            var prometheus = collector.ExportPrometheus();

            // Assert
            Assert.Contains("test_env=\"unittest\"", prometheus);
        }

        [Fact]
        public void MetricsCollector_ExportPrometheus_ValidFormat()
        {
            // Arrange
            var collector = MetricsCollector.Instance;
            collector.Reset();
            collector.RecordStash("TestTree", 1.0);

            // Act
            var prometheus = collector.ExportPrometheus();

            // Assert
            Assert.Contains("# HELP", prometheus);
            Assert.Contains("# TYPE", prometheus);
            Assert.Contains("acorndb_", prometheus);
        }

        [Fact]
        public void MetricsCollector_ExportJson_ValidFormat()
        {
            // Arrange
            var collector = MetricsCollector.Instance;
            collector.Reset();
            collector.RecordStash("TestTree", 1.0);

            // Act
            var json = collector.ExportJson();

            // Assert
            Assert.Contains("\"timestamp\"", json);
            Assert.Contains("\"counters\"", json);
            Assert.Contains("\"gauges\"", json);
            Assert.Contains("\"stash_total\"", json);
        }

        [Fact]
        public void MetricsCollector_RecordMultipleOperations_AllTracked()
        {
            // Arrange
            var collector = MetricsCollector.Instance;
            collector.Reset();

            // Act
            collector.RecordStash("Tree1", 1.0);
            collector.RecordCrack("Tree1", 0.5, true);
            collector.RecordToss("Tree1", 0.8);
            collector.RecordSquabble("Tree1");
            collector.RecordPush("Branch1", 5);
            collector.RecordPull("Branch1", 3);
            collector.RecordConflict("Tree1");

            // Assert
            var prometheus = collector.ExportPrometheus();
            Assert.Contains("acorndb_stash_total", prometheus);
            Assert.Contains("acorndb_crack_total", prometheus);
            Assert.Contains("acorndb_toss_total", prometheus);
            Assert.Contains("acorndb_squabble_total", prometheus);
            Assert.Contains("acorndb_sync_push_total", prometheus);
            Assert.Contains("acorndb_sync_pull_total", prometheus);
            Assert.Contains("acorndb_sync_conflict_total", prometheus);
        }

        [Fact]
        public void MetricsCollector_ResilienceMetrics_Tracked()
        {
            // Arrange
            var collector = MetricsCollector.Instance;
            collector.Reset();

            // Act
            collector.RecordRetry("TestTrunk");
            collector.RecordFallback("TestTrunk");
            collector.RecordCircuitBreakerTrip("TestTrunk");

            // Assert
            var prometheus = collector.ExportPrometheus();
            Assert.Contains("acorndb_retry_total", prometheus);
            Assert.Contains("acorndb_fallback_total", prometheus);
            Assert.Contains("acorndb_circuit_breaker_trip_total", prometheus);
        }

        // ===== MetricsConfiguration Tests =====

        [Fact]
        public void MetricsConfiguration_ConfigureLabels_AppliesLabels()
        {
            // Arrange
            var collector = MetricsCollector.Instance;
            collector.Reset();

            // Act
            MetricsConfiguration.ConfigureLabels(
                environment: "test",
                region: "us-test-1",
                instance: "test-instance"
            );

            var prometheus = collector.ExportPrometheus();

            // Assert
            Assert.Contains("environment=\"test\"", prometheus);
            Assert.Contains("region=\"us-test-1\"", prometheus);
            Assert.Contains("instance=\"test-instance\"", prometheus);
        }

        // ===== MetricsServer Tests =====

        [Fact]
        public void MetricsServer_Create_Success()
        {
            // Arrange & Act
            var server = new MetricsServer(port: 9091);

            // Assert
            Assert.NotNull(server);

            server.Dispose();
        }

        [Fact]
        public void MetricsServer_Dispose_DoesNotThrow()
        {
            // Arrange
            var server = new MetricsServer(port: 9092);

            // Act & Assert
            var exception = Record.Exception(() => server.Dispose());
            Assert.Null(exception);
        }

        // ===== Integration Tests =====

        [Fact]
        public void Integration_ResilientTrunkWithMetrics_Works()
        {
            // Arrange
            var collector = MetricsCollector.Instance;
            collector.Reset();

            var trunk = new MemoryTrunk<TestUser>();
            var resilientTrunk = trunk.WithResilience(new ResilienceOptions
            {
                MaxRetries = 2,
                EnableCircuitBreaker = false
            });

            var tree = new Tree<TestUser>(resilientTrunk);

            // Act
            tree.Stash("user1", new TestUser { Name = "Alice", Email = "alice@example.com" });
            collector.RecordStash("TestTree", 1.0);

            var user = tree.Crack("user1");
            collector.RecordCrack("TestTree", 0.5, user != null);

            // Assert
            Assert.NotNull(user);
            Assert.Equal("Alice", user.Name);

            var stats = resilientTrunk.GetStats();
            Assert.Equal(0, stats.TotalRetries);
            Assert.True(stats.IsHealthy);

            var prometheus = collector.ExportPrometheus();
            Assert.Contains("acorndb_stash_total", prometheus);
            Assert.Contains("acorndb_crack_total", prometheus);

            resilientTrunk.Dispose();
        }

        [Fact]
        public void Integration_AllProductionFeatures_Work()
        {
            // Arrange
            var collector = MetricsCollector.Instance;
            collector.Reset();

            MetricsConfiguration.ConfigureLabels(
                environment: "integration-test",
                region: "test-region",
                instance: "test-1"
            );

            var primaryTrunk = new MemoryTrunk<TestUser>();
            var fallbackTrunk = new MemoryTrunk<TestUser>();
            var resilientTrunk = primaryTrunk.WithFallback(
                fallbackTrunk,
                ResilienceOptions.Default
            );

            var tree = new Tree<TestUser>(resilientTrunk);

            var branch = new Branch("http://localhost:5000")
                .WithBatching(batchSize: 5, batchTimeoutMs: 100)
                .WithSyncMode(SyncMode.PushOnly);

            // Act
            tree.Stash("user1", new TestUser { Name = "Test User", Email = "test@example.com" });
            collector.RecordStash("IntegrationTree", 1.0);

            var user = tree.Crack("user1");
            collector.RecordCrack("IntegrationTree", 0.5, user != null);

            // Assert
            Assert.NotNull(user);
            Assert.Equal("Test User", user.Name);

            var resStats = resilientTrunk.GetStats();
            Assert.Equal(CircuitBreakerState.Closed, resStats.CircuitState);
            Assert.True(resStats.IsHealthy);

            var branchStats = branch.GetStats();
            Assert.Equal("http://localhost:5000", branchStats.RemoteUrl);
            Assert.Equal(SyncMode.PushOnly, branchStats.SyncMode);

            var prometheus = collector.ExportPrometheus();
            Assert.Contains("environment=\"integration-test\"", prometheus);
            Assert.Contains("acorndb_stash_total", prometheus);
            Assert.Contains("acorndb_crack_total", prometheus);

            // Cleanup
            resilientTrunk.Dispose();
            branch.Dispose();
        }

        // ===== Helper Classes =====

        private class TestUser
        {
            public string Name { get; set; } = "";
            public string Email { get; set; } = "";
        }
    }
}
