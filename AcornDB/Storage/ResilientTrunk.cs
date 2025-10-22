using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Storage
{
    /// <summary>
    /// Resilient trunk wrapper providing retry logic, fallback capabilities, and circuit breaker pattern.
    ///
    /// Features:
    /// - Automatic retry with exponential backoff on transient failures
    /// - Fallback to secondary trunk if primary fails
    /// - Circuit breaker to prevent cascading failures
    /// - Health monitoring and automatic recovery
    ///
    /// Use Cases:
    /// - High-availability applications requiring fault tolerance
    /// - Cloud storage with network reliability issues
    /// - Multi-region deployments with failover
    /// - Gradual degradation instead of complete failure
    /// </summary>
    /// <typeparam name="T">Type of objects stored in trunk</typeparam>
    public class ResilientTrunk<T> : ITrunk<T>, IDisposable
    {
        private readonly ITrunk<T> _primaryTrunk;
        private readonly ITrunk<T>? _fallbackTrunk;
        private readonly ResilienceOptions _options;
        private bool _disposed;

        // Circuit breaker state
        private CircuitBreakerState _circuitState = CircuitBreakerState.Closed;
        private int _failureCount = 0;
        private DateTime _circuitOpenedAt = DateTime.MinValue;
        private DateTime _lastFailureTime = DateTime.MinValue;

        // Statistics
        private long _totalRetries = 0;
        private long _totalFallbacks = 0;
        private long _circuitBreakerTrips = 0;

        /// <summary>
        /// Create resilient trunk with retry logic and optional fallback
        /// </summary>
        /// <param name="primaryTrunk">Primary trunk to use</param>
        /// <param name="fallbackTrunk">Optional fallback trunk if primary fails</param>
        /// <param name="options">Resilience configuration options</param>
        public ResilientTrunk(
            ITrunk<T> primaryTrunk,
            ITrunk<T>? fallbackTrunk = null,
            ResilienceOptions? options = null)
        {
            _primaryTrunk = primaryTrunk ?? throw new ArgumentNullException(nameof(primaryTrunk));
            _fallbackTrunk = fallbackTrunk;
            _options = options ?? ResilienceOptions.Default;

            // Safely access capabilities with null check
            try
            {
                var primaryCaps = _primaryTrunk.Capabilities;
                Console.WriteLine($"🛡️ ResilientTrunk initialized:");
                Console.WriteLine($"   Primary: {primaryCaps?.TrunkType ?? "Unknown"}");
                if (_fallbackTrunk != null)
                {
                    var fallbackCaps = _fallbackTrunk.Capabilities;
                    Console.WriteLine($"   Fallback: {fallbackCaps?.TrunkType ?? "Unknown"}");
                }
                Console.WriteLine($"   Max Retries: {_options.MaxRetries}");
                Console.WriteLine($"   Circuit Breaker: {(_options.EnableCircuitBreaker ? "Enabled" : "Disabled")}");
            }
            catch (Exception ex)
            {
                // If capabilities check fails, just log and continue
                Console.WriteLine($"🛡️ ResilientTrunk initialized (capabilities unavailable: {ex.Message})");
            }
        }

        public void Save(string id, Nut<T> nut)
        {
            ExecuteWithResilience(
                () => _primaryTrunk.Save(id, nut),
                () => _fallbackTrunk?.Save(id, nut),
                "Save"
            );
        }

        public Nut<T>? Load(string id)
        {
            return ExecuteWithResilience(
                () => _primaryTrunk.Load(id),
                () => _fallbackTrunk?.Load(id),
                "Load"
            );
        }

        public void Delete(string id)
        {
            ExecuteWithResilience(
                () => _primaryTrunk.Delete(id),
                () => _fallbackTrunk?.Delete(id),
                "Delete"
            );
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            return ExecuteWithResilience(
                () => _primaryTrunk.LoadAll(),
                () => _fallbackTrunk?.LoadAll() ?? Enumerable.Empty<Nut<T>>(),
                "LoadAll"
            );
        }

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            return ExecuteWithResilience(
                () => _primaryTrunk.GetHistory(id),
                () => _fallbackTrunk?.GetHistory(id) ?? Array.Empty<Nut<T>>(),
                "GetHistory"
            );
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return ExecuteWithResilience(
                () => _primaryTrunk.ExportChanges(),
                () => _fallbackTrunk?.ExportChanges() ?? Enumerable.Empty<Nut<T>>(),
                "ExportChanges"
            );
        }

        public void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            ExecuteWithResilience(
                () => _primaryTrunk.ImportChanges(incoming),
                () => _fallbackTrunk?.ImportChanges(incoming),
                "ImportChanges"
            );
        }

        /// <summary>
        /// Execute operation with retry logic, fallback, and circuit breaker
        /// </summary>
        private void ExecuteWithResilience(
            Action primaryOperation,
            Action? fallbackOperation,
            string operationName)
        {
            ExecuteWithResilience<object?>(
                () => { primaryOperation(); return null; },
                fallbackOperation != null ? () => { fallbackOperation(); return null; } : null,
                operationName
            );
        }

        /// <summary>
        /// Execute operation with retry logic, fallback, and circuit breaker (with return value)
        /// </summary>
        private TResult ExecuteWithResilience<TResult>(
            Func<TResult> primaryOperation,
            Func<TResult>? fallbackOperation,
            string operationName)
        {
            // Check circuit breaker state
            if (_options.EnableCircuitBreaker && _circuitState == CircuitBreakerState.Open)
            {
                // Check if circuit should transition to half-open
                if (DateTime.UtcNow - _circuitOpenedAt >= _options.CircuitBreakerTimeout)
                {
                    _circuitState = CircuitBreakerState.HalfOpen;
                    Console.WriteLine($"🔄 Circuit breaker transitioning to Half-Open state");
                }
                else
                {
                    // Circuit is open, use fallback immediately
                    if (fallbackOperation != null)
                    {
                        Console.WriteLine($"⚡ Circuit OPEN - using fallback for {operationName}");
                        _totalFallbacks++;
                        return fallbackOperation();
                    }
                    throw new InvalidOperationException($"Circuit breaker is OPEN and no fallback available for {operationName}");
                }
            }

            // Try primary trunk with retry logic
            Exception? lastException = null;
            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                try
                {
                    var result = primaryOperation();

                    // Success - reset circuit breaker if in half-open state
                    if (_circuitState == CircuitBreakerState.HalfOpen)
                    {
                        _circuitState = CircuitBreakerState.Closed;
                        _failureCount = 0;
                        Console.WriteLine($"✅ Circuit breaker CLOSED after successful operation");
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _lastFailureTime = DateTime.UtcNow;

                    // Check if exception is retryable
                    if (!IsRetryableException(ex))
                    {
                        // Non-retryable exception - fail immediately or use fallback
                        break;
                    }

                    // Don't retry on last attempt
                    if (attempt < _options.MaxRetries)
                    {
                        _totalRetries++;
                        var delay = CalculateRetryDelay(attempt);
                        Console.WriteLine($"⚠️ {operationName} failed (attempt {attempt + 1}/{_options.MaxRetries + 1}), retrying in {delay}ms: {ex.Message}");
                        System.Threading.Thread.Sleep(delay);
                    }
                }
            }

            // All retries exhausted - update circuit breaker
            if (_options.EnableCircuitBreaker)
            {
                _failureCount++;
                if (_failureCount >= _options.CircuitBreakerThreshold)
                {
                    _circuitState = CircuitBreakerState.Open;
                    _circuitOpenedAt = DateTime.UtcNow;
                    _circuitBreakerTrips++;
                    Console.WriteLine($"🔴 Circuit breaker OPENED after {_failureCount} failures");
                }
            }

            // Try fallback trunk if available
            if (fallbackOperation != null)
            {
                try
                {
                    Console.WriteLine($"🔄 Primary trunk failed, using fallback for {operationName}");
                    _totalFallbacks++;
                    return fallbackOperation();
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"❌ Fallback trunk also failed for {operationName}: {fallbackEx.Message}");
                    throw new AggregateException(
                        $"Both primary and fallback trunks failed for {operationName}",
                        lastException!,
                        fallbackEx
                    );
                }
            }

            // No fallback available - throw original exception
            throw lastException ?? new InvalidOperationException($"{operationName} failed");
        }

        /// <summary>
        /// Determine if an exception is retryable (transient failure vs permanent error)
        /// </summary>
        private bool IsRetryableException(Exception ex)
        {
            // Network-related exceptions are typically retryable
            if (ex is System.Net.Http.HttpRequestException ||
                ex is System.Net.Sockets.SocketException ||
                ex is TimeoutException ||
                ex is System.IO.IOException)
            {
                return true;
            }

            // Check for specific retryable error messages
            var message = ex.Message.ToLowerInvariant();
            if (message.Contains("timeout") ||
                message.Contains("connection") ||
                message.Contains("network") ||
                message.Contains("unavailable") ||
                message.Contains("throttl"))
            {
                return true;
            }

            // ArgumentException, InvalidOperationException, etc. are typically NOT retryable
            if (ex is ArgumentException ||
                ex is ArgumentNullException ||
                ex is InvalidOperationException ||
                ex is NotSupportedException)
            {
                return false;
            }

            // Default: retry on unknown exceptions (conservative approach)
            return _options.RetryOnUnknownExceptions;
        }

        /// <summary>
        /// Calculate retry delay with exponential backoff
        /// </summary>
        private int CalculateRetryDelay(int attemptNumber)
        {
            if (_options.RetryStrategy == RetryStrategy.Fixed)
            {
                return _options.BaseRetryDelayMs;
            }

            // Exponential backoff: delay = base * 2^attempt
            var delay = _options.BaseRetryDelayMs * Math.Pow(2, attemptNumber);

            // Apply jitter to prevent thundering herd
            if (_options.UseJitter)
            {
                var random = new Random();
                var jitter = random.Next(0, (int)(delay * 0.3)); // ±30% jitter
                delay += jitter;
            }

            // Cap at maximum delay
            return Math.Min((int)delay, _options.MaxRetryDelayMs);
        }

        /// <summary>
        /// Get resilience statistics
        /// </summary>
        public ResilienceStats GetStats()
        {
            return new ResilienceStats
            {
                CircuitState = _circuitState,
                FailureCount = _failureCount,
                TotalRetries = _totalRetries,
                TotalFallbacks = _totalFallbacks,
                CircuitBreakerTrips = _circuitBreakerTrips,
                LastFailureTime = _lastFailureTime,
                IsHealthy = _circuitState != CircuitBreakerState.Open
            };
        }

        /// <summary>
        /// Manually reset circuit breaker to closed state
        /// </summary>
        public void ResetCircuitBreaker()
        {
            _circuitState = CircuitBreakerState.Closed;
            _failureCount = 0;
            _circuitOpenedAt = DateTime.MinValue;
            Console.WriteLine($"🔄 Circuit breaker manually reset to CLOSED");
        }

        // ITrunkCapabilities implementation - forward to primary trunk with custom TrunkType
        public ITrunkCapabilities Capabilities
        {
            get
            {
                try
                {
                    var primaryCaps = _primaryTrunk.Capabilities;
                    var fallbackInfo = "";

                    if (_fallbackTrunk != null)
                    {
                        try
                        {
                            fallbackInfo = $"+Fallback({_fallbackTrunk.Capabilities?.TrunkType ?? "Unknown"})";
                        }
                        catch
                        {
                            fallbackInfo = "+Fallback(Unknown)";
                        }
                    }

                    return new TrunkCapabilities
                    {
                        SupportsHistory = primaryCaps?.SupportsHistory ?? false,
                        SupportsSync = true,
                        IsDurable = primaryCaps?.IsDurable ?? false,
                        SupportsAsync = primaryCaps?.SupportsAsync ?? false,
                        TrunkType = $"ResilientTrunk({primaryCaps?.TrunkType ?? "Unknown"}{fallbackInfo})"
                    };
                }
                catch (Exception)
                {
                    // Fallback if primary capabilities fail
                    return new TrunkCapabilities
                    {
                        SupportsHistory = false,
                        SupportsSync = true,
                        IsDurable = false,
                        SupportsAsync = false,
                        TrunkType = "ResilientTrunk(Unknown)"
                    };
                }
            }
        }

        // IRoot interface members - forward to primary trunk
        public IReadOnlyList<IRoot> Roots => _primaryTrunk.Roots;
        public void AddRoot(IRoot root) => _primaryTrunk.AddRoot(root);
        public bool RemoveRoot(string name) => _primaryTrunk.RemoveRoot(name);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_primaryTrunk is IDisposable primaryDisposable)
                primaryDisposable.Dispose();

            if (_fallbackTrunk is IDisposable fallbackDisposable)
                fallbackDisposable.Dispose();
        }
    }

    /// <summary>
    /// Configuration options for ResilientTrunk
    /// </summary>
    public class ResilienceOptions
    {
        /// <summary>
        /// Maximum number of retry attempts (default: 3)
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Base retry delay in milliseconds (default: 100ms)
        /// </summary>
        public int BaseRetryDelayMs { get; set; } = 100;

        /// <summary>
        /// Maximum retry delay in milliseconds (default: 5000ms)
        /// </summary>
        public int MaxRetryDelayMs { get; set; } = 5000;

        /// <summary>
        /// Retry strategy (Fixed or ExponentialBackoff)
        /// </summary>
        public RetryStrategy RetryStrategy { get; set; } = RetryStrategy.ExponentialBackoff;

        /// <summary>
        /// Add random jitter to retry delays (default: true)
        /// Prevents thundering herd problem
        /// </summary>
        public bool UseJitter { get; set; } = true;

        /// <summary>
        /// Retry on unknown exceptions (default: true)
        /// When false, only retries known transient failures
        /// </summary>
        public bool RetryOnUnknownExceptions { get; set; } = true;

        /// <summary>
        /// Enable circuit breaker pattern (default: true)
        /// </summary>
        public bool EnableCircuitBreaker { get; set; } = true;

        /// <summary>
        /// Number of failures before opening circuit (default: 5)
        /// </summary>
        public int CircuitBreakerThreshold { get; set; } = 5;

        /// <summary>
        /// Time to keep circuit open before attempting recovery (default: 30 seconds)
        /// </summary>
        public TimeSpan CircuitBreakerTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Default resilience options (3 retries, exponential backoff, circuit breaker enabled)
        /// </summary>
        public static ResilienceOptions Default => new ResilienceOptions();

        /// <summary>
        /// Aggressive retry strategy (5 retries, faster backoff)
        /// Best for: Highly reliable networks, temporary glitches
        /// </summary>
        public static ResilienceOptions Aggressive => new ResilienceOptions
        {
            MaxRetries = 5,
            BaseRetryDelayMs = 50,
            MaxRetryDelayMs = 2000,
            CircuitBreakerThreshold = 10
        };

        /// <summary>
        /// Conservative retry strategy (2 retries, slower backoff, quick circuit break)
        /// Best for: Unreliable networks, expensive operations
        /// </summary>
        public static ResilienceOptions Conservative => new ResilienceOptions
        {
            MaxRetries = 2,
            BaseRetryDelayMs = 200,
            MaxRetryDelayMs = 10000,
            CircuitBreakerThreshold = 3,
            CircuitBreakerTimeout = TimeSpan.FromMinutes(1)
        };

        /// <summary>
        /// No retries, circuit breaker only
        /// Best for: Fast failure detection, quick fallback
        /// </summary>
        public static ResilienceOptions CircuitBreakerOnly => new ResilienceOptions
        {
            MaxRetries = 0,
            EnableCircuitBreaker = true,
            CircuitBreakerThreshold = 3,
            CircuitBreakerTimeout = TimeSpan.FromSeconds(10)
        };
    }

    /// <summary>
    /// Retry strategy for failed operations
    /// </summary>
    public enum RetryStrategy
    {
        /// <summary>
        /// Fixed delay between retries
        /// </summary>
        Fixed,

        /// <summary>
        /// Exponentially increasing delay (delay = base * 2^attempt)
        /// </summary>
        ExponentialBackoff
    }

    /// <summary>
    /// Circuit breaker states
    /// </summary>
    public enum CircuitBreakerState
    {
        /// <summary>
        /// Normal operation - requests flow through
        /// </summary>
        Closed,

        /// <summary>
        /// Too many failures - requests blocked, fallback used
        /// </summary>
        Open,

        /// <summary>
        /// Testing recovery - single request allowed to test primary
        /// </summary>
        HalfOpen
    }

    /// <summary>
    /// Statistics about resilient trunk health and operations
    /// </summary>
    public class ResilienceStats
    {
        public CircuitBreakerState CircuitState { get; set; }
        public int FailureCount { get; set; }
        public long TotalRetries { get; set; }
        public long TotalFallbacks { get; set; }
        public long CircuitBreakerTrips { get; set; }
        public DateTime LastFailureTime { get; set; }
        public bool IsHealthy { get; set; }
    }
}
