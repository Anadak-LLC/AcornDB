namespace AcornDB.Storage
{
    /// <summary>
    /// Default implementation of trunk capabilities
    /// </summary>
    public class TrunkCapabilities : ITrunkCapabilities
    {
        public bool SupportsHistory { get; init; }
        public bool SupportsSync { get; init; }
        public bool IsDurable { get; init; }
        public bool SupportsAsync { get; init; }
        public bool SupportsNativeIndexes { get; init; }
        public bool SupportsFullTextSearch { get; init; }
        public bool SupportsComputedIndexes { get; init; }
        public string TrunkType { get; init; } = "Unknown";
    }
}
