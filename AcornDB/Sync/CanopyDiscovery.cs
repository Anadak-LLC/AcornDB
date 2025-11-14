using System;
using AcornDB.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Models;

namespace AcornDB.Sync
{
    /// <summary>
    /// Canopy - Network-wide auto-discovery system for AcornDB groves and trees
    /// Uses UDP broadcast for LAN discovery and optional HTTP for WAN discovery
    /// </summary>
    public class CanopyDiscovery
    {
        private const int DiscoveryPort = 50505;
        private const int BroadcastIntervalMs = 5000;

        private readonly Grove _localGrove;
        private readonly int _httpPort;
        private readonly string _nodeId;
        private UdpClient? _udpClient;
        private CancellationTokenSource? _cancellationSource;

        private readonly ConcurrentDictionary<string, DiscoveredNode> _discoveredNodes = new();

        public IEnumerable<DiscoveredNode> DiscoveredNodes => _discoveredNodes.Values;

        public CanopyDiscovery(Grove grove, int httpPort)
        {
            _localGrove = grove;
            _httpPort = httpPort;
            _nodeId = Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Start broadcasting presence and listening for other nodes
        /// </summary>
        public void StartDiscovery(bool autoConnect = true)
        {
            _cancellationSource = new CancellationTokenSource();
            var token = _cancellationSource.Token;

            // Start broadcasting
            Task.Run(async () => await BroadcastPresence(token), token);

            // Start listening
            Task.Run(async () => await ListenForNodes(autoConnect, token), token);

            AcornLog.Info($"> 🌳 Canopy discovery started on port {DiscoveryPort}");
        }

        /// <summary>
        /// Stop discovery
        /// </summary>
        public void StopDiscovery()
        {
            _cancellationSource?.Cancel();
            _udpClient?.Close();
            AcornLog.Info($"> 🌳 Canopy discovery stopped");
        }

        private async Task BroadcastPresence(CancellationToken token)
        {
            using var udpBroadcast = new UdpClient();
            udpBroadcast.EnableBroadcast = true;
            var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var announcement = new CanopyAnnouncement
                    {
                        NodeId = _nodeId,
                        HttpPort = _httpPort,
                        TreeCount = _localGrove.TreeCount,
                        TreeTypes = _localGrove.GetTreeInfo().Select(t => t.Type).ToList(),
                        Timestamp = DateTime.UtcNow
                    };

                    var json = JsonSerializer.Serialize(announcement);
                    var data = Encoding.UTF8.GetBytes($"CANOPY:{json}");

                    await udpBroadcast.SendAsync(data, data.Length, endpoint);
                    await Task.Delay(BroadcastIntervalMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AcornLog.Info($"> ⚠️ Canopy broadcast error: {ex.Message}");
                }
            }
        }

        private async Task ListenForNodes(bool autoConnect, CancellationToken token)
        {
            try
            {
                _udpClient = new UdpClient(DiscoveryPort);
                _udpClient.EnableBroadcast = true;

                while (!token.IsCancellationRequested)
                {
                    var result = await _udpClient.ReceiveAsync();
                    var message = Encoding.UTF8.GetString(result.Buffer);

                    if (message.StartsWith("CANOPY:"))
                    {
                        var json = message.Substring(7);
                        var announcement = JsonSerializer.Deserialize<CanopyAnnouncement>(json);

                        if (announcement != null && announcement.NodeId != _nodeId)
                        {
                            var nodeKey = $"{result.RemoteEndPoint.Address}:{announcement.HttpPort}";
                            var remoteUrl = $"http://{result.RemoteEndPoint.Address}:{announcement.HttpPort}";

                            var node = new DiscoveredNode
                            {
                                NodeId = announcement.NodeId,
                                Address = result.RemoteEndPoint.Address.ToString(),
                                HttpPort = announcement.HttpPort,
                                RemoteUrl = remoteUrl,
                                TreeCount = announcement.TreeCount,
                                TreeTypes = announcement.TreeTypes,
                                LastSeen = DateTime.UtcNow,
                                DiscoveredAt = _discoveredNodes.ContainsKey(nodeKey)
                                    ? _discoveredNodes[nodeKey].DiscoveredAt
                                    : DateTime.UtcNow
                            };

                            _discoveredNodes.AddOrUpdate(nodeKey, node, (k, old) => node);

                            if (autoConnect && node.DiscoveredAt == DateTime.UtcNow)
                            {
                                AcornLog.Info($"> 🌳 Canopy: Discovered {nodeKey} ({node.TreeCount} trees)");
                                ConnectToNode(node);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                AcornLog.Info($"> ⚠️ Canopy listener error: {ex.Message}");
            }
        }

        private void ConnectToNode(DiscoveredNode node)
        {
            try
            {
                _localGrove.EntangleAll(node.RemoteUrl);
                AcornLog.Info($"> 🔗 Auto-connected to {node.RemoteUrl}");
            }
            catch (Exception ex)
            {
                AcornLog.Info($"> ⚠️ Failed to connect to {node.RemoteUrl}: {ex.Message}");
            }
        }

        /// <summary>
        /// Manually connect to a discovered node
        /// </summary>
        public void ConnectTo(string nodeKey)
        {
            if (_discoveredNodes.TryGetValue(nodeKey, out var node))
            {
                ConnectToNode(node);
            }
        }

        /// <summary>
        /// Get discovery statistics
        /// </summary>
        public CanopyStats GetStats()
        {
            var activeNodes = _discoveredNodes.Values
                .Where(n => (DateTime.UtcNow - n.LastSeen).TotalSeconds < 30)
                .ToList();

            return new CanopyStats
            {
                LocalNodeId = _nodeId,
                TotalDiscovered = _discoveredNodes.Count,
                ActiveNodes = activeNodes.Count,
                TotalTrees = activeNodes.Sum(n => n.TreeCount),
                UniqueTreeTypes = activeNodes
                    .SelectMany(n => n.TreeTypes)
                    .Distinct()
                    .Count()
            };
        }

        /// <summary>
        /// Clear stale nodes (not seen in 30+ seconds)
        /// </summary>
        public void ClearStaleNodes()
        {
            var staleKeys = _discoveredNodes
                .Where(kvp => (DateTime.UtcNow - kvp.Value.LastSeen).TotalSeconds > 30)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                _discoveredNodes.TryRemove(key, out _);
                AcornLog.Info($"> 🌳 Removed stale node: {key}");
            }
        }
    }
}
