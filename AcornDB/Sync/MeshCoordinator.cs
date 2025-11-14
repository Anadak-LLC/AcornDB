using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Logging;

namespace AcornDB.Sync
{
    /// <summary>
    /// Coordinates synchronization across a mesh network of Trees
    /// Handles topology management and prevents sync loops
    /// </summary>
    public class MeshCoordinator<T> where T : class
    {
        private readonly Dictionary<string, Tree<T>> _nodes = new();
        private readonly Dictionary<string, List<string>> _topology = new();
        private readonly object _topologyLock = new();

        /// <summary>
        /// Add a node to the mesh
        /// </summary>
        public void AddNode(string nodeId, Tree<T> tree)
        {
            lock (_topologyLock)
            {
                tree.NodeId = nodeId;
                _nodes[nodeId] = tree;
                if (!_topology.ContainsKey(nodeId))
                {
                    _topology[nodeId] = new List<string>();
                }
            }
        }

        /// <summary>
        /// Connect two nodes bidirectionally (creates a mesh link)
        /// </summary>
        public void ConnectNodes(string nodeA, string nodeB)
        {
            lock (_topologyLock)
            {
                if (!_nodes.ContainsKey(nodeA) || !_nodes.ContainsKey(nodeB))
                    throw new InvalidOperationException("Both nodes must be added before connecting");

                // Add bidirectional connection to topology
                if (!_topology[nodeA].Contains(nodeB))
                    _topology[nodeA].Add(nodeB);

                if (!_topology[nodeB].Contains(nodeA))
                    _topology[nodeB].Add(nodeA);

                // Create in-process tangles for bidirectional sync
                var treeA = _nodes[nodeA];
                var treeB = _nodes[nodeB];

                // A → B
                var tangleAB = new Tangle<T>(treeA, new InProcessBranch<T>(treeB), $"Tangle_{nodeA}→{nodeB}");

                // B → A
                var tangleBA = new Tangle<T>(treeB, new InProcessBranch<T>(treeA), $"Tangle_{nodeB}→{nodeA}");

                AcornLog.Info($"> 🕸️  Mesh link created: {nodeA} ↔ {nodeB}");
            }
        }

        /// <summary>
        /// Create a full mesh (all nodes connected to all other nodes)
        /// </summary>
        public void CreateFullMesh()
        {
            lock (_topologyLock)
            {
                var nodeIds = _nodes.Keys.ToList();

                for (int i = 0; i < nodeIds.Count; i++)
                {
                    for (int j = i + 1; j < nodeIds.Count; j++)
                    {
                        ConnectNodes(nodeIds[i], nodeIds[j]);
                    }
                }

                AcornLog.Info($"> 🕸️  Full mesh created with {nodeIds.Count} nodes");
            }
        }

        /// <summary>
        /// Create a ring topology (each node connects to next, last connects to first)
        /// </summary>
        public void CreateRing()
        {
            lock (_topologyLock)
            {
                var nodeIds = _nodes.Keys.ToList();

                for (int i = 0; i < nodeIds.Count; i++)
                {
                    var nextIndex = (i + 1) % nodeIds.Count;
                    ConnectNodes(nodeIds[i], nodeIds[nextIndex]);
                }

                AcornLog.Info($"> 🔗 Ring topology created with {nodeIds.Count} nodes");
            }
        }

        /// <summary>
        /// Create a star topology (all nodes connect to a central hub)
        /// </summary>
        public void CreateStar(string hubNodeId)
        {
            lock (_topologyLock)
            {
                if (!_nodes.ContainsKey(hubNodeId))
                    throw new InvalidOperationException($"Hub node {hubNodeId} not found");

                foreach (var nodeId in _nodes.Keys)
                {
                    if (nodeId != hubNodeId)
                    {
                        ConnectNodes(hubNodeId, nodeId);
                    }
                }

                AcornLog.Info($"> ⭐ Star topology created with hub: {hubNodeId}");
            }
        }

        /// <summary>
        /// Synchronize all nodes in the mesh
        /// Triggers a shake on all tangles to pull changes
        /// </summary>
        public void SynchronizeAll()
        {
            lock (_topologyLock)
            {
                foreach (var tree in _nodes.Values)
                {
                    tree.Shake();
                }
            }

            AcornLog.Info($"> 🌊 Mesh synchronized: {_nodes.Count} nodes");
        }

        /// <summary>
        /// Get topology information
        /// </summary>
        public MeshTopology GetTopology()
        {
            lock (_topologyLock)
            {
                return new MeshTopology
                {
                    TotalNodes = _nodes.Count,
                    TotalConnections = _topology.Values.Sum(list => list.Count) / 2,
                    Connections = _topology.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList())
                };
            }
        }

        /// <summary>
        /// Get a node by ID
        /// </summary>
        public Tree<T>? GetNode(string nodeId)
        {
            lock (_topologyLock)
            {
                return _nodes.TryGetValue(nodeId, out var node) ? node : null;
            }
        }

        /// <summary>
        /// Get all mesh statistics
        /// </summary>
        public MeshNetworkStats GetNetworkStats()
        {
            lock (_topologyLock)
            {
                return new MeshNetworkStats
                {
                    Topology = GetTopology(),
                    NodeStats = _nodes.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.GetMeshStats()
                    )
                };
            }
        }
    }
}
