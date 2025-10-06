
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using AcornDB.Models;

namespace AcornDB
{
    public class AcornBroadcaster
    {
        private const int DiscoveryPort = 50505;
        private readonly string _message;
        private UdpClient _udp;

        public AcornBroadcaster(int hardwoodPort)
        {
            _message = $"ACORN:{hardwoodPort}";
        }

        public void StartBroadcast()
        {
            Task.Run(async () =>
            {
                _udp = new UdpClient();
                var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
                var data = Encoding.UTF8.GetBytes(_message);

                while (true)
                {
                    await _udp.SendAsync(data, data.Length, endpoint);
                    await Task.Delay(5000); // Broadcast every 5 sec
                }
            });
        }

        public static async Task ListenAndEntangle(Models.Grove grove)
        {
            var udpClient = new UdpClient(DiscoveryPort);
            while (true)
            {
                var result = await udpClient.ReceiveAsync();
                var msg = Encoding.UTF8.GetString(result.Buffer);
                if (msg.StartsWith("ACORN:"))
                {
                    var port = msg.Split(":")[1];
                    var remote = $"http://{result.RemoteEndPoint.Address}:{port}";
                    grove.EntangleAll(remote);
                }
            }
        }
    }

    public class TangleStats
    {
        public string TreeType { get; set; }
        public string LocalTreeId { get; set; }
        public string RemoteTreeId { get; set; }
        public string RemoteUrl { get; set; }
        public string RemoteAddress { get; set; }
        public int TotalPushes { get; set; }
        public int TotalPulls { get; set; }
        public DateTime? LastSyncTime { get; set; }
        public string Status { get; set; }
    }

}

namespace AcornDB.Models
{
    public partial class Grove
    {
        public List<TangleStats> GetTangleStats()
        {
            var list = new List<TangleStats>();
            foreach (var kvp in _trees)
            {
                dynamic tree = kvp.Value;
                if (tree._tangles == null) continue;

                foreach (dynamic tangle in tree._tangles)
                {
                    list.Add(new TangleStats
                    {
                        TreeType = typeof(TangleStats).GenericTypeArguments?[0]?.Name ?? "Unknown",
                        RemoteAddress = tangle.RemoteUrl,
                        TotalPushes = tangle.TotalPushes,
                        TotalPulls = tangle.TotalPulls,
                        LastSyncTime = tangle.LastSyncTime,
                        Status = tangle.LastStatus
                    });
                }
            }
            return list;
        }
    }
}
