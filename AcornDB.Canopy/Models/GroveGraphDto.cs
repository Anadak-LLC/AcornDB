
using System.Collections.Generic;

namespace AcornDB.Models
{
    public class GroveGraphDto
    {
        public List<TreeNodeDto> Trees { get; set; } = new();
        public List<TangleEdgeDto> Tangles { get; set; } = new();
    }

    public class TreeNodeDto
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public int NutCount { get; set; }
        public bool IsRemote { get; set; }
    }

    public class TangleEdgeDto
    {
        public string FromTreeId { get; set; } = "";
        public string ToTreeId { get; set; } = "";
        public string Url { get; set; } = "";
    }
}
