using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp;

namespace AcornDB.Git
{
    /// <summary>
    /// LibGit2Sharp implementation of IGitProvider
    /// </summary>
    public class LibGit2SharpProvider : IGitProvider
    {
        private Repository? _repo;
        private string _repoPath = "";

        public string RepositoryPath => _repoPath;

        public void InitOrOpenRepository(string repoPath)
        {
            _repoPath = Path.GetFullPath(repoPath);

            // Ensure directory exists
            Directory.CreateDirectory(_repoPath);

            // Check if already a git repo
            if (Repository.IsValid(_repoPath))
            {
                _repo = new Repository(_repoPath);
            }
            else
            {
                // Initialize new repo
                Repository.Init(_repoPath);
                _repo = new Repository(_repoPath);
            }
        }

        public string CommitFile(string filePath, string message, string author, string email)
        {
            if (_repo == null)
                throw new InvalidOperationException("Repository not initialized. Call InitOrOpenRepository first.");

            var fullPath = Path.Combine(_repoPath, filePath);

            // Ensure the file exists
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"File not found: {fullPath}");

            // Stage the file
            Commands.Stage(_repo, filePath);

            // Create signature
            var signature = new Signature(author, email, DateTimeOffset.UtcNow);

            // Commit
            var commit = _repo.Commit(message, signature, signature);

            return commit.Sha;
        }

        public string DeleteFile(string filePath, string message, string author, string email)
        {
            if (_repo == null)
                throw new InvalidOperationException("Repository not initialized.");

            var fullPath = Path.Combine(_repoPath, filePath);

            // Delete the file if it exists
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            // Stage the deletion
            Commands.Remove(_repo, filePath);

            // Create signature
            var signature = new Signature(author, email, DateTimeOffset.UtcNow);

            // Commit the deletion
            var commit = _repo.Commit(message, signature, signature);

            return commit.Sha;
        }

        public string? ReadFile(string filePath)
        {
            if (_repo == null)
                throw new InvalidOperationException("Repository not initialized.");

            var fullPath = Path.Combine(_repoPath, filePath);

            if (!File.Exists(fullPath))
                return null;

            return File.ReadAllText(fullPath);
        }

        public IEnumerable<string> GetAllFiles()
        {
            if (_repo == null)
                throw new InvalidOperationException("Repository not initialized.");

            // Get all files in the working directory (excluding .git)
            return Directory.GetFiles(_repoPath, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".git"))
                .Select(f => Path.GetRelativePath(_repoPath, f))
                .ToList();
        }

        public IEnumerable<GitCommitInfo> GetFileHistory(string filePath)
        {
            if (_repo == null)
                throw new InvalidOperationException("Repository not initialized.");

            var commits = new List<GitCommitInfo>();

            // Use the commit filter to get only commits that affected this file
            var filter = new CommitFilter
            {
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
            };

            foreach (var commit in _repo.Commits.QueryBy(filePath, filter))
            {
                commits.Add(new GitCommitInfo
                {
                    Sha = commit.Commit.Sha,
                    Message = commit.Commit.MessageShort,
                    Author = commit.Commit.Author.Name,
                    Email = commit.Commit.Author.Email,
                    Timestamp = commit.Commit.Author.When.UtcDateTime
                });
            }

            return commits;
        }

        public string? ReadFileAtCommit(string filePath, string commitSha)
        {
            if (_repo == null)
                throw new InvalidOperationException("Repository not initialized.");

            var commit = _repo.Lookup<Commit>(commitSha);
            if (commit == null)
                return null;

            var treeEntry = commit[filePath];
            if (treeEntry?.TargetType != TreeEntryTargetType.Blob)
                return null;

            var blob = (Blob)treeEntry.Target;
            return blob.GetContentText();
        }

        public bool FileExists(string filePath)
        {
            var fullPath = Path.Combine(_repoPath, filePath);
            return File.Exists(fullPath);
        }

        public void SquashCommits(string since)
        {
            if (_repo == null)
                throw new InvalidOperationException("Repository not initialized.");

            // Note: Interactive rebase is complex in LibGit2Sharp
            // For now, we'll implement a simplified version
            throw new NotImplementedException(
                "Squash/rebase not yet implemented. " +
                "Consider using git CLI or advanced LibGit2Sharp techniques.");
        }

        public void Push(string remoteName = "origin", string branch = "main")
        {
            if (_repo == null)
                throw new InvalidOperationException("Repository not initialized.");

            var remote = _repo.Network.Remotes[remoteName];
            if (remote == null)
                throw new InvalidOperationException($"Remote '{remoteName}' not configured.");

            var options = new PushOptions();
            _repo.Network.Push(remote, $"refs/heads/{branch}", options);
        }

        public void Pull(string remoteName = "origin", string branch = "main")
        {
            if (_repo == null)
                throw new InvalidOperationException("Repository not initialized.");

            var signature = new Signature("AcornDB", "acorn@acorndb.dev", DateTimeOffset.UtcNow);
            Commands.Pull(_repo, signature, new PullOptions());
        }

        public bool HasRemote(string remoteName = "origin")
        {
            if (_repo == null)
                return false;

            return _repo.Network.Remotes[remoteName] != null;
        }
    }
}
