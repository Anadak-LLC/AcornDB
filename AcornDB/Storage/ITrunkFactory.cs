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
}
