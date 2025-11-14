using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcornDB.Storage;
using AcornDB.Logging;
using AcornDB.Storage.Serialization;
using Newtonsoft.Json;

namespace AcornDB.Git
{
    /// <summary>
    /// Git-backed trunk where every Stash() is a commit and history is preserved via Git.
    /// 🌰 Peak nuttiness: Your database IS your Git history!
    /// Extends TrunkBase to support IRoot pipeline (compression, encryption, policy enforcement).
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    public class GitHubTrunk<T> : TrunkBase<T> where T : class
    {
        private readonly IGitProvider _git;
        private readonly string _authorName;
        private readonly string _authorEmail;
        private readonly bool _autoPush;

        /// <summary>
        /// Create a GitHubTrunk with the specified Git provider and author info
        /// </summary>
        /// <param name="repoPath">Path to the Git repository (will be created if it doesn't exist)</param>
        /// <param name="authorName">Git author name for commits</param>
        /// <param name="authorEmail">Git author email for commits</param>
        /// <param name="autoPush">Automatically push to remote after each commit (if remote exists)</param>
        /// <param name="gitProvider">Custom Git provider (defaults to LibGit2Sharp)</param>
        /// <param name="serializer">Custom serializer (defaults to Newtonsoft.Json)</param>
        public GitHubTrunk(
            string repoPath,
            string authorName = "AcornDB",
            string authorEmail = "acorn@acorndb.dev",
            bool autoPush = false,
            IGitProvider? gitProvider = null,
            ISerializer? serializer = null)
            : base(serializer, enableBatching: false) // Git commits are inherently unbatched
        {
            _git = gitProvider ?? new LibGit2SharpProvider();
            _authorName = authorName;
            _authorEmail = authorEmail;
            _autoPush = autoPush;

            // Initialize or open the repo
            _git.InitOrOpenRepository(repoPath);

            AcornLog.Info($"🐿️ GitHubTrunk initialized at: {_git.RepositoryPath}");
            AcornLog.Info($"   Author: {_authorName} <{_authorEmail}>");
            AcornLog.Info($"   Auto-push: {_autoPush}");
        }

        public override void Stash(string id, Nut<T> nut)
        {
            var fileName = GetFileName(id);
            var fullPath = Path.Combine(_git.RepositoryPath, fileName);

            // Serialize nut to JSON
            var json = _serializer.Serialize(nut);

            // Write to file
            File.WriteAllText(fullPath, json);

            // Commit the file
            var commitMessage = $"Stash: {id} at {nut.Timestamp:yyyy-MM-dd HH:mm:ss}";
            var commitSha = _git.CommitFile(fileName, commitMessage, _authorName, _authorEmail);

            AcornLog.Info($"   ✓ Committed {id} → {commitSha[..7]}");

            // Auto-push if enabled and remote exists
            if (_autoPush && _git.HasRemote())
            {
                try
                {
                    _git.Push();
                    AcornLog.Info($"   ↑ Pushed to remote");
                }
                catch (Exception ex)
                {
                    AcornLog.Error($"   ⚠ Push failed: {ex.Message}");
                }
            }
        }

        [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
        public void Save(string id, Nut<T> nut) => Stash(id, nut);

        public override Nut<T>? Crack(string id)
        {
            var fileName = GetFileName(id);

            if (!_git.FileExists(fileName))
                return null;

            var json = _git.ReadFile(fileName);
            if (json == null)
                return null;

            return _serializer.Deserialize<Nut<T>>(json);
        }

        [Obsolete("Use Crack() instead. This method will be removed in a future version.")]
        public Nut<T>? Load(string id) => Crack(id);

        public override void Toss(string id)
        {
            var fileName = GetFileName(id);

            if (!_git.FileExists(fileName))
                return;

            // Commit the deletion
            var commitMessage = $"Toss: {id} at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
            var commitSha = _git.DeleteFile(fileName, commitMessage, _authorName, _authorEmail);

            AcornLog.Info($"   ✓ Deleted {id} → {commitSha[..7]}");

            // Auto-push if enabled
            if (_autoPush && _git.HasRemote())
            {
                try
                {
                    _git.Push();
                    AcornLog.Info($"   ↑ Pushed deletion to remote");
                }
                catch (Exception ex)
                {
                    AcornLog.Info($"   ⚠ Push failed: {ex.Message}");
                }
            }
        }

        [Obsolete("Use Toss() instead. This method will be removed in a future version.")]
        public void Delete(string id) => Toss(id);

        public override IEnumerable<Nut<T>> CrackAll()
        {
            var nuts = new List<Nut<T>>();

            foreach (var file in _git.GetAllFiles())
            {
                if (!file.EndsWith(".json"))
                    continue;

                var json = _git.ReadFile(file);
                if (json != null)
                {
                    try
                    {
                        var nut = _serializer.Deserialize<Nut<T>>(json);
                        if (nut != null)
                            nuts.Add(nut);
                    }
                    catch (Exception ex)
                    {
                        AcornLog.Info($"   ⚠ Failed to deserialize {file}: {ex.Message}");
                    }
                }
            }

            return nuts;
        }

        [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
        public IEnumerable<Nut<T>> LoadAll() => CrackAll();

        public override IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            var fileName = GetFileName(id);
            var history = new List<Nut<T>>();

            // Get commit history for this file
            var commits = _git.GetFileHistory(fileName).ToList();

            // Read file content at each commit
            foreach (var commit in commits)
            {
                try
                {
                    var json = _git.ReadFileAtCommit(fileName, commit.Sha);
                    if (json != null)
                    {
                        var nut = _serializer.Deserialize<Nut<T>>(json);
                        if (nut != null)
                            history.Add(nut);
                    }
                }
                catch (Exception ex)
                {
                    AcornLog.Info($"   ⚠ Failed to read {fileName} at {commit.Sha[..7]}: {ex.Message}");
                }
            }

            // Return in reverse order (oldest first)
            history.Reverse();
            return history;
        }

        public override IEnumerable<Nut<T>> ExportChanges()
        {
            // Export all current nuts (snapshot)
            return CrackAll();
        }

        public override void ImportChanges(IEnumerable<Nut<T>> changes)
        {
            // Import each nut as a commit
            foreach (var nut in changes)
            {
                Stash(nut.Id, nut);
            }

            AcornLog.Info($"   ✓ Imported {changes.Count()} nuts");
        }

        // ITrunkCapabilities implementation
        public override ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = true,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = false,
            TrunkType = "GitHubTrunk"
        };

        /// <summary>
        /// Manually push to remote
        /// </summary>
        public void Push(string remoteName = "origin", string branch = "main")
        {
            _git.Push(remoteName, branch);
            AcornLog.Info($"   ↑ Pushed to {remoteName}/{branch}");
        }

        /// <summary>
        /// Manually pull from remote
        /// </summary>
        public void Pull(string remoteName = "origin", string branch = "main")
        {
            _git.Pull(remoteName, branch);
            AcornLog.Info($"   ↓ Pulled from {remoteName}/{branch}");
        }

        /// <summary>
        /// Get commit history (Git log)
        /// </summary>
        public IEnumerable<GitCommitInfo> GetCommitLog(string id)
        {
            var fileName = GetFileName(id);
            return _git.GetFileHistory(fileName);
        }

        private string GetFileName(string id)
        {
            // Sanitize ID for filename (replace invalid chars)
            var sanitized = string.Join("_", id.Split(Path.GetInvalidFileNameChars()));
            return $"{sanitized}.json";
        }
    }
}
