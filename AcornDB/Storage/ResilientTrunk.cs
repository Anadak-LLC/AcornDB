using System;
using AcornDB.Logging;
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
                AcornLog.Info($"🛡️ ResilientTrunk initialized:");
                AcornLog.Info($"   Primary: {primaryCaps?.TrunkType ?? "Unknown"}");
                if (_fallbackTrunk != null)
                {
                    var fallbackCaps = _fallbackTrunk.Capabilities;
                    AcornLog.Info($"   Fallback: {fallbackCaps?.TrunkType ?? "Unknown"}");
                }
                AcornLog.Info($"   Max Retries: {_options.MaxRetries}");
                AcornLog.Info($"   Circuit Breaker: {(_options.EnableCircuitBreaker ? "Enabled" : "Disabled")}");
            }
            catch (Exception ex)
            {
                // If capabilities check fails, just log and continue
                AcornLog.Info($"🛡️ ResilientTrunk initialized (capabilities unavailable: {ex.Message})");
            }
        }

        public void Stash(string id, Nut<T> nut)
        {
            ExecuteWithResilience(
                () => _primaryTrunk.Stash(id, nut),
                () => _fallbackTrunk?.Stash(id, nut),
                "Stash"
            );
        }

        [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
        public void Save(string id, Nut<T> nut) => Stash(id, nut);

        public Nut<T>? Crack(string id)
        {
            return ExecuteWithResilience(
                () => _primaryTrunk.Crack(id),
                () => _fallbackTrunk?.Crack(id),
                "Crack"
            );
        }

        [Obsolete("Use Crack() instead. This method will be removed in a future version.")]
        public Nut<T>? Load(string id) => Crack(id);

        public void Toss(string id)
        {
            ExecuteWithResilience(
                () => _primaryTrunk.Toss(id),
                () => _fallbackTrunk?.Toss(id),
                "Toss"
            );
        }

        [Obsolete("Use Toss() instead. This method will be removed in a future version.")]
        public void Delete(string id) => Toss(id);

        public IEnumerable<Nut<T>> CrackAll()
        {
            return ExecuteWithResilience(
                () => _primaryTrunk.CrackAll(),
                () => _fallbackTrunk?.CrackAll() ?? Enumerable.Empty<Nut<T>>(),
                "CrackAll"
            );
        }

        [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
        public IEnumerable<Nut<T>> LoadAll() => CrackAll();

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
                    AcornLog.Info($"🔄 Circuit breaker transitioning to Half-Open state");
                }
                else
                {
                    // Circuit is open, use fallback immediately
                    if (fallbackOperation != null)
                    {
                        AcornLog.Info($"⚡ Circuit OPEN - using fallback for {operationName}");
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
                        AcornLog.Info($"✅ Circuit breaker CLOSED after successful operation");
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
                        AcornLog.Info($"⚠️ {operationName} failed (attempt {attempt + 1}/{_options.MaxRetries + 1}), retrying in {delay}ms: {ex.Message}");
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
                    AcornLog.Info($"🔴 Circuit breaker OPENED after {_failureCount} failures");
                }
            }

            // Try fallback trunk if available
            if (fallbackOperation != null)
            {
                try
                {
                    AcornLog.Info($"🔄 Primary trunk failed, using fallback for {operationName}");
                    _totalFallbacks++;
                    return fallbackOperation();
                }
                catch (Exception fallbackEx)
                {
                    AcornLog.Info($"❌ Fallback trunk also failed for {operationName}: {fallbackEx.Message}");
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
            AcornLog.Info($"🔄 Circuit breaker manually reset to CLOSED");
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
}
