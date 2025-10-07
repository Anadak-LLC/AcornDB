
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AcornDB
{
    public partial class Nut<T>
    {
        /// <summary>
        /// Unique identifier for this specific change
        /// Used for loop prevention in mesh sync to avoid infinite propagation
        /// </summary>
        public Guid ChangeId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Origin node ID where this change was first created
        /// Helps track change provenance in mesh networks
        /// </summary>
        public string? OriginNodeId { get; set; }

        /// <summary>
        /// Hop count - number of nodes this change has traversed
        /// Prevents excessive propagation in large meshes
        /// </summary>
        public int HopCount { get; set; } = 0;
    }

    public partial class Tree<T>
    {
        private readonly HashSet<Guid> _recentChangeIds = new();
        private readonly ConcurrentQueue<Guid> _changeIdQueue = new();
        private const int ChangeIdMemoryLimit = 1000; // Increased for larger meshes
        private readonly object _changeIdLock = new();

        /// <summary>
        /// Maximum number of hops a change can traverse in the mesh
        /// Prevents infinite propagation in complex topologies
        /// </summary>
        public int MaxHopCount { get; set; } = 10;

        /// <summary>
        /// Unique identifier for this tree/node in the mesh
        /// </summary>
        public string NodeId { get; set; } = Guid.NewGuid().ToString();

        private bool HasSeenChange(Guid changeId)
        {
            lock (_changeIdLock)
            {
                return _recentChangeIds.Contains(changeId);
            }
        }

        private void RememberChange(Guid changeId)
        {
            lock (_changeIdLock)
            {
                if (_recentChangeIds.Add(changeId))
                {
                    _changeIdQueue.Enqueue(changeId);

                    // Maintain memory limit
                    while (_changeIdQueue.Count > ChangeIdMemoryLimit)
                    {
                        if (_changeIdQueue.TryDequeue(out var oldId))
                        {
                            _recentChangeIds.Remove(oldId);
                        }
                    }
                }
            }
        }

        internal void PushToAllTangles(string key, T item)
        {
            var shell = new Nut<T>
            {
                Id = key,
                Payload = item,
                Timestamp = DateTime.UtcNow,
                OriginNodeId = NodeId,
                HopCount = 0
            };

            RememberChange(shell.ChangeId);

            foreach (var tangle in _tangles)
            {
                tangle.PushUpdate(key, item);
            }
        }

        internal bool ShouldApplyChange(Nut<T> nut)
        {
            // Already seen this exact change?
            if (HasSeenChange(nut.ChangeId))
                return false;

            // Originated from this node? (loop back to self)
            if (nut.OriginNodeId == NodeId)
                return false;

            // Exceeded hop limit? (prevent excessive propagation)
            if (nut.HopCount >= MaxHopCount)
                return false;

            RememberChange(nut.ChangeId);
            return true;
        }

        /// <summary>
        /// Propagate a change to all connected tangles (mesh sync)
        /// Increments hop count and preserves change ID and origin
        /// </summary>
        internal void PropagateToTangles(string key, Nut<T> nut)
        {
            if (!ShouldApplyChange(nut))
                return;

            // Increment hop count for propagation
            var propagatedNut = new Nut<T>
            {
                Id = nut.Id,
                Payload = nut.Payload,
                Timestamp = nut.Timestamp,
                ChangeId = nut.ChangeId, // Preserve original change ID
                OriginNodeId = nut.OriginNodeId ?? NodeId,
                HopCount = nut.HopCount + 1
            };

            foreach (var tangle in _tangles)
            {
                // Don't send back to the tangle we received from (handled by Tangle)
                tangle.PushUpdate(key, propagatedNut.Payload);
            }
        }

        /// <summary>
        /// Get statistics about loop prevention and mesh sync
        /// </summary>
        public MeshSyncStats GetMeshStats()
        {
            lock (_changeIdLock)
            {
                return new MeshSyncStats
                {
                    NodeId = NodeId,
                    TrackedChangeIds = _recentChangeIds.Count,
                    ActiveTangles = _tangles.Count,
                    MaxHopCount = MaxHopCount
                };
            }
        }
    }

    /// <summary>
    /// Statistics about mesh sync and loop prevention
    /// </summary>
    public class MeshSyncStats
    {
        public string NodeId { get; set; } = "";
        public int TrackedChangeIds { get; set; }
        public int ActiveTangles { get; set; }
        public int MaxHopCount { get; set; }
    }

    public partial class Tangle<T>
    {
        private readonly HashSet<Guid> _processedChanges = new();

        public void ApplyRemoteChange(string key, Nut<T> remoteNut, Tree<T> tree)
        {
            // Check if we've already processed this change in this tangle
            if (_processedChanges.Contains(remoteNut.ChangeId))
                return;

            _processedChanges.Add(remoteNut.ChangeId);

            // Apply the change if the tree hasn't seen it
            if (tree.ShouldApplyChange(remoteNut))
            {
                tree.Squabble(key, remoteNut);

                // Propagate to other tangles (mesh sync)
                tree.PropagateToTangles(key, remoteNut);
            }
        }
    }
}
