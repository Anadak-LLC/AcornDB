using System.Collections.Generic;

namespace AcornDB.Sync
{
    /// <summary>
    /// Mesh topology information
    /// </summary>
    public class MeshTopology
    {
        public int TotalNodes { get; set; }
        public int TotalConnections { get; set; }
        public Dictionary<string, List<string>> Connections { get; set; } = new();

        public double AverageDegree => TotalNodes > 0 ? (TotalConnections * 2.0) / TotalNodes : 0;
    }
}
