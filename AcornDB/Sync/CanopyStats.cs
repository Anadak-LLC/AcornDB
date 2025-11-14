namespace AcornDB.Sync
{
    /// <summary>
    /// Canopy discovery statistics
    /// </summary>
    public class CanopyStats
    {
        public string LocalNodeId { get; set; } = "";
        public int TotalDiscovered { get; set; }
        public int ActiveNodes { get; set; }
        public int TotalTrees { get; set; }
        public int UniqueTreeTypes { get; set; }
    }
}
