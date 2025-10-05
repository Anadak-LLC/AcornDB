using System.Collections.Generic;

namespace AcornDB.Models
{
    public class GroveStats
    {
        public int TotalTrees { get; set; }
        public int TotalStashed { get; set; }
        public int TotalTossed { get; set; }
        public int TotalSquabbles { get; set; }
        public int TotalSmushes { get; set; }
        public int ActiveTangles { get; set; }
        public List<string> TreeTypes { get; set; } = new();
    }
}