using System;
using System.Collections.Generic;

namespace AcornDB.Git
{
    /// <summary>
    /// Abstraction for Git operations used by GitHubTrunk.
    /// Allows for testability and alternative Git implementations.
    /// </summary>
    public interface IGitProvider
    {
        /// <summary>
        /// Initialize or open a Git repository at the specified path
        /// </summary>
        void InitOrOpenRepository(string repoPath);

        /// <summary>
        /// Stage and commit a file with a message
        /// </summary>
        /// <param name="filePath">Relative path to the file within the repo</param>
        /// <param name="message">Commit message</param>
        /// <param name="author">Author name</param>
        /// <param name="email">Author email</param>
        /// <returns>Commit SHA</returns>
        string CommitFile(string filePath, string message, string author, string email);

        /// <summary>
        /// Delete a file and commit the deletion
        /// </summary>
        string DeleteFile(string filePath, string message, string author, string email);

        /// <summary>
        /// Read file content at HEAD
        /// </summary>
        string? ReadFile(string filePath);

        /// <summary>
        /// Get all files in the repository (excluding .git)
        /// </summary>
        IEnumerable<string> GetAllFiles();

        /// <summary>
        /// Get commit history for a specific file
        /// </summary>
        /// <param name="filePath">Relative path to the file</param>
        /// <returns>List of commits affecting this file (newest first)</returns>
        IEnumerable<GitCommitInfo> GetFileHistory(string filePath);

        /// <summary>
        /// Get file content at a specific commit
        /// </summary>
        string? ReadFileAtCommit(string filePath, string commitSha);

        /// <summary>
        /// Check if file exists in the working directory
        /// </summary>
        bool FileExists(string filePath);

        /// <summary>
        /// Get the repository path
        /// </summary>
        string RepositoryPath { get; }

        /// <summary>
        /// Perform an interactive rebase (squash commits)
        /// </summary>
        /// <param name="since">Squash commits since this commit</param>
        void SquashCommits(string since);

        /// <summary>
        /// Push changes to remote (if configured)
        /// </summary>
        void Push(string remoteName = "origin", string branch = "main");

        /// <summary>
        /// Pull changes from remote (if configured)
        /// </summary>
        void Pull(string remoteName = "origin", string branch = "main");

        /// <summary>
        /// Check if the repository has a remote configured
        /// </summary>
        bool HasRemote(string remoteName = "origin");
    }

    /// <summary>
    /// Git commit information
    /// </summary>
    public class GitCommitInfo
    {
        public string Sha { get; set; } = "";
        public string Message { get; set; } = "";
        public string Author { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}
