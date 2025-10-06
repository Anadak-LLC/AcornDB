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

        [HttpGet("{typeName}/nut/{id}")]
        public ActionResult<NutDto> GetNut(string typeName, string id)
        {
            var tree = _grove.GetTreeByTypeName(typeName);
            if (tree == null)
            {
                return NotFound(new { message = $"Tree '{typeName}' not found" });
            }

            var treeType = tree.GetType();
            var crackMethod = treeType.GetMethod("Crack");
            if (crackMethod == null)
            {
                return StatusCode(500, new { message = "Could not access Crack method" });
            }

            var nutShell = crackMethod.Invoke(tree, new object[] { id });
            if (nutShell == null)
            {
                return NotFound(new { message = $"Nut '{id}' not found in tree '{typeName}'" });
            }

            var nutType = nutShell.GetType();
            var payloadProp = nutType.GetProperty("Payload");
            var timestampProp = nutType.GetProperty("Timestamp");
            var versionProp = nutType.GetProperty("Version");

            var payload = payloadProp?.GetValue(nutShell);
            var payloadJson = payload != null
                ? JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })
                : "null";

            return Ok(new NutDto
            {
                Id = id,
                PayloadJson = payloadJson,
                Timestamp = (DateTime)(timestampProp?.GetValue(nutShell) ?? DateTime.MinValue),
                Version = (int)(versionProp?.GetValue(nutShell) ?? 0),
                HasHistory = false
            });
        }

        [HttpPost("{typeName}/nut")]
        public ActionResult CreateNut(string typeName, [FromBody] CreateNutRequest request)
        {
            var tree = _grove.GetTreeByTypeName(typeName);
            if (tree == null)
            {
                return NotFound(new { message = $"Tree '{typeName}' not found" });
            }

            var treeType = tree.GetType();
            var genericArg = treeType.GenericTypeArguments.FirstOrDefault();
            if (genericArg == null)
            {
                return BadRequest(new { message = "Could not determine tree type" });
            }

            // Deserialize the payload JSON to the correct type
            object? payload;
            try
            {
                payload = JsonSerializer.Deserialize(request.PayloadJson, genericArg);
                if (payload == null)
                {
                    return BadRequest(new { message = "Invalid JSON payload" });
                }
            }
            catch (JsonException ex)
            {
                return BadRequest(new { message = $"JSON parsing failed: {ex.Message}" });
            }

            // Call Stash method
            var stashMethod = treeType.GetMethod("Stash");
            if (stashMethod == null)
            {
                return StatusCode(500, new { message = "Could not access Stash method" });
            }

            try
            {
                stashMethod.Invoke(tree, new object[] { request.Id, payload });
                return Ok(new { message = $"Nut '{request.Id}' created successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Stash failed: {ex.Message}" });
            }
        }

        [HttpPut("{typeName}/nut/{id}")]
        public ActionResult UpdateNut(string typeName, string id, [FromBody] UpdateNutRequest request)
        {
            var tree = _grove.GetTreeByTypeName(typeName);
            if (tree == null)
            {
                return NotFound(new { message = $"Tree '{typeName}' not found" });
            }

            var treeType = tree.GetType();
            var genericArg = treeType.GenericTypeArguments.FirstOrDefault();
            if (genericArg == null)
            {
                return BadRequest(new { message = "Could not determine tree type" });
            }

            // Deserialize the payload JSON to the correct type
            object? payload;
            try
            {
                payload = JsonSerializer.Deserialize(request.PayloadJson, genericArg);
                if (payload == null)
                {
                    return BadRequest(new { message = "Invalid JSON payload" });
                }
            }
            catch (JsonException ex)
            {
                return BadRequest(new { message = $"JSON parsing failed: {ex.Message}" });
            }

            // Call Stash method (which will update existing)
            var stashMethod = treeType.GetMethod("Stash");
            if (stashMethod == null)
            {
                return StatusCode(500, new { message = "Could not access Stash method" });
            }

            try
            {
                stashMethod.Invoke(tree, new object[] { id, payload });
                return Ok(new { message = $"Nut '{id}' updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Update failed: {ex.Message}" });
            }
        }

        [HttpDelete("{typeName}/nut/{id}")]
        public ActionResult DeleteNut(string typeName, string id)
        {
            var tree = _grove.GetTreeByTypeName(typeName);
            if (tree == null)
            {
                return NotFound(new { message = $"Tree '{typeName}' not found" });
            }

            var treeType = tree.GetType();
            var tossMethod = treeType.GetMethod("Toss");
            if (tossMethod == null)
            {
                return StatusCode(500, new { message = "Could not access Toss method" });
            }

            try
            {
                tossMethod.Invoke(tree, new object[] { id });
                return Ok(new { message = $"Nut '{id}' deleted successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Delete failed: {ex.Message}" });
            }
        }

        [HttpGet("{typeName}/nut/{id}/history")]
        public ActionResult<NutHistoryDto> GetNutHistory(string typeName, string id)
        {
            var tree = _grove.GetTreeByTypeName(typeName);
            if (tree == null)
            {
                return NotFound(new { message = $"Tree '{typeName}' not found" });
            }

            var treeType = tree.GetType();
            var genericArg = treeType.GenericTypeArguments.FirstOrDefault();
            if (genericArg == null)
            {
                return BadRequest(new { message = "Could not determine tree type" });
            }

            // Get trunk to check if history is supported
            var trunkField = treeType.GetField("_trunk",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            var trunk = trunkField?.GetValue(tree);

            if (trunk == null)
            {
                return StatusCode(500, new { message = "Could not access trunk" });
            }

            // Check if trunk supports history
            var canGetHistoryMethod = typeof(TrunkCapabilitiesExtensions)
                .GetMethod("CanGetHistory")
                ?.MakeGenericMethod(genericArg);

            if (canGetHistoryMethod != null)
            {
                var canGetHistory = (bool)(canGetHistoryMethod.Invoke(null, new[] { trunk }) ?? false);
                if (!canGetHistory)
                {
                    return BadRequest(new { message = $"Tree '{typeName}' does not support history" });
                }
            }

            // Get history using the trunk's GetHistory method
            var trunkType = trunk.GetType();
            var getHistoryMethod = trunkType.GetMethod("GetHistory");
            if (getHistoryMethod == null)
            {
                return StatusCode(500, new { message = "Trunk does not implement GetHistory" });
            }

            try
            {
                var history = getHistoryMethod.Invoke(trunk, new object[] { id }) as System.Collections.IEnumerable;
                if (history == null)
                {
                    return NotFound(new { message = $"No history found for nut '{id}'" });
                }

                var historyList = new List<NutVersionDto>();
                foreach (var item in history)
                {
                    if (item == null) continue;

                    var itemType = item.GetType();
                    var payloadProp = itemType.GetProperty("Payload");
                    var timestampProp = itemType.GetProperty("Timestamp");
                    var versionProp = itemType.GetProperty("Version");

                    var payload = payloadProp?.GetValue(item);
                    var payloadJson = payload != null
                        ? JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })
                        : "null";

                    historyList.Add(new NutVersionDto
                    {
                        Version = (int)(versionProp?.GetValue(item) ?? 0),
                        PayloadJson = payloadJson,
                        Timestamp = (DateTime)(timestampProp?.GetValue(item) ?? DateTime.MinValue)
                    });
                }

                return Ok(new NutHistoryDto
                {
                    Id = id,
                    History = historyList
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Failed to get history: {ex.Message}" });
            }
        }
    }
}
