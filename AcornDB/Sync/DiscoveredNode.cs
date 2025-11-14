using System;
using System.Collections.Generic;

namespace AcornDB.Sync
{
    /// <summary>
    /// Information about a discovered node
    /// </summary>
    public class DiscoveredNode
    {
        public string NodeId { get; set; } = "";
        public string Address { get; set; } = "";
        public int HttpPort { get; set; }
        public string RemoteUrl { get; set; } = "";
        public int TreeCount { get; set; }
        public List<string> TreeTypes { get; set; } = new();
        public DateTime DiscoveredAt { get; set; }
        public DateTime LastSeen { get; set; }
    }
}
