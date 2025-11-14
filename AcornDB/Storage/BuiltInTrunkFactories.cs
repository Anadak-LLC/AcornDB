using System;
using System.Collections.Generic;
using System.Linq;

namespace AcornDB.Storage
{
    /// <summary>
    /// Factory for creating FileTrunk instances
    /// </summary>
    public class FileTrunkFactory : ITrunkFactory
    {
        public ITrunk<object> Create(Type itemType, Dictionary<string, object> configuration)
        {
            var path = configuration.TryGetValue("path", out var pathObj) ? pathObj?.ToString() : null;

            var trunkType = typeof(FileTrunk<>).MakeGenericType(itemType);
            var trunk = Activator.CreateInstance(trunkType, path);
            return (ITrunk<object>)trunk!;
        }

        public TrunkMetadata GetMetadata()
        {
            return new TrunkMetadata
            {
                TypeId = "file",
                DisplayName = "File Trunk",
                Description = "Stores nuts as JSON files in a local folder. Simple and human-readable.",
                Capabilities = new TrunkCapabilities
                {
                    SupportsHistory = false,
                    SupportsSync = true,
                    IsDurable = true,
                    SupportsAsync = false,
                    TrunkType = "FileTrunk"
                },
                RequiredConfigKeys = new List<string>(),
                OptionalConfigKeys = new Dictionary<string, object>
                {
                    { "path", "./data/{TypeName}" }
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

    // NOTE: AzureTrunk and cloud-based trunks have been moved to AcornDB.Persistence.Cloud package
    // To use Azure, S3, or other cloud storage:
    // 1. Install: AcornDB.Persistence.Cloud NuGet package
    // 2. Use: new AzureTrunk<T>(connectionString, containerName) or
    //         new CloudTrunk<T>(new AzureBlobProvider(connectionString, containerName))
}
