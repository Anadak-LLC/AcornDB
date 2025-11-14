using AcornDB.Sync;
using AcornDB.Logging;
using System;
using System.Linq;

namespace AcornDB
{
    public partial class Tree<T> where T : class
    {
        // Tree identity for anti-loop tracking
        public string TreeId { get; private set; } = Guid.NewGuid().ToString();

        // Sequence counter for unique leaf IDs
        private long _leafSequenceCounter = 0;

        // Processed leaves cache for deduplication (simple dictionary with size limit)
        private readonly Dictionary<string, DateTime> _processedLeaves = new();
        private const int MaxProcessedLeaves = 10_000;

        // Maximum hops a leaf can take before being dropped (prevents infinite propagation)
        private const int MaxLeafHops = 10;

        /// <summary>
        /// Handle an incoming leaf from another tree
        /// Includes anti-loop protection and deduplication
        /// </summary>
        public void HandleLeaf(Leaf<T> leaf)
        {
            // 1. Already processed this exact leaf?
            if (_processedLeaves.ContainsKey(leaf.LeafId))
            {
                AcornLog.Info($"> 🍃 Leaf {leaf.LeafId} already processed, skipping");
                return;
            }

            // 2. Are we the origin? (Don't loop back to ourselves)
            if (leaf.OriginTreeId == TreeId)
            {
                AcornLog.Info($"> 🍃 Leaf {leaf.LeafId} originated from us, skipping");
                return;
            }

            // 3. Already visited this tree? (Path tracking)
            if (leaf.HasVisited(TreeId))
            {
                AcornLog.Info($"> 🍃 Leaf {leaf.LeafId} already visited this tree, skipping");
                return;
            }

            // 4. Exceeded hop limit? (Safety mechanism)
            if (leaf.IsExpired(MaxLeafHops))
            {
                AcornLog.Info($"> 🍃 Leaf {leaf.LeafId} exceeded hop limit ({MaxLeafHops}), dropping");
                return;
            }

            // 5. Apply the change locally (WITHOUT propagating to avoid loops)
            ApplyLeafLocally(leaf);

            // 6. Mark this leaf as processed (with LRU eviction if needed)
            if (_processedLeaves.Count >= MaxProcessedLeaves)
            {
                // Simple LRU: remove oldest entry
                var oldest = _processedLeaves.OrderBy(kvp => kvp.Value).First().Key;
                _processedLeaves.Remove(oldest);
            }
            _processedLeaves[leaf.LeafId] = DateTime.UtcNow;

            // 7. Mark this tree as visited and increment hop count
            leaf.MarkVisited(TreeId);
            leaf.HopCount++;

            // 8. Propagate to our branches (they haven't seen this leaf yet)
            PropagateLeaf(leaf);
        }

        /// <summary>
        /// Apply a leaf's change locally without propagating
        /// </summary>
        private void ApplyLeafLocally(Leaf<T> leaf)
        {
            switch (leaf.Type)
            {
                case LeafType.Stash:
                    if (leaf.Data != null)
                    {
                        // Use Squabble for incoming stashes (handles conflicts)
                        Squabble(leaf.Key, leaf.Data);
                        AcornLog.Info($"> 🍃 Applied stash leaf: {leaf.Key}");
                    }
                    break;

                case LeafType.Toss:
                    // Delete locally without propagating (propagate=false)
                    Toss(leaf.Key, propagate: false);
                    AcornLog.Info($"> 🍃 Applied toss leaf: {leaf.Key}");
                    break;

                case LeafType.Squabble:
                    if (leaf.Data != null)
                    {
                        Squabble(leaf.Key, leaf.Data);
                        AcornLog.Info($"> 🍃 Applied squabble leaf: {leaf.Key}");
                    }
                    break;

                case LeafType.Update:
                    if (leaf.Data != null)
                    {
                        // Update is like stash but for existing items
                        Squabble(leaf.Key, leaf.Data);
                        AcornLog.Info($"> 🍃 Applied update leaf: {leaf.Key}");
                    }
                    break;
            }
        }

        /// <summary>
        /// Propagate a leaf to all capable branches
        /// Checks each branch's capabilities before sending
        /// </summary>
        private void PropagateLeaf(Leaf<T> leaf)
        {
            foreach (var branch in _branches.OfType<IBranch>())
            {
                try
                {
                    // Check if branch supports this operation
                    bool shouldPropagate = leaf.Type switch
                    {
                        LeafType.Stash => branch.Capabilities.Supports(BranchCapabilities.Stash),
                        LeafType.Toss => branch.Capabilities.Supports(BranchCapabilities.Toss),
                        LeafType.Squabble => branch.Capabilities.Supports(BranchCapabilities.Squabble),
                        LeafType.Update => branch.Capabilities.Supports(BranchCapabilities.Stash),
                        _ => false
                    };

                    if (!shouldPropagate)
                        continue;

                    // Don't send back to the origin (if this is a sync branch)
                    var remoteTreeId = branch.GetRemoteTreeId();
                    if (remoteTreeId != null && remoteTreeId == leaf.OriginTreeId)
                    {
                        AcornLog.Info($"> 🍃 Skipping branch {branch.BranchId} (would send to origin)");
                        continue;
                    }

                    // Send to branch
                    switch (leaf.Type)
                    {
                        case LeafType.Stash:
                        case LeafType.Update:
                            branch.OnStash(leaf);
                            break;
                        case LeafType.Toss:
                            branch.OnToss(leaf);
                            break;
                        case LeafType.Squabble:
                            branch.OnSquabble(leaf);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    AcornLog.Error($"> ⚠️ Failed to propagate leaf to branch {branch.BranchId}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Create a new leaf for a local change
        /// </summary>
        private Leaf<T> CreateLeaf(LeafType type, string key, Nut<T>? data = null)
        {
            var sequence = System.Threading.Interlocked.Increment(ref _leafSequenceCounter);
            var timestamp = DateTime.UtcNow;

            return new Leaf<T>
            {
                LeafId = $"{TreeId}-{timestamp.Ticks}-{sequence}",
                OriginTreeId = TreeId,
                Type = type,
                Key = key,
                Data = data,
                Timestamp = timestamp,
                HopCount = 0
            };
        }
    }
}
