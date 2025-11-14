namespace AcornDB.Storage
{
    /// <summary>
    /// Cache statistics
    /// </summary>
    public class CacheStats
    {
        public int CachedItemCount { get; set; }
        public int ExpiredItemCount { get; set; }
        public int ActiveItemCount { get; set; }
    }
}
