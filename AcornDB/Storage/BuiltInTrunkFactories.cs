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

    /// <summary>
    /// Factory for creating GitHubTrunk instances
    /// </summary>
    public class GitHubTrunkFactory : ITrunkFactory
    {
        public ITrunk<object> Create(Type itemType, Dictionary<string, object> configuration)
        {
            var repoPath = configuration.TryGetValue("repoPath", out var pathObj)
                ? pathObj?.ToString()
                : null;

            var authorName = configuration.TryGetValue("authorName", out var nameObj)
                ? nameObj?.ToString() ?? "AcornDB"
                : "AcornDB";

            var authorEmail = configuration.TryGetValue("authorEmail", out var emailObj)
                ? emailObj?.ToString() ?? "acorn@acorndb.dev"
                : "acorn@acorndb.dev";

            var autoPush = configuration.TryGetValue("autoPush", out var pushObj)
                && pushObj is bool pushBool
                && pushBool;

            var trunkType = typeof(Git.GitHubTrunk<>).MakeGenericType(itemType);
            var trunk = Activator.CreateInstance(trunkType, repoPath, authorName, authorEmail, autoPush);
            return (ITrunk<object>)trunk!;
        }

        public TrunkMetadata GetMetadata()
        {
            return new TrunkMetadata
            {
                TypeId = "git",
                DisplayName = "GitHub Trunk",
                Description = "Git-based storage where every Stash() creates a commit. Full version control integration.",
                Capabilities = new TrunkCapabilities
                {
                    SupportsHistory = true,
                    SupportsSync = true,
                    IsDurable = true,
                    SupportsAsync = false,
                    TrunkType = "GitHubTrunk"
                },
                RequiredConfigKeys = new List<string>(),
                OptionalConfigKeys = new Dictionary<string, object>
                {
                    { "repoPath", "./acorndb_git_{TypeName}" },
                    { "authorName", "AcornDB" },
                    { "authorEmail", "acorn@acorndb.dev" },
                    { "autoPush", false }
                },
                IsBuiltIn = true,
                Category = "Git"
            };
        }

        public bool ValidateConfiguration(Dictionary<string, object> configuration)
        {
            // All parameters are optional
            return true;
        }
    }

    // NOTE: AzureTrunk and cloud-based trunks have been moved to AcornDB.Persistence.Cloud package
    // To use Azure, S3, or other cloud storage:
    // 1. Install: AcornDB.Persistence.Cloud NuGet package
    // 2. Use: new AzureTrunk<T>(connectionString, containerName) or
    //         new CloudTrunk<T>(new AzureBlobProvider(connectionString, containerName))
}
