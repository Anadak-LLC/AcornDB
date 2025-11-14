namespace AcornDB.Storage
{
    /// <summary>
    /// Near/far cache statistics
    /// </summary>
    public class NearFarStats
    {
        public int NearCacheCount { get; set; }
        public int FarCacheCount { get; set; }
        public int BackingStoreCount { get; set; }
    }
}
