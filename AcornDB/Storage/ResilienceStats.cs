using System;

namespace AcornDB.Storage
{
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
