using System;
using System.Threading.Tasks;
using AcornDB.Logging;

namespace AcornDB.Sync
{
    /// <summary>
    /// InProcessBranch: Syncs between two trees in the same process without HTTP
    /// </summary>
    public class InProcessBranch<T> : Branch where T : class
    {
        private readonly Tree<T> _targetTree;

        public InProcessBranch(Tree<T> targetTree) : base("in-process")
        {
            _targetTree = targetTree ?? throw new ArgumentNullException(nameof(targetTree));
        }

        /// <summary>
        /// Get the target tree ID for loop prevention
        /// </summary>
        public new string? GetRemoteTreeId()
        {
            return _targetTree.TreeId;
        }

        public override void TryPush<TItem>(string id, Nut<TItem> nut)
        {
            if (typeof(TItem) != typeof(T))
            {
                AcornLog.Error($"> ⚠️ InProcessBranch: Type mismatch - expected {typeof(T).Name}, got {typeof(TItem).Name}");
                return;
            }

            try
            {
                // Cast nut to correct type and squabble on target tree
                var typedNut = nut as Nut<T>;
                if (typedNut != null)
                {
                    _targetTree.Squabble(id, typedNut);
                }
            }
            catch (Exception ex)
            {
                AcornLog.Error($"> ⚠️ InProcessBranch push failed: {ex.Message}");
            }
        }

        public override void TryDelete<TItem>(string id)
        {
            if (typeof(TItem) != typeof(T))
            {
                AcornLog.Error($"> ⚠️ InProcessBranch: Type mismatch during delete - expected {typeof(T).Name}, got {typeof(TItem).Name}");
                return;
            }

            try
            {
                // Delete directly on target tree without propagating (propagate=false prevents loops)
                _targetTree.Toss(id, propagate: false);
                AcornLog.Info($"> 🔄 InProcessBranch: Deleted '{id}' from target tree");
            }
            catch (Exception ex)
            {
                AcornLog.Error($"> ⚠️ InProcessBranch delete failed: {ex.Message}");
            }
        }

        // ===== IBranch Interface Overrides for In-Process Sync =====

        /// <summary>
        /// Handle stash leaf - use HandleLeaf for proper loop prevention
        /// </summary>
        public new void OnStash<TLeaf>(Leaf<TLeaf> leaf)
        {
            if (typeof(TLeaf) != typeof(T))
            {
                AcornLog.Error($"> ⚠️ InProcessBranch: Type mismatch for leaf - expected {typeof(T).Name}, got {typeof(TLeaf).Name}");
                return;
            }

            try
            {
                // Cast leaf to correct type and use HandleLeaf
                var typedLeaf = leaf as Leaf<T>;
                if (typedLeaf != null)
                {
                    _targetTree.HandleLeaf(typedLeaf);
                }
            }
            catch (Exception ex)
            {
                AcornLog.Error($"> ⚠️ InProcessBranch OnStash failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle toss leaf - use HandleLeaf for proper loop prevention
        /// </summary>
        public new void OnToss<TLeaf>(Leaf<TLeaf> leaf)
        {
            if (typeof(TLeaf) != typeof(T))
            {
                AcornLog.Error($"> ⚠️ InProcessBranch: Type mismatch for leaf - expected {typeof(T).Name}, got {typeof(TLeaf).Name}");
                return;
            }

            try
            {
                var typedLeaf = leaf as Leaf<T>;
                if (typedLeaf != null)
                {
                    _targetTree.HandleLeaf(typedLeaf);
                }
            }
            catch (Exception ex)
            {
                AcornLog.Error($"> ⚠️ InProcessBranch OnToss failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle squabble leaf - use HandleLeaf for proper loop prevention
        /// </summary>
        public new void OnSquabble<TLeaf>(Leaf<TLeaf> leaf)
        {
            if (typeof(TLeaf) != typeof(T))
            {
                AcornLog.Error($"> ⚠️ InProcessBranch: Type mismatch for leaf - expected {typeof(T).Name}, got {typeof(TLeaf).Name}");
                return;
            }

            try
            {
                var typedLeaf = leaf as Leaf<T>;
                if (typedLeaf != null)
                {
                    _targetTree.HandleLeaf(typedLeaf);
                }
            }
            catch (Exception ex)
            {
                AcornLog.Error($"> ⚠️ InProcessBranch OnSquabble failed: {ex.Message}");
            }
        }

        public override async Task ShakeAsync<TItem>(Tree<TItem> sourceTree)
        {
            if (typeof(TItem) != typeof(T))
            {
                AcornLog.Error($"> ⚠️ InProcessBranch: Type mismatch during shake");
                return;
            }

            try
            {
                var changes = _targetTree.ExportChanges();
                foreach (var nut in changes)
                {
                    var typedSourceTree = sourceTree as Tree<T>;
                    typedSourceTree?.Squabble(nut.Id, nut);
                }
            }
            catch (Exception ex)
            {
                AcornLog.Error($"> ⚠️ InProcessBranch shake failed: {ex.Message}");
            }

            await Task.CompletedTask;
        }
    }
}
