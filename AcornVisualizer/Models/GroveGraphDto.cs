namespace AcornVisualizer.Models
{
    public class GroveGraphDto
    {
        public List<TreeNodeDto> Trees { get; set; } = new();
        public List<TangleEdgeDto> Tangles { get; set; } = new();
        public GroveStatsDto Stats { get; set; } = new();
    }

    public class TreeNodeDto
    {
        public string Id { get; set; } = "";
        public string TypeName { get; set; } = "";
        public int NutCount { get; set; }
        public int TangleCount { get; set; }
        public string TrunkType { get; set; } = "";
        public bool SupportsHistory { get; set; }
        public bool IsDurable { get; set; }
    }

    public class TangleEdgeDto
    {
        public string Id { get; set; } = "";
        public string SourceTreeType { get; set; } = "";
        public string TargetUrl { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class GroveStatsDto
    {
        public int TotalTrees { get; set; }
        public int TotalNuts { get; set; }
        public int ActiveTangles { get; set; }
        public int TotalStashed { get; set; }
        public int TotalTossed { get; set; }
        public int TotalSquabbles { get; set; }
    }

    public class TreeDetailDto
    {
        public string TypeName { get; set; } = "";
        public int NutCount { get; set; }
        public List<NutDto> Nuts { get; set; } = new();
        public TreeStatsDto Stats { get; set; } = new();
        public TrunkCapabilitiesDto Capabilities { get; set; } = new();
    }

    public class NutDto
    {
        public string Id { get; set; } = "";
        public string PayloadJson { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public int Version { get; set; }
        public bool HasHistory { get; set; }
    }

    public class TreeStatsDto
    {
        public int TotalStashed { get; set; }
        public int TotalTossed { get; set; }
        public int SquabblesResolved { get; set; }
        public int ActiveTangles { get; set; }
    }

    public class TrunkCapabilitiesDto
    {
        public string TrunkType { get; set; } = "";
        public bool SupportsHistory { get; set; }
        public bool SupportsSync { get; set; }
        public bool IsDurable { get; set; }
        public bool SupportsAsync { get; set; }
    }

    public class NutHistoryDto
    {
        public string Id { get; set; } = "";
        public List<NutVersionDto> History { get; set; } = new();
    }

    public class NutVersionDto
    {
        public int Version { get; set; }
        public string PayloadJson { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public class CreateNutRequest
    {
        public string Id { get; set; } = "";
        public string PayloadJson { get; set; } = "";
    }

    public class UpdateNutRequest
    {
        public string PayloadJson { get; set; } = "";
    }

    public class RegisterTreeRequest
    {
        public string TypeName { get; set; } = "";
        public string RemoteUrl { get; set; } = "";
    }
}
