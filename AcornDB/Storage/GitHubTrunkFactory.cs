using System;
using System.Collections.Generic;

namespace AcornDB.Storage
{
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
}
