namespace AcornDB.Storage
{
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
}
