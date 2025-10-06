using Microsoft.AspNetCore.Mvc;
using AcornDB.Models;
using AcornDB.Storage;
using AcornVisualizer.Models;
using System.Text.Json;

namespace AcornVisualizer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GroveGraphController : ControllerBase
    {
        private readonly Grove _grove;

        public GroveGraphController(Grove grove)
        {
            _grove = grove;
        }

        [HttpGet]
        public ActionResult<GroveGraphDto> GetGraph()
        {
            var graph = new GroveGraphDto();

            // Get all trees
            var treeInfos = _grove.GetTreeInfo();
            foreach (var treeInfo in treeInfos)
            {
                var tree = _grove.GetTreeByTypeName(treeInfo.Id);
                if (tree == null) continue;

                var treeType = tree.GetType();
                var genericArg = treeType.GenericTypeArguments.FirstOrDefault();
                if (genericArg == null) continue;

                // Get trunk via reflection
                var trunkField = treeType.GetField("_trunk",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var trunk = trunkField?.GetValue(tree);

                var capabilities = new TrunkCapabilitiesDto
                {
                    TrunkType = "Unknown",
                    SupportsHistory = false,
                    SupportsSync = false,
                    IsDurable = false,
                    SupportsAsync = false
                };

                if (trunk != null)
                {
                    // Get capabilities using extension method
                    var capsMethod = typeof(TrunkCapabilitiesExtensions)
                        .GetMethod("GetCapabilities")
                        ?.MakeGenericMethod(genericArg);

                    if (capsMethod != null)
                    {
                        var caps = capsMethod.Invoke(null, new[] { trunk });
                        if (caps != null)
                        {
                            var capsType = caps.GetType();
                            capabilities.TrunkType = capsType.GetProperty("TrunkType")?.GetValue(caps)?.ToString() ?? "Unknown";
                            capabilities.SupportsHistory = (bool)(capsType.GetProperty("SupportsHistory")?.GetValue(caps) ?? false);
                            capabilities.SupportsSync = (bool)(capsType.GetProperty("SupportsSync")?.GetValue(caps) ?? false);
                            capabilities.IsDurable = (bool)(capsType.GetProperty("IsDurable")?.GetValue(caps) ?? false);
                            capabilities.SupportsAsync = (bool)(capsType.GetProperty("SupportsAsync")?.GetValue(caps) ?? false);
                        }
                    }
                }

                // Get stats
                var statsMethod = treeType.GetMethod("GetNutStats");
                var stats = statsMethod?.Invoke(tree, null);
                int tangleCount = 0;
                if (stats != null)
                {
                    var statsType = stats.GetType();
                    tangleCount = (int)(statsType.GetProperty("ActiveTangles")?.GetValue(stats) ?? 0);
                }

                graph.Trees.Add(new TreeNodeDto
                {
                    Id = treeInfo.Id,
                    TypeName = treeInfo.Type,
                    NutCount = treeInfo.NutCount,
                    TangleCount = tangleCount,
                    TrunkType = capabilities.TrunkType,
                    SupportsHistory = capabilities.SupportsHistory,
                    IsDurable = capabilities.IsDurable
                });
            }

            // Get grove-wide stats
            var groveStats = _grove.GetNutStats();
            graph.Stats = new GroveStatsDto
            {
                TotalTrees = groveStats.TotalTrees,
                TotalNuts = graph.Trees.Sum(t => t.NutCount),
                ActiveTangles = groveStats.ActiveTangles,
                TotalStashed = groveStats.TotalStashed,
                TotalTossed = groveStats.TotalTossed,
                TotalSquabbles = groveStats.TotalSquabbles
            };

            return Ok(graph);
        }

        [HttpGet("stats")]
        public ActionResult<GroveStatsDto> GetStats()
        {
            var stats = _grove.GetNutStats();
            return Ok(new GroveStatsDto
            {
                TotalTrees = stats.TotalTrees,
                TotalNuts = 0, // Will be calculated
                ActiveTangles = stats.ActiveTangles,
                TotalStashed = stats.TotalStashed,
                TotalTossed = stats.TotalTossed,
                TotalSquabbles = stats.TotalSquabbles
            });
        }
    }
}
