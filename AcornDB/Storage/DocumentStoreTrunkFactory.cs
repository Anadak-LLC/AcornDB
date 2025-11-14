using System;
using System.Collections.Generic;

namespace AcornDB.Storage
{
    /// <summary>
    /// Factory for creating DocumentStoreTrunk instances
    /// </summary>
    public class DocumentStoreTrunkFactory : ITrunkFactory
    {
        public ITrunk<object> Create(Type itemType, Dictionary<string, object> configuration)
        {
            var path = configuration.TryGetValue("path", out var pathObj) ? pathObj?.ToString() : null;

            var trunkType = typeof(DocumentStoreTrunk<>).MakeGenericType(itemType);
            var trunk = Activator.CreateInstance(trunkType, path);
            return (ITrunk<object>)trunk!;
        }

        public TrunkMetadata GetMetadata()
        {
            return new TrunkMetadata
            {
                TypeId = "docstore",
                DisplayName = "Document Store Trunk",
                Description = "Full-featured trunk with append-only logging, versioning, and time-travel. Supports history.",
                Capabilities = new TrunkCapabilities
                {
                    SupportsHistory = true,
                    SupportsSync = true,
                    IsDurable = true,
                    SupportsAsync = false,
                    TrunkType = "DocumentStoreTrunk"
                },
                RequiredConfigKeys = new List<string>(),
                OptionalConfigKeys = new Dictionary<string, object>
                {
                    { "path", "./data/docstore/{TypeName}" }
                },
                IsBuiltIn = true,
                Category = "Local"
            };
        }

        public bool ValidateConfiguration(Dictionary<string, object> configuration)
        {
            // Path is optional, so always valid
            return true;
        }
    }
}
