
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace AcornDB
{
    public class CanopyHub : Hub
    {
        public async Task SendUpdate(string message)
        {
            await Clients.All.SendAsync("ReceiveUpdate", message);
        }
    }
}
