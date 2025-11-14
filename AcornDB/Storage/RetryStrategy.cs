namespace AcornDB.Storage
{
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
}
