using Microsoft.AspNetCore.Mvc;
using AcornDB.Models;
using AcornDB.Sync;
using AcornDB.Storage;
using AcornVisualizer.Models;

namespace AcornVisualizer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GroveManagementController : ControllerBase
    {
        private readonly Grove _grove;

        public GroveManagementController(Grove grove)
        {
            _grove = grove;
        }

        [HttpPost("register-tree")]
        public ActionResult RegisterRemoteTree([FromBody] RegisterTreeRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TypeName))
            {
                return BadRequest(new { message = "TypeName is required" });
            }

            if (string.IsNullOrWhiteSpace(request.RemoteUrl))
            {
                return BadRequest(new { message = "RemoteUrl is required" });
            }

            var tree = _grove.GetTreeByTypeName(request.TypeName);
            if (tree == null)
            {
                return NotFound(new { message = $"Tree '{request.TypeName}' not found in grove" });
            }

            var treeType = tree.GetType();
            var genericArg = treeType.GenericTypeArguments.FirstOrDefault();
            if (genericArg == null)
            {
                return BadRequest(new { message = "Could not determine tree type" });
            }

            try
            {
                // Create a Branch to connect to the remote tree
                var branch = new Branch(request.RemoteUrl);

                // Start synchronization using ShakeAsync
                var shakeMethod = typeof(Branch)
                    .GetMethod("ShakeAsync")
                    ?.MakeGenericMethod(genericArg);

                if (shakeMethod != null)
                {
                    // Fire and forget - async sync in background
                    _ = shakeMethod.Invoke(branch, new object[] { tree });
                }

                return Ok(new
                {
                    message = $"Remote tree '{request.TypeName}' registered successfully. Synchronization started.",
                    remoteUrl = request.RemoteUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Registration failed: {ex.Message}" });
            }
        }

        [HttpGet("trees")]
        public ActionResult<List<TreeInfoDto>> GetTrees()
        {
            var trees = _grove.GetTreeInfo();
            var treeInfos = new List<TreeInfoDto>();

            foreach (var t in trees)
            {
                // Get the actual tree object to query capabilities
                var tree = _grove.GetTreeByTypeName(t.Type);
                if (tree == null) continue;

                var treeType = tree.GetType();
                var genericArg = treeType.GenericTypeArguments.FirstOrDefault();
                if (genericArg == null) continue;

                // Get trunk capabilities
                var trunkField = treeType.GetField("_trunk",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var trunk = trunkField?.GetValue(tree);

                string trunkTypeName = "Unknown";
                bool supportsHistory = false;
                bool supportsSync = false;
                bool isDurable = false;

                if (trunk != null)
                {
                    var capsMethod = typeof(TrunkCapabilitiesExtensions)
                        .GetMethod("GetCapabilities")
                        ?.MakeGenericMethod(genericArg);

                    if (capsMethod != null)
                    {
                        var caps = capsMethod.Invoke(null, new[] { trunk });
                        if (caps != null)
                        {
                            var capsType = caps.GetType();
                            trunkTypeName = capsType.GetProperty("TrunkType")?.GetValue(caps)?.ToString() ?? "Unknown";
                            supportsHistory = (bool)(capsType.GetProperty("SupportsHistory")?.GetValue(caps) ?? false);
                            supportsSync = (bool)(capsType.GetProperty("SupportsSync")?.GetValue(caps) ?? false);
                            isDurable = (bool)(capsType.GetProperty("IsDurable")?.GetValue(caps) ?? false);
                        }
                    }
                }

                treeInfos.Add(new TreeInfoDto
                {
                    TypeName = t.Type,
                    NutCount = t.NutCount,
                    TrunkType = trunkTypeName,
                    SupportsHistory = supportsHistory,
                    SupportsSync = supportsSync,
                    IsDurable = isDurable
                });
            }

            return Ok(treeInfos);
        }

        [HttpDelete("tree/{typeName}/clear")]
        public ActionResult ClearTree(string typeName)
        {
            var tree = _grove.GetTreeByTypeName(typeName);
            if (tree == null)
            {
                return NotFound(new { message = $"Tree '{typeName}' not found" });
            }

            var treeType = tree.GetType();
            var clearMethod = treeType.GetMethod("Clear");
            if (clearMethod == null)
            {
                return StatusCode(500, new { message = "Could not access Clear method" });
            }

            try
            {
                clearMethod.Invoke(tree, null);
                return Ok(new { message = $"Tree '{typeName}' cleared successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Clear failed: {ex.Message}" });
            }
        }
    }

    public class TreeInfoDto
    {
        public string TypeName { get; set; } = "";
        public int NutCount { get; set; }
        public string TrunkType { get; set; } = "";
        public bool SupportsHistory { get; set; }
        public bool SupportsSync { get; set; }
        public bool IsDurable { get; set; }
    }
}
