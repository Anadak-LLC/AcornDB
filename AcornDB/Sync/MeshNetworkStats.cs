using System.Collections.Generic;

namespace AcornDB.Sync
{
    /// <summary>
    /// Complete mesh network statistics
    /// </summary>
    public class MeshNetworkStats
    {
        public MeshTopology Topology { get; set; } = new();
        public Dictionary<string, MeshSyncStats> NodeStats { get; set; } = new();
    }
}
