using System;

namespace AcornDB.Sync
{
    /// <summary>
    /// Statistics about a branch's sync activity
    /// </summary>
    public class BranchStats
    {
        public string RemoteUrl { get; set; } = "";
        public SyncMode SyncMode { get; set; }
        public ConflictDirection ConflictDirection { get; set; }
        public int TotalPushed { get; set; }
        public int TotalDeleted { get; set; }
        public long TotalPulled { get; set; }
        public long TotalConflicts { get; set; }
        public bool DeltaSyncEnabled { get; set; }
        public DateTime LastSyncTimestamp { get; set; }
        public long TotalOperations => TotalPushed + TotalDeleted + TotalPulled;
        public bool HasSynced => LastSyncTimestamp != DateTime.MinValue;
    }
}
