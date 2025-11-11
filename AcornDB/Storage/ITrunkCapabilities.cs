namespace AcornDB.Storage
{
    /// <summary>
    /// Describes the capabilities of a trunk implementation
    /// Allows runtime discovery of what features a trunk supports
    /// </summary>
    public interface ITrunkCapabilities
    {
        /// <summary>
        /// Whether this trunk supports retrieving historical versions of nuts
        /// </summary>
        bool SupportsHistory { get; }

        /// <summary>
        /// Whether this trunk can be used for synchronization (export/import changes)
        /// </summary>
        bool SupportsSync { get; }

        /// <summary>
        /// Whether this trunk persists data durably (vs in-memory only)
        /// </summary>
        bool IsDurable { get; }

        /// <summary>
        /// Whether this trunk supports async operations
        /// </summary>
        bool SupportsAsync { get; }

        /// <summary>
        /// Whether this trunk supports native secondary indexes.
        /// When true, the trunk can create and maintain its own indexes (e.g., SQL CREATE INDEX).
        /// When false, managed indexes via ManagedIndexRoot will be used.
        /// </summary>
        bool SupportsNativeIndexes { get; }

        /// <summary>
        /// Whether this trunk supports native full-text search.
        /// When true, the trunk can perform FTS operations natively (e.g., SQLite FTS5, PostgreSQL tsvector).
        /// When false, managed text indexing will be used.
        /// </summary>
        bool SupportsFullTextSearch { get; }

        /// <summary>
        /// Whether this trunk supports native computed/expression indexes.
        /// When true, the trunk can create indexes on expressions (e.g., PostgreSQL expression indexes).
        /// When false, computed indexes will be materialized in managed index.
        /// </summary>
        bool SupportsComputedIndexes { get; }

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
        /// Get capabilities for a trunk (backward compatibility helper)
        /// </summary>
        public static ITrunkCapabilities GetCapabilities<T>(this ITrunk<T> trunk)
        {
            return trunk.Capabilities;
        }

        /// <summary>
        /// Check if history is supported without throwing exceptions
        /// </summary>
        public static bool CanGetHistory<T>(this ITrunk<T> trunk)
        {
            return trunk.Capabilities.SupportsHistory;
        }

        /// <summary>
        /// Check if sync is supported without throwing exceptions
        /// </summary>
        public static bool CanSync<T>(this ITrunk<T> trunk)
        {
            return trunk.Capabilities.SupportsSync;
        }
    }
}
