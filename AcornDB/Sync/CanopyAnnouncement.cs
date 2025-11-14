using System;
using System.Collections.Generic;

namespace AcornDB.Sync
{
    /// <summary>
    /// Canopy announcement broadcast on the network
    /// </summary>
    public class CanopyAnnouncement
    {
        public string NodeId { get; set; } = "";
        public int HttpPort { get; set; }
        public int TreeCount { get; set; }
        public List<string> TreeTypes { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }
}
