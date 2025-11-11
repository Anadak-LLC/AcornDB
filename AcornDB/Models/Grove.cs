using AcornDB.Metrics;
using AcornDB.Sync;
using AcornDB.Logging;

namespace AcornDB.Models
{
    public partial class Grove
    {
        internal readonly Dictionary<string, object> _trees = new();
        private readonly List<object> _tangles = new();
        private readonly HashSet<string> _meshPairs = new(); // Track entangled pairs
        private int _nextTreeId = 0;

        public int TreeCount => _trees.Count;

        /// <summary>
        /// Plant a tree in the grove with an auto-generated unique ID
        /// </summary>
        public string Plant<T>(Tree<T> tree) where T : class
        {
            var id = $"{typeof(T).FullName}#{_nextTreeId++}";
            _trees[id] = tree;
            AcornLog.Info($"> 🌳 Grove planted Tree<{typeof(T).Name}> with ID '{id}'");
            return id;
        }

        /// <summary>
        /// Plant a tree in the grove with a specific ID
        /// </summary>
        public void Plant<T>(Tree<T> tree, string id) where T : class
        {
            _trees[id] = tree;
            AcornLog.Info($"> 🌳 Grove planted Tree<{typeof(T).Name}> with ID '{id}'");
        }

        /// <summary>
        /// Get the first tree of a specific type (for backward compatibility)
        /// </summary>
        public Tree<T>? GetTree<T>() where T : class
        {
            var typePrefix = typeof(T).FullName!;
            var match = _trees.FirstOrDefault(kvp => kvp.Key.StartsWith(typePrefix + "#") || kvp.Key == typePrefix);
            return match.Value as Tree<T>;
        }

        /// <summary>
        /// Get a tree by its specific ID
        /// </summary>
        public Tree<T>? GetTree<T>(string id) where T : class
        {
            return _trees.TryGetValue(id, out var obj) ? obj as Tree<T> : null;
        }

        /// <summary>
        /// Get all trees of a specific type
        /// </summary>
        public IEnumerable<Tree<T>> GetTrees<T>() where T : class
        {
            var typePrefix = typeof(T).FullName!;
            return _trees
                .Where(kvp => kvp.Key.StartsWith(typePrefix + "#") || kvp.Key == typePrefix)
                .Select(kvp => kvp.Value as Tree<T>)
                .Where(t => t != null)!;
        }

        public IEnumerable<object> GetAllTrees()
        {
            return _trees.Values;
        }

        public Tangle<T> Entangle<T>(Branch branch, string tangleId) where T : class
        {
            var tree = GetTree<T>();
            if (tree == null)
                throw new InvalidOperationException($"🌰 Tree<{typeof(T).Name}> not found in Grove.");

            var tangle = new Tangle<T>(tree, branch, tangleId);
            _tangles.Add(tangle);
            AcornLog.Info($"> 🪢 Grove entangled Tree<{typeof(T).Name}> with branch '{branch.RemoteUrl}'");
            return tangle;
        }

        public Tangle<T> Entangle<T>(Branch branch, string treeId, string tangleId) where T : class
        {
            var tree = GetTree<T>(treeId);
            if (tree == null)
                throw new InvalidOperationException($"🌰 Tree<{typeof(T).Name}> with ID '{treeId}' not found in Grove.");

            var tangle = new Tangle<T>(tree, branch, tangleId);
            _tangles.Add(tangle);
            AcornLog.Info($"> 🪢 Grove entangled Tree<{typeof(T).Name}>[{treeId}] with branch '{branch.RemoteUrl}'");
            return tangle;
        }

        public void Oversee<T>(Branch branch, string id) where T : class
        {
            Entangle<T>(branch, id);
            AcornLog.Info($">Grove is overseeing Tangle '{id}' for Tree<{typeof(T).Name}>");
        }

