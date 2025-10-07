using System;
using System.Collections.Generic;

namespace AcornDB.Storage
{
    /// <summary>
    /// Factory interface for creating trunk instances
    /// Allows dynamic trunk creation from registry
    /// </summary>
    public interface ITrunkFactory<T>
    {
        /// <summary>
        /// Create a trunk instance with the given configuration
        /// </summary>
        /// <param name="configuration">Configuration dictionary (keys depend on trunk type)</param>
        /// <returns>Configured trunk instance</returns>
        ITrunk<T> Create(Dictionary<string, object> configuration);

        /// <summary>
        /// Get metadata about this trunk type
        /// </summary>
        TrunkMetadata GetMetadata();

        /// <summary>
        /// Validate configuration before creating trunk
        /// </summary>
        /// <param name="configuration">Configuration to validate</param>
        /// <returns>True if configuration is valid</returns>
        bool ValidateConfiguration(Dictionary<string, object> configuration);
    }

    /// <summary>
    /// Non-generic factory interface for type-agnostic registration
    /// </summary>
    public interface ITrunkFactory
    {
        /// <summary>
        /// Create a trunk instance with the given configuration
        /// </summary>
        ITrunk<object> Create(Type itemType, Dictionary<string, object> configuration);

        /// <summary>
        /// Get metadata about this trunk type
        /// </summary>
        TrunkMetadata GetMetadata();

        /// <summary>
        /// Validate configuration before creating trunk
        /// </summary>
        bool ValidateConfiguration(Dictionary<string, object> configuration);
    }

    /// <summary>
    /// Metadata describing a trunk implementation
    /// </summary>
    public class TrunkMetadata
    {
        /// <summary>
        /// Unique identifier for this trunk type (e.g., "file", "memory", "s3")
        /// </summary>
        public string TypeId { get; set; } = "";

        /// <summary>
        /// Human-readable display name
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// Description of what this trunk does
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Capabilities of this trunk
        /// </summary>
        public ITrunkCapabilities Capabilities { get; set; } = new TrunkCapabilities();

        /// <summary>
        /// Required configuration keys for this trunk
        /// </summary>
        public List<string> RequiredConfigKeys { get; set; } = new();

        /// <summary>
        /// Optional configuration keys with default values
        /// </summary>
        public Dictionary<string, object> OptionalConfigKeys { get; set; } = new();

        /// <summary>
        /// Whether this trunk is built-in or from a plugin
        /// </summary>
        public bool IsBuiltIn { get; set; } = true;

        /// <summary>
        /// Category (e.g., "Local", "Cloud", "Database", "Git")
        /// </summary>
        public string Category { get; set; } = "Local";
    }
}
