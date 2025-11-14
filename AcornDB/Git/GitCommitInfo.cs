using System;

namespace AcornDB.Git
{
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
