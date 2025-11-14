using System;

namespace AcornDB.Sync
{
    /// <summary>
    /// Audit log entry
    /// </summary>
    public class AuditEntry
    {
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } = string.Empty;
        public string LeafId { get; set; } = string.Empty;
        public string OriginTreeId { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string TreeType { get; set; } = string.Empty;
        public int HopCount { get; set; }
        public string Details { get; set; } = string.Empty;
    }
}
