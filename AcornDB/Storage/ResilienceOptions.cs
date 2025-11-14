using System;

namespace AcornDB.Storage
{
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
}
