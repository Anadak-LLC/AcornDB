using System;
using System.Collections.Generic;

namespace AcornDB.Storage
{
    /// <summary>
    /// Factory for creating MemoryTrunk instances
    /// </summary>
    public class MemoryTrunkFactory : ITrunkFactory
    {
        public ITrunk<object> Create(Type itemType, Dictionary<string, object> configuration)
        {
            var trunkType = typeof(MemoryTrunk<>).MakeGenericType(itemType);
            var trunk = Activator.CreateInstance(trunkType);
            return (ITrunk<object>)trunk!;
        }

        public TrunkMetadata GetMetadata()
        {
            return new TrunkMetadata
            {
                TypeId = "memory",
                DisplayName = "Memory Trunk",
                Description = "In-memory storage for testing. Non-durable, fast, no history.",
                Capabilities = new TrunkCapabilities
                {
                    SupportsHistory = false,
                    SupportsSync = true,
                    IsDurable = false,
                    SupportsAsync = false,
                    TrunkType = "MemoryTrunk"
                },
                RequiredConfigKeys = new List<string>(),
                OptionalConfigKeys = new Dictionary<string, object>(),
                IsBuiltIn = true,
                Category = "Local"
            };
        }

        public bool ValidateConfiguration(Dictionary<string, object> configuration)
        {
            // No configuration needed
            return true;
        }
    }
}