        /// <summary>
        /// Detangle a specific tangle from the grove
        /// </summary>
        public void Detangle<T>(Tangle<T> tangle) where T : class
        {
            if (_tangles.Remove(tangle))
            {
                AcornLog.Info($"> 🔓 Grove detangled tangle");
                tangle?.Dispose();
            }
        }

        /// <summary>
        /// Detangle all tangles in the grove
        /// </summary>
        public void DetangleAll()
        {
            AcornLog.Info("> 🔓 Grove detangling all tangles...");
            foreach (var tangle in _tangles.ToList())
            {
                if (tangle is IDisposable disposable)
                    disposable.Dispose();
            }
            _tangles.Clear();
            AcornLog.Info("> 🔓 All grove entanglements cleared!");
        }

        public void ShakeAll()
        {
            AcornLog.Info("> 🍃 Grove is shaking all tangles...");
            foreach (var tangle in _tangles)
            {
                if (tangle is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        /// <summary>
        /// Entangle all trees in the grove with a remote URL
        /// Creates a star topology: all trees → remote
        /// </summary>
        public void EntangleAll(string remoteUrl)
        {
            AcornLog.Info($"> 🌐 Grove entangling all trees with {remoteUrl}");

            var branch = new Branch(remoteUrl);

            foreach (var kvp in _trees)
            {
                var tree = kvp.Value;
                var treeType = tree.GetType();
                var genericArg = treeType.GenericTypeArguments.FirstOrDefault();

                if (genericArg == null) continue;

                // Use reflection to call Entangle<T>(Branch, string) with the correct type
                var entangleMethod = typeof(Grove).GetMethod(nameof(Entangle), new[] { typeof(Branch), typeof(string) });
                var genericEntangle = entangleMethod?.MakeGenericMethod(genericArg);

                try
                {
                    var tangleId = $"{genericArg.Name}_Tangle";
                    genericEntangle?.Invoke(this, new object[] { branch, tangleId });
                }
                catch (Exception ex)
                {
                    AcornLog.Error($"> ⚠️ Failed to entangle Tree<{genericArg.Name}>: {ex.Message}");
                }
            }

            AcornLog.Info($"> ✅ Grove entangled {_trees.Count} trees with {remoteUrl}");
        }

        /// <summary>
        /// Create a full mesh of in-process entanglements between all trees in the grove
        /// Creates a full mesh topology: every tree ↔ every other tree
        /// </summary>
        /// <param name="bidirectional">If true, creates bidirectional entanglements. If false, creates unidirectional.</param>
        /// <returns>Number of tangles created</returns>
        public int EntangleAll(bool bidirectional = true)
        {
            AcornLog.Info($"> 🕸️ Grove creating {(bidirectional ? "bidirectional" : "unidirectional")} mesh entanglement...");

            var trees = _trees.ToList();
            var tangleCount = 0;

            for (int i = 0; i < trees.Count; i++)
            {
                for (int j = i + 1; j < trees.Count; j++)
                {
                    var tree1 = trees[i];
                    var tree2 = trees[j];

                    var type1 = tree1.Value.GetType();
                    var type2 = tree2.Value.GetType();
                    var genericArg1 = type1.GenericTypeArguments.FirstOrDefault();
                    var genericArg2 = type2.GenericTypeArguments.FirstOrDefault();

                    if (genericArg1 == null || genericArg2 == null) continue;

                    // Only entangle trees of the same type
                    if (genericArg1 == genericArg2)
                    {
                        var pairKey = $"{tree1.Key}↔{tree2.Key}";

                        // Check if this pair is already entangled
                        if (_meshPairs.Contains(pairKey))
                            continue;

                        _meshPairs.Add(pairKey);

                        try
                        {
                            // Use dynamic to call Tree<T>.Entangle(Tree<T>) method
                            dynamic dynamicTree1 = tree1.Value;
                            dynamic dynamicTree2 = tree2.Value;

                            var tangle = dynamicTree1.Entangle(dynamicTree2);
                            if (tangle != null)
                            {
                                _tangles.Add(tangle);
                                tangleCount++;
                                AcornLog.Info($">   🪢 {genericArg1.Name} [{tree1.Key}] ↔ [{tree2.Key}]");
                            }
                        }
                        catch (Exception ex)
                        {
                            AcornLog.Error($">   ⚠️ Failed to entangle {tree1.Key} ↔ {tree2.Key}: {ex.Message}");
                        }
                    }
                }
            }

            AcornLog.Info($"> ✅ Grove mesh complete: {tangleCount} tangles created for {trees.Count} trees");
            return tangleCount;
        }

        public bool TryStash(string typeName, string key, string json)
        {
            if (_trees.TryGetValue(typeName, out var obj))
            {
                var stashMethod = obj.GetType().GetMethod("Stash");
                var type = obj.GetType().GenericTypeArguments[0];
                var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, type);
                stashMethod?.Invoke(obj, new[] { key, deserialized });
                return true;
            }
            return false;
        }

        public bool TryToss(string typeName, string key)
        {
            if (_trees.TryGetValue(typeName, out var obj))
            {
                var tossMethod = obj.GetType().GetMethod("Toss");
                tossMethod?.Invoke(obj, new[] { key });
                return true;
            }
            return false;
        }

        public string? TryCrack(string typeName, string key)
        {
            if (_trees.TryGetValue(typeName, out var obj))
            {
                var crackMethod = obj.GetType().GetMethod("Crack");
                var result = crackMethod?.Invoke(obj, new[] { key });
                return System.Text.Json.JsonSerializer.Serialize(result);
            }
            return null;
        }

        public GroveStats GetNutStats()
        {
            var stats = new GroveStats();
            var trees = _trees.Values;

            stats.TotalTrees = trees.Count;
            stats.TreeTypes = trees.Select(t => t.GetType().GenericTypeArguments.First().Name).ToList();

            foreach (dynamic tree in trees)
            {
                var nutStats = ((dynamic)tree).GetNutStats();
                stats.TotalStashed += nutStats.TotalStashed;
                stats.TotalTossed += nutStats.TotalTossed;
                stats.TotalSquabbles += nutStats.SquabblesResolved;
                stats.TotalSmushes += nutStats.SmushesPerformed;
                stats.ActiveTangles += nutStats.ActiveTangles;
            }

            return stats;
        }

        public List<TreeInfo> GetTreeInfo()
        {
            var result = new List<TreeInfo>();
            foreach (var kvp in _trees)
            {
                var type = kvp.Value.GetType();
                var genericArg = type.GenericTypeArguments.FirstOrDefault();

                dynamic tree = kvp.Value;
                result.Add(new TreeInfo
                {
                    Id = kvp.Key,
                    Type = genericArg?.Name ?? "Unknown",
                    NutCount = tree.NutCount,
                    IsRemote = false // Local trees in this Grove
                });
            }
            return result;
        }

        public object? GetTreeByTypeName(string typeName)
        {
            return _trees.TryGetValue(typeName, out var tree) ? tree : null;
        }

        public IEnumerable<object> ExportChanges(string typeName)
        {
            var tree = GetTreeByTypeName(typeName);
            if (tree == null) return Enumerable.Empty<object>();

            var exportMethod = tree.GetType().GetMethod("ExportChanges");
            var result = exportMethod?.Invoke(tree, null);
            return result as IEnumerable<object> ?? Enumerable.Empty<object>();
        }

        public void ImportChanges(string typeName, IEnumerable<object> changes)
        {
            var tree = GetTreeByTypeName(typeName);
            if (tree == null) return;

            var importMethod = tree.GetType().GetMethod("ImportChanges");
            importMethod?.Invoke(tree, new[] { changes });
        }
    }

    public class TreeInfo
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public int NutCount { get; set; }
        public bool IsRemote { get; set; }
    }
}