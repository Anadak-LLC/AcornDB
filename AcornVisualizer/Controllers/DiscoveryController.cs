using Microsoft.AspNetCore.Mvc;
using AcornVisualizer.Services;

namespace AcornVisualizer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DiscoveryController : ControllerBase
    {
        private readonly DiscoveryService _discoveryService;

        public DiscoveryController(DiscoveryService discoveryService)
        {
            _discoveryService = discoveryService;
        }

        [HttpPost("start")]
        public ActionResult StartDiscovery()
        {
            if (_discoveryService.IsRunning)
            {
                return Ok(new { message = "Discovery is already running", isRunning = true });
            }

            try
            {
                _discoveryService.Start();
                return Ok(new { message = "Discovery started successfully", isRunning = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Failed to start discovery: {ex.Message}" });
            }
        }

        [HttpPost("stop")]
        public ActionResult StopDiscovery()
        {
            if (!_discoveryService.IsRunning)
            {
                return Ok(new { message = "Discovery is not running", isRunning = false });
            }

            try
            {
                _discoveryService.Stop();
                return Ok(new { message = "Discovery stopped successfully", isRunning = false });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Failed to stop discovery: {ex.Message}" });
            }
        }

        [HttpGet("status")]
        public ActionResult GetStatus()
        {
            return Ok(new
            {
                isRunning = _discoveryService.IsRunning,
                discoveredGroves = _discoveryService.GetDiscoveredGroves().Select(g => new
                {
                    id = g.Id,
                    address = g.Address,
                    port = g.Port,
                    url = g.Url,
                    treeCount = g.TreeCount,
                    lastSeen = g.LastSeen,
                    isConnected = g.IsConnected
                })
            });
        }

        [HttpPost("connect/{groveId}")]
        public async Task<ActionResult> ConnectToGrove(string groveId)
        {
            if (string.IsNullOrWhiteSpace(groveId))
            {
                return BadRequest(new { message = "Grove ID is required" });
            }

            try
            {
                _discoveryService.CleanupOldGroves(); // Clean up stale groves first
                var success = await _discoveryService.ConnectToGrove(groveId);

                if (success)
                {
                    return Ok(new { message = $"Successfully connected to grove '{groveId}'", groveId });
                }
                else
                {
                    return NotFound(new { message = $"Grove '{groveId}' not found or connection failed" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Failed to connect to grove: {ex.Message}" });
            }
        }

        [HttpPost("cleanup")]
        public ActionResult CleanupOldGroves()
        {
            try
            {
                _discoveryService.CleanupOldGroves();
                return Ok(new { message = "Old groves cleaned up successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Cleanup failed: {ex.Message}" });
            }
        }
    }
}
