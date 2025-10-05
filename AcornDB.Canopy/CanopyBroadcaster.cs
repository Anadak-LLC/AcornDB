
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using System.Threading.Tasks;
using AcornDB.Models;

namespace AcornDB
{
    public class CanopyBroadcaster
    {
        private readonly Grove _grove;
        private readonly IHubContext<CanopyHub> _hubContext;
        private readonly System.Timers.Timer _timer;

        public CanopyBroadcaster(Grove grove, IHubContext<CanopyHub> hubContext)
        {
            _grove = grove;
            _hubContext = hubContext;
            _timer = new System.Timers.Timer(3000); // Every 3 seconds
            _timer.Elapsed += async (s, e) => await Broadcast();
            _timer.Start();
        }

        public async Task Broadcast()
        {
            var stats = new
            {
                Time = DateTime.UtcNow,
                TreeCount = _grove.TreeCount,
                Tangles = _grove.GetTangleStats()
            };

            var json = JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true });
            await _hubContext.Clients.All.SendAsync("ReceiveUpdate", json);
        }
    }
}
