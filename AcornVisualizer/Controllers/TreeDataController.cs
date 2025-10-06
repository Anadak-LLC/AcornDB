using Microsoft.AspNetCore.Mvc;
using AcornDB.Models;
using AcornDB.Storage;
using AcornVisualizer.Models;
using System.Text.Json;

namespace AcornVisualizer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TreeDataController : ControllerBase
    {
        private readonly Grove _grove;

        public TreeDataController(Grove grove)
        {
            _grove = grove;
        }

        [HttpGet("{typeName}")]
        public ActionResult<TreeDetailDto> GetTreeDetails(string typeName)
        {
            var tree = _grove.GetTreeByTypeName(typeName);
            if (tree == null)
            {
                return NotFound(new { message = $"Tree '{typeName}' not found in grove" });
            }

            var treeType = tree.GetType();
            var genericArg = treeType.GenericTypeArguments.FirstOrDefault();
            if (genericArg == null)
            {
                return BadRequest(new { message = "Could not determine tree type" });
            }

            var detail = new TreeDetailDto
            {
                TypeName = genericArg.Name
            };

            // Get all nuts using ExportChanges
            var changes = _grove.ExportChanges(typeName);
            var nutsList = new List<NutDto>();

            foreach (var change in changes)
            {
                if (change == null) continue;

                var changeType = change.GetType();
                var idProp = changeType.GetProperty("Id");
                var payloadProp = changeType.GetProperty("Payload");
                var timestampProp = changeType.GetProperty("Timestamp");
                var versionProp = changeType.GetProperty("Version");

                var id = idProp?.GetValue(change)?.ToString() ?? "unknown";
                var payload = payloadProp?.GetValue(change);
                var timestamp = (DateTime)(timestampProp?.GetValue(change) ?? DateTime.MinValue);
                var version = (int)(versionProp?.GetValue(change) ?? 0);

                var payloadJson = payload != null
                    ? JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })
                    : "null";

                // Check if this nut has history
                bool hasHistory = false;
                var trunkField = treeType.GetField("_trunk",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var trunk = trunkField?.GetValue(tree);

                if (trunk != null)
                {
                    var canGetHistoryMethod = typeof(TrunkCapabilitiesExtensions)
                        .GetMethod("CanGetHistory")
                        ?.MakeGenericMethod(genericArg);

                    if (canGetHistoryMethod != null)
                    {
                        hasHistory = (bool)(canGetHistoryMethod.Invoke(null, new[] { trunk }) ?? false);
                    }
                }

                nutsList.Add(new NutDto
                {
                    Id = id,
                    PayloadJson = payloadJson,
                    Timestamp = timestamp,
                    Version = version,
                    HasHistory = hasHistory
                });
            }

            detail.Nuts = nutsList;
            detail.NutCount = nutsList.Count;

            // Get stats
            var statsMethod = treeType.GetMethod("GetNutStats");
            var stats = statsMethod?.Invoke(tree, null);

            if (stats != null)
            {
                var statsType = stats.GetType();
                detail.Stats = new TreeStatsDto
                {
                    TotalStashed = (int)(statsType.GetProperty("TotalStashed")?.GetValue(stats) ?? 0),
                    TotalTossed = (int)(statsType.GetProperty("TotalTossed")?.GetValue(stats) ?? 0),
                    SquabblesResolved = (int)(statsType.GetProperty("SquabblesResolved")?.GetValue(stats) ?? 0),
                    ActiveTangles = (int)(statsType.GetProperty("ActiveTangles")?.GetValue(stats) ?? 0)
                };
            }

            // Get trunk capabilities
            var trunkFieldForCaps = treeType.GetField("_trunk",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            var trunkForCaps = trunkFieldForCaps?.GetValue(tree);

            if (trunkForCaps != null)
            {
                var capsMethod = typeof(TrunkCapabilitiesExtensions)
                    .GetMethod("GetCapabilities")
                    ?.MakeGenericMethod(genericArg);

                if (capsMethod != null)
                {
                    var caps = capsMethod.Invoke(null, new[] { trunkForCaps });
                    if (caps != null)
                    {
                        var capsType = caps.GetType();
                        detail.Capabilities = new TrunkCapabilitiesDto
                        {
                            TrunkType = capsType.GetProperty("TrunkType")?.GetValue(caps)?.ToString() ?? "Unknown",
                            SupportsHistory = (bool)(capsType.GetProperty("SupportsHistory")?.GetValue(caps) ?? false),
                            SupportsSync = (bool)(capsType.GetProperty("SupportsSync")?.GetValue(caps) ?? false),
                            IsDurable = (bool)(capsType.GetProperty("IsDurable")?.GetValue(caps) ?? false),
                            SupportsAsync = (bool)(capsType.GetProperty("SupportsAsync")?.GetValue(caps) ?? false)
                        };
                    }
                }
            }

            return Ok(detail);
        }

        [HttpGet("{typeName}/nuts")]
        public ActionResult<List<NutDto>> GetNuts(string typeName)
        {
            var changes = _grove.ExportChanges(typeName);
            var nuts = new List<NutDto>();

            foreach (var change in changes)
            {
                if (change == null) continue;

                var changeType = change.GetType();
                var idProp = changeType.GetProperty("Id");
                var payloadProp = changeType.GetProperty("Payload");
                var timestampProp = changeType.GetProperty("Timestamp");
                var versionProp = changeType.GetProperty("Version");

                var payload = payloadProp?.GetValue(change);
                var payloadJson = payload != null
                    ? JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })
                    : "null";

                nuts.Add(new NutDto
                {
                    Id = idProp?.GetValue(change)?.ToString() ?? "unknown",
                    PayloadJson = payloadJson,
                    Timestamp = (DateTime)(timestampProp?.GetValue(change) ?? DateTime.MinValue),
                    Version = (int)(versionProp?.GetValue(change) ?? 0),
                    HasHistory = false
                });
            }

            return Ok(nuts);
        }
    }
}
