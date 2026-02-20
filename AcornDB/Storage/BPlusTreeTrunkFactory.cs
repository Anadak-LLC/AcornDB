using System;
using System.Collections.Generic;
using AcornDB.Storage.BPlusTree;

namespace AcornDB.Storage
{
    /// <summary>
    /// Factory for creating BPlusTreeTrunk instances via the Nursery registry.
    /// </summary>
    public class BPlusTreeTrunkFactory : ITrunkFactory
    {
        public ITrunk<object> Create(Type itemType, Dictionary<string, object> configuration)
        {
            var path = configuration.TryGetValue("path", out var pathObj) ? pathObj?.ToString() : null;

            var options = new BPlusTreeOptions
            {
                PageSize = GetInt(configuration, "pageSize", BPlusTreeOptions.Default.PageSize),
                MaxCachePages = GetInt(configuration, "maxCachePages", BPlusTreeOptions.Default.MaxCachePages),
                ValidateChecksumsOnRead = GetBool(configuration, "validateChecksums", BPlusTreeOptions.Default.ValidateChecksumsOnRead),
                FsyncOnCommit = GetBool(configuration, "fsyncOnCommit", BPlusTreeOptions.Default.FsyncOnCommit),
                CheckpointThreshold = GetInt(configuration, "checkpointThreshold", BPlusTreeOptions.Default.CheckpointThreshold)
            };

            var trunkType = typeof(BPlusTreeTrunk<>).MakeGenericType(itemType);
            var trunk = Activator.CreateInstance(trunkType, path, null /* serializer */, options);
            return (ITrunk<object>)trunk!;
        }

        public TrunkMetadata GetMetadata()
        {
            return new TrunkMetadata
            {
                TypeId = "bplustree",
                DisplayName = "B+Tree Trunk",
                Description = "Page-based B+Tree with WAL crash safety, page cache, and ordered access. High-performance durable storage.",
                Capabilities = new TrunkCapabilities
                {
                    SupportsHistory = false,
                    SupportsSync = true,
                    IsDurable = true,
                    SupportsAsync = false,
                    TrunkType = "BPlusTreeTrunk"
                },
                RequiredConfigKeys = new List<string>(),
                OptionalConfigKeys = new Dictionary<string, object>
                {
                    { "path", "./data/{TypeName}" },
                    { "pageSize", 8192 },
                    { "maxCachePages", 256 },
                    { "validateChecksums", true },
                    { "fsyncOnCommit", true },
                    { "checkpointThreshold", 1000 }
                },
                IsBuiltIn = true,
                Category = "Local"
            };
        }

        public bool ValidateConfiguration(Dictionary<string, object> configuration)
        {
            if (configuration.TryGetValue("pageSize", out var pageSizeObj))
            {
                var pageSize = ConvertToInt(pageSizeObj);
                if (pageSize == null || pageSize < 4096 || pageSize > 65536 || (pageSize & (pageSize - 1)) != 0)
                    return false;
            }

            if (configuration.TryGetValue("maxCachePages", out var cacheObj))
            {
                var maxCache = ConvertToInt(cacheObj);
                if (maxCache == null || maxCache <= 0)
                    return false;
            }

            if (configuration.TryGetValue("checkpointThreshold", out var threshObj))
            {
                var threshold = ConvertToInt(threshObj);
                if (threshold == null || threshold <= 0)
                    return false;
            }

            return true;
        }

        private static int GetInt(Dictionary<string, object> config, string key, int defaultValue)
        {
            if (config.TryGetValue(key, out var val))
                return ConvertToInt(val) ?? defaultValue;
            return defaultValue;
        }

        private static bool GetBool(Dictionary<string, object> config, string key, bool defaultValue)
        {
            if (config.TryGetValue(key, out var val))
            {
                if (val is bool b) return b;
                if (val is string s && bool.TryParse(s, out var parsed)) return parsed;
            }
            return defaultValue;
        }

        private static int? ConvertToInt(object val)
        {
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (val is string s && int.TryParse(s, out var parsed)) return parsed;
            if (val is IConvertible c)
            {
                try { return c.ToInt32(null); }
                catch { return null; }
            }
            return null;
        }
    }
}
