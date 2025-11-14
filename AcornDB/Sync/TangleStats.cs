using System;

namespace AcornDB
{
    public class TangleStats
    {
        public string TreeType { get; set; }
        public string LocalTreeId { get; set; }
        public string RemoteTreeId { get; set; }
        public string RemoteUrl { get; set; }
        public string RemoteAddress { get; set; }
        public int TotalPushes { get; set; }
        public int TotalPulls { get; set; }
        public DateTime? LastSyncTime { get; set; }
        public string Status { get; set; }
    }
}
