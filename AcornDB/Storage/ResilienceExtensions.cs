using System;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Storage
{
    /// <summary>
    /// Extension methods for adding resilience (retry logic, fallback, circuit breaker) to trunks
    /// </summary>
    public static class ResilienceExtensions
    {
        /// <summary>
        /// Wrap trunk with retry logic and circuit breaker
        /// </summary>
        /// <param name="trunk">Trunk to make resilient</param>
        /// <param name="options">Resilience options (retries, circuit breaker, etc.)</param>
        /// <returns>Resilient trunk wrapper</returns>
        public static ResilientTrunk<T> WithResilience<T>(
            this ITrunk<T> trunk,
            ResilienceOptions? options = null)
        {
            return new ResilientTrunk<T>(trunk, fallbackTrunk: null, options);
        }

        /// <summary>
        /// Wrap trunk with fallback to another trunk if primary fails
        /// </summary>
        /// <param name="trunk">Primary trunk</param>
        /// <param name="fallbackTrunk">Fallback trunk to use if primary fails</param>
        /// <param name="options">Resilience options</param>
        /// <returns>Resilient trunk with fallback</returns>
        public static ResilientTrunk<T> WithFallback<T>(
            this ITrunk<T> trunk,
            ITrunk<T> fallbackTrunk,
            ResilienceOptions? options = null)
        {
            return new ResilientTrunk<T>(trunk, fallbackTrunk, options);
        }

        /// <summary>
        /// Wrap trunk with aggressive retry strategy (5 retries, fast backoff)
        /// Best for: Highly reliable networks, temporary glitches
        /// </summary>
        public static ResilientTrunk<T> WithAggressiveRetry<T>(this ITrunk<T> trunk)
        {
            return new ResilientTrunk<T>(trunk, fallbackTrunk: null, ResilienceOptions.Aggressive);
        }

        /// <summary>
        /// Wrap trunk with conservative retry strategy (2 retries, slow backoff, quick circuit break)
        /// Best for: Unreliable networks, expensive operations
        /// </summary>
        public static ResilientTrunk<T> WithConservativeRetry<T>(this ITrunk<T> trunk)
        {
            return new ResilientTrunk<T>(trunk, fallbackTrunk: null, ResilienceOptions.Conservative);
        }

        /// <summary>
        /// Wrap trunk with circuit breaker only (no retries)
        /// Best for: Fast failure detection, quick fallback
        /// </summary>
        public static ResilientTrunk<T> WithCircuitBreaker<T>(
            this ITrunk<T> trunk,
            ITrunk<T>? fallbackTrunk = null)
        {
            return new ResilientTrunk<T>(trunk, fallbackTrunk, ResilienceOptions.CircuitBreakerOnly);
        }
    }
}
