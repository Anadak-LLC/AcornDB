using AcornDB.Sync;

namespace AcornDB.Models
{
    public partial class Grove
    {
        internal readonly Dictionary<string, object> _trees = new();
        private readonly List<object> _tangles = new();

        public int TreeCount => _trees.Count;

        public void Plant<T>(Tree<T> tree)
        {
            var key = typeof(T).FullName!;
            _trees[key] = tree;
            Console.WriteLine($"> 🌳 Grove planted Tree<{typeof(T).Name}>");
        }

        public Tree<T>? GetTree<T>()
        {
            var key = typeof(T).FullName!;
            return _trees.TryGetValue(key, out var obj) ? obj as Tree<T> : null;
        }

        public IEnumerable<object> GetAllTrees()
        {
            return _trees.Values;
        }

        public Tangle<T> Entangle<T>(Branch branch, string id)
        {
            var tree = GetTree<T>();
            if (tree == null)
                throw new InvalidOperationException($"🌰 Tree<{typeof(T).Name}> not found in Grove.");

            var tangle = new Tangle<T>(tree, branch, id);
            _tangles.Add(tangle);
            Console.WriteLine($"> 🪢 Grove entangled Tree<{typeof(T).Name}> with branch '{branch.RemoteUrl}'");
            return tangle;
        }

        public void Oversee<T>(Branch branch, string id)
        {
            Entangle<T>(branch, id);
            Console.WriteLine($">Grove is overseeing Tangle '{id}' for Tree<{typeof(T).Name}>");
        }

        public void ShakeAll()
        {
            Console.WriteLine("> 🍃 Grove is shaking all tangles...");
            foreach (var tangle in _tangles)
            {
                if (tangle is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        public void EntangleAll(string remoteUrl)
        {
            Console.WriteLine($"> 🌐 Grove entangling all trees with {remoteUrl}");

            var branch = new Branch(remoteUrl);

            foreach (var kvp in _trees)
            {
                var tree = kvp.Value;
                var treeType = tree.GetType();
                var genericArg = treeType.GenericTypeArguments.FirstOrDefault();

                if (genericArg == null) continue;

                // Use reflection to call Entangle<T> with the correct type
                var entangleMethod = typeof(Grove).GetMethod(nameof(Entangle));
                var genericEntangle = entangleMethod?.MakeGenericMethod(genericArg);

                try
                {
                    var tangleId = $"{genericArg.Name}_Tangle";
                    genericEntangle?.Invoke(this, new object[] { branch, tangleId });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"> ⚠️ Failed to entangle Tree<{genericArg.Name}>: {ex.Message}");
                }
            }

            Console.WriteLine($"> ✅ Grove entangled {_trees.Count} trees with {remoteUrl}");
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