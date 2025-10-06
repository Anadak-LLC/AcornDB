namespace AcornDB.Storage
{
    /// <summary>
    /// Capabilities interface for feature detection on trunks.
    /// Allows runtime checking of trunk features without try/catch.
    /// </summary>
    public interface ITrunkCapabilities
    {
        /// <summary>
        /// Whether this trunk supports versioning and GetHistory()
        /// </summary>
        bool SupportsHistory { get; }

        /// <summary>
        /// Whether this trunk supports ExportChanges() and ImportChanges()
        /// </summary>
        bool SupportsSync { get; }

        /// <summary>
        /// Whether this trunk persists data to disk/cloud
        /// </summary>
        bool IsDurable { get; }

        /// <summary>
        /// Whether this trunk supports async operations
        /// </summary>
        bool SupportsAsync { get; }

        /// <summary>
        /// Human-readable name of the trunk type
        /// </summary>
        string TrunkType { get; }
    }

    /// <summary>
    /// Extension methods for ITrunk capability detection
    /// </summary>
    public static class TrunkCapabilitiesExtensions
    {
        /// <summary>
        /// Get capabilities for a trunk (returns default if not explicitly implemented)
        /// </summary>
        public static ITrunkCapabilities GetCapabilities<T>(this ITrunk<T> trunk)
        {
            if (trunk is ITrunkCapabilities caps)
            {
                return caps;
            }

            // Return inferred capabilities based on trunk type
            return trunk switch
            {
                DocumentStoreTrunk<T> => new TrunkCapabilities
                {
                    SupportsHistory = true,
                    SupportsSync = true,
                    IsDurable = true,
                    SupportsAsync = false,
                    TrunkType = "DocumentStoreTrunk"
                },
                FileTrunk<T> => new TrunkCapabilities
                {
                    SupportsHistory = false,
                    SupportsSync = true,
                    IsDurable = true,
                    SupportsAsync = false,
                    TrunkType = "FileTrunk"
                },
                MemoryTrunk<T> => new TrunkCapabilities
                {
                    SupportsHistory = false,
                    SupportsSync = true,
                    IsDurable = false,
                    SupportsAsync = false,
                    TrunkType = "MemoryTrunk"
                },
                AzureTrunk<T> => new TrunkCapabilities
                {
                    SupportsHistory = false,
                    SupportsSync = true,
                    IsDurable = true,
                    SupportsAsync = true,
                    TrunkType = "AzureTrunk"
                },
                _ => new TrunkCapabilities
                {
                    SupportsHistory = false,
                    SupportsSync = true,
                    IsDurable = false,
                    SupportsAsync = false,
                    TrunkType = trunk.GetType().Name
                }
            };
        }

        /// <summary>
        /// Check if history is supported without throwing exceptions
        /// </summary>
        public static bool CanGetHistory<T>(this ITrunk<T> trunk)
        {
            return trunk.GetCapabilities().SupportsHistory;
        }

        /// <summary>
        /// Check if sync is supported without throwing exceptions
        /// </summary>
        public static bool CanSync<T>(this ITrunk<T> trunk)
        {
            return trunk.GetCapabilities().SupportsSync;
        }
    }

    /// <summary>
    /// Default implementation of trunk capabilities
    /// </summary>
    internal class TrunkCapabilities : ITrunkCapabilities
    {
        public bool SupportsHistory { get; init; }
        public bool SupportsSync { get; init; }
        public bool IsDurable { get; init; }
        public bool SupportsAsync { get; init; }
        public string TrunkType { get; init; } = "Unknown";
    }
}
