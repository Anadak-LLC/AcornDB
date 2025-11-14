using System;
using System.Collections.Generic;
using System.Linq;

namespace AcornDB.Storage
{
    /// <summary>
    /// The Nursery - where trunk types grow and are discovered!
    /// Central registry for discovering and creating trunk implementations.
    /// Supports both built-in and custom trunk types.
    /// </summary>
    public static class Nursery
    {
        private static readonly Dictionary<string, ITrunkFactory> _factories = new();
        private static readonly object _lock = new();
        private static bool _initialized = false;

        /// <summary>
        /// Plant a new trunk factory in the nursery by type ID
        /// </summary>
        public static void Plant(string typeId, ITrunkFactory factory)
        {
            lock (_lock)
            {
                if (_factories.ContainsKey(typeId))
                {
                    throw new InvalidOperationException($"Trunk type '{typeId}' is already planted in the nursery");
                }
                _factories[typeId] = factory;
            }
        }

        /// <summary>
        /// Plant a new trunk factory in the nursery (uses metadata.TypeId)
        /// </summary>
        public static void Plant(ITrunkFactory factory)
        {
            var metadata = factory.GetMetadata();
            Plant(metadata.TypeId, factory);
        }

        /// <summary>
        /// Remove a trunk factory from the nursery
        /// </summary>
        public static bool Remove(string typeId)
        {
            lock (_lock)
            {
                return _factories.Remove(typeId);
            }
        }

        /// <summary>
        /// Check if a trunk type is available in the nursery
        /// </summary>
        public static bool HasTrunk(string typeId)
        {
            lock (_lock)
            {
                EnsureInitialized();
                return _factories.ContainsKey(typeId);
            }
        }

        /// <summary>
        /// Get factory for a trunk type
        /// </summary>
        public static ITrunkFactory? GetFactory(string typeId)
        {
            lock (_lock)
            {
                EnsureInitialized();
                return _factories.TryGetValue(typeId, out var factory) ? factory : null;
            }
        }

        /// <summary>
        /// Grow a trunk instance by type ID
        /// </summary>
        public static ITrunk<T> Grow<T>(string typeId, Dictionary<string, object> configuration)
        {
            lock (_lock)
            {
                EnsureInitialized();

                if (!_factories.TryGetValue(typeId, out var factory))
                {
                    throw new InvalidOperationException($"Trunk type '{typeId}' not found in nursery. Available types: {string.Join(", ", GetAvailableTypes())}");
                }

                if (!factory.ValidateConfiguration(configuration))
                {
                    var metadata = factory.GetMetadata();
                    throw new ArgumentException($"Invalid configuration for trunk type '{typeId}'. Required keys: {string.Join(", ", metadata.RequiredConfigKeys)}");
                }

                var trunk = factory.Create(typeof(T), configuration);
                return (ITrunk<T>)trunk;
            }
        }

        /// <summary>
        /// Get all trunk types available in the nursery
        /// </summary>
        public static IEnumerable<string> GetAvailableTypes()
        {
            lock (_lock)
            {
                EnsureInitialized();
                return _factories.Keys.ToList();
            }
        }

        /// <summary>
        /// Get metadata for all trunks in the nursery
        /// </summary>
        public static IEnumerable<TrunkMetadata> GetAllMetadata()
        {
            lock (_lock)
            {
                EnsureInitialized();
                return _factories.Values.Select(f => f.GetMetadata()).ToList();
            }
        }

        /// <summary>
        /// Get metadata for a specific trunk type
        /// </summary>
        public static TrunkMetadata? GetMetadata(string typeId)
        {
            lock (_lock)
            {
                EnsureInitialized();
                return _factories.TryGetValue(typeId, out var factory) ? factory.GetMetadata() : null;
            }
        }

        /// <summary>
        /// Find trunks by category (e.g., "Local", "Cloud", "Database")
        /// </summary>
        public static IEnumerable<TrunkMetadata> GetByCategory(string category)
        {
            lock (_lock)
            {
                EnsureInitialized();
                return _factories.Values
                    .Select(f => f.GetMetadata())
                    .Where(m => m.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        /// <summary>
        /// Find trunks by capability
        /// </summary>
        public static IEnumerable<TrunkMetadata> GetByCapability(Func<ITrunkCapabilities, bool> predicate)
        {
            lock (_lock)
            {
                EnsureInitialized();
                return _factories.Values
                    .Select(f => f.GetMetadata())
                    .Where(m => predicate(m.Capabilities))
                    .ToList();
            }
        }

        /// <summary>
        /// Clear the entire nursery (useful for testing)
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _factories.Clear();
                _initialized = false;
            }
        }

        /// <summary>
        /// Plant all built-in trunk factories in the nursery
        /// </summary>
        private static void EnsureInitialized()
        {
            if (_initialized) return;

            // Plant built-in trunks
            Plant(new FileTrunkFactory());
            Plant(new MemoryTrunkFactory());
            Plant(new DocumentStoreTrunkFactory());
            Plant(new GitHubTrunkFactory());
            // NOTE: AzureTrunk moved to AcornDB.Persistence.Cloud package

            _initialized = true;
        }

        /// <summary>
        /// Get a formatted catalog of all trunks in the nursery
        /// </summary>
        public static string GetCatalog()
        {
            lock (_lock)
            {
                EnsureInitialized();
                var catalog = "🌳 Nursery Catalog - Available Trunk Types:\n";

                foreach (var metadata in GetAllMetadata().OrderBy(m => m.Category).ThenBy(m => m.TypeId))
                {
                    catalog += $"\n[{metadata.Category}] {metadata.TypeId} - {metadata.DisplayName}\n";
                    catalog += $"  {metadata.Description}\n";
                    catalog += $"  Durable: {metadata.Capabilities.IsDurable}, ";
                    catalog += $"History: {metadata.Capabilities.SupportsHistory}, ";
                    catalog += $"Sync: {metadata.Capabilities.SupportsSync}, ";
                    catalog += $"Async: {metadata.Capabilities.SupportsAsync}\n";
                }

                return catalog;
            }
        }
    }
}
