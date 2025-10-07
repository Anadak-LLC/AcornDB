using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AcornDB;
using AcornDB.Models;
using AcornDB.Storage;
using AcornDB.Sync;

namespace AcornVisualizer.Services
{
    /// <summary>
    /// Service for network discovery of AcornDB groves
    /// Broadcasts presence and listens for other groves
    /// </summary>
    public class DiscoveryService : IDisposable
    {
        private const int DiscoveryPort = 50505;
        private readonly Grove _grove;
        private readonly int _visualizerPort;
        private UdpClient? _broadcastClient;
        private UdpClient? _listenClient;
        private CancellationTokenSource? _cancellationSource;
        private bool _isRunning = false;
        private readonly ConcurrentDictionary<string, DiscoveredGrove> _discoveredGroves = new();

        public DiscoveryService(Grove grove, int visualizerPort = 5100)
        {
            _grove = grove;
            _visualizerPort = visualizerPort;
        }

        public bool IsRunning => _isRunning;

        public IEnumerable<DiscoveredGrove> GetDiscoveredGroves()
        {
            return _discoveredGroves.Values;
        }

        /// <summary>
        /// Start broadcasting and listening for groves
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _cancellationSource = new CancellationTokenSource();

            // Start broadcasting
            Task.Run(() => BroadcastLoop(_cancellationSource.Token), _cancellationSource.Token);

            // Start listening
            Task.Run(() => ListenLoop(_cancellationSource.Token), _cancellationSource.Token);

            Console.WriteLine($"🔍 Discovery service started on port {DiscoveryPort}");
        }

        /// <summary>
        /// Stop broadcasting and listening
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            _cancellationSource?.Cancel();
            _broadcastClient?.Close();
            _listenClient?.Close();

            Console.WriteLine("🔍 Discovery service stopped");
        }

        private async Task BroadcastLoop(CancellationToken cancellationToken)
        {
            try
            {
                _broadcastClient = new UdpClient();
                _broadcastClient.EnableBroadcast = true;
                var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Broadcast message format: "ACORN_VISUALIZER:<port>:<tree_count>"
                        var message = $"ACORN_VISUALIZER:{_visualizerPort}:{_grove.TreeCount}";
                        var data = Encoding.UTF8.GetBytes(message);

                        await _broadcastClient.SendAsync(data, data.Length, endpoint);
                        Console.WriteLine($"📡 Broadcast: {message}");

                        await Task.Delay(5000, cancellationToken); // Broadcast every 5 seconds
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Broadcast error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Broadcast loop failed: {ex.Message}");
            }
        }

        private async Task ListenLoop(CancellationToken cancellationToken)
        {
            try
            {
                _listenClient = new UdpClient(DiscoveryPort);
                _listenClient.EnableBroadcast = true;

                Console.WriteLine($"👂 Listening for groves on port {DiscoveryPort}");

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = await _listenClient.ReceiveAsync();
                        var message = Encoding.UTF8.GetString(result.Buffer);

                        if (message.StartsWith("ACORN_VISUALIZER:"))
                        {
                            ProcessDiscoveredGrove(message, result.RemoteEndPoint);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Listen error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Listen loop failed: {ex.Message}");
            }
        }

        private void ProcessDiscoveredGrove(string message, IPEndPoint remoteEndpoint)
        {
            try
            {
                // Parse message: "ACORN_VISUALIZER:<port>:<tree_count>"
                var parts = message.Split(':');
                if (parts.Length < 3)
                {
                    return;
                }

                var port = int.Parse(parts[1]);
                var treeCount = int.Parse(parts[2]);
                var groveUrl = $"http://{remoteEndpoint.Address}:{port}";

                // Ignore self
                if (port == _visualizerPort && IsLocalAddress(remoteEndpoint.Address))
                {
                    return;
                }

                var groveId = $"{remoteEndpoint.Address}:{port}";

                // Add or update discovered grove
                _discoveredGroves.AddOrUpdate(
                    groveId,
                    new DiscoveredGrove
                    {
                        Id = groveId,
                        Address = remoteEndpoint.Address.ToString(),
                        Port = port,
                        Url = groveUrl,
                        TreeCount = treeCount,
                        LastSeen = DateTime.UtcNow,
                        IsConnected = false
                    },
                    (key, existing) =>
                    {
                        existing.TreeCount = treeCount;
                        existing.LastSeen = DateTime.UtcNow;
                        return existing;
                    });

                Console.WriteLine($"🌳 Discovered grove: {groveId} with {treeCount} trees");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to process discovered grove: {ex.Message}");
            }
        }

        private bool IsLocalAddress(IPAddress address)
        {
            // Check if it's localhost or our machine's IP
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            try
            {
                var hostName = Dns.GetHostName();
                var hostAddresses = Dns.GetHostAddresses(hostName);

                foreach (var hostAddress in hostAddresses)
                {
                    if (hostAddress.Equals(address))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // If we can't determine, assume it's not local
            }

            return false;
        }

        /// <summary>
        /// Connect to a discovered grove and sync trees
        /// </summary>
        public async Task<bool> ConnectToGrove(string groveId)
        {
            if (!_discoveredGroves.TryGetValue(groveId, out var discoveredGrove))
            {
                return false;
            }

            try
            {
                // For each tree in our grove, attempt to sync with the remote grove
                foreach (var treeInfo in _grove.GetTreeInfo())
                {
                    var tree = _grove.GetTreeByTypeName(treeInfo.Type);
                    if (tree == null) continue;

                    // Create a branch to the remote grove
                    var remoteUrl = $"{discoveredGrove.Url}/api/bark/{treeInfo.Type}";
                    var branch = new Branch(remoteUrl);

                    // Get tree type
                    var treeType = tree.GetType();
                    var genericArg = treeType.GenericTypeArguments.FirstOrDefault();
                    if (genericArg == null) continue;

                    // Call ShakeAsync to sync
                    var shakeMethod = typeof(Branch)
                        .GetMethod("ShakeAsync")
                        ?.MakeGenericMethod(genericArg);

                    if (shakeMethod != null)
                    {
                        await (Task)shakeMethod.Invoke(branch, new object[] { tree })!;
                    }
                }

                discoveredGrove.IsConnected = true;
                Console.WriteLine($"✅ Connected to grove: {groveId}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to connect to grove {groveId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clean up old discovered groves (not seen in 30+ seconds)
        /// </summary>
        public void CleanupOldGroves()
        {
            var threshold = DateTime.UtcNow.AddSeconds(-30);
            var oldGroves = _discoveredGroves
                .Where(kvp => kvp.Value.LastSeen < threshold)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var groveId in oldGroves)
            {
                if (_discoveredGroves.TryRemove(groveId, out var _))
                {
                    Console.WriteLine($"🗑️ Removed stale grove: {groveId}");
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _broadcastClient?.Dispose();
            _listenClient?.Dispose();
            _cancellationSource?.Dispose();
        }
    }

    public class DiscoveredGrove
    {
        public string Id { get; set; } = "";
        public string Address { get; set; } = "";
        public int Port { get; set; }
        public string Url { get; set; } = "";
        public int TreeCount { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsConnected { get; set; }
    }
}
