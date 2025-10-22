using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Persistence.DataLake
{
    /// <summary>
    /// Tiered storage trunk implementing hot/cold data architecture.
    /// Hot tier: Fast OLTP storage (BTree, Memory, SQL)
    /// Cold tier: Columnar analytics storage (Parquet, archived data)
    ///
    /// Benefits:
    /// - Fast queries on recent data (hot tier)
    /// - Cost-effective long-term storage (cold tier)
    /// - Automatic data aging/archival
    /// - Query federation across tiers
    /// </summary>
    public class TieredTrunk<T> : ITrunk<T>, IDisposable
    {
        private readonly ITrunk<T> _hotTrunk;
        private readonly ITrunk<T> _coldTrunk;
        private readonly TieringOptions<T> _options;
        private readonly Timer? _tieringTimer;
        private bool _disposed;

        /// <summary>
        /// Create tiered trunk with hot and cold storage
        /// </summary>
        /// <param name="hotTrunk">Hot tier (fast OLTP storage)</param>
        /// <param name="coldTrunk">Cold tier (columnar/archived storage)</param>
        /// <param name="options">Tiering options (aging strategy)</param>
        public TieredTrunk(
            ITrunk<T> hotTrunk,
            ITrunk<T> coldTrunk,
            TieringOptions<T>? options = null)
        {
            _hotTrunk = hotTrunk ?? throw new ArgumentNullException(nameof(hotTrunk));
            _coldTrunk = coldTrunk ?? throw new ArgumentNullException(nameof(coldTrunk));
            _options = options ?? new TieringOptions<T>();

            // Auto-tiering background task
            if (_options.AutoTiering)
            {
                _tieringTimer = new Timer(
                    _ => TierData(),
                    null,
                    _options.TieringInterval,
                    _options.TieringInterval
                );
            }

            Console.WriteLine($"🔥❄️ TieredTrunk initialized:");
            Console.WriteLine($"   Hot Tier: {GetTrunkType(_hotTrunk)}");
            Console.WriteLine($"   Cold Tier: {GetTrunkType(_coldTrunk)}");
            Console.WriteLine($"   Auto-Tiering: {(_options.AutoTiering ? "Enabled" : "Disabled")}");
            if (_options.ArchiveAfter.HasValue)
            {
                Console.WriteLine($"   Archive After: {_options.ArchiveAfter.Value.TotalDays:F0} days");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Save(string id, Nut<T> nut)
        {
            // Always write to hot tier for fast writes
            _hotTrunk.Save(id, nut);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Load(string id)
        {
            // Try hot tier first (cache pattern)
            var nut = _hotTrunk.Load(id);
            if (nut != null)
                return nut;

            // Fallback to cold tier
            nut = _coldTrunk.Load(id);

            // Optional: Promote to hot tier on access
            if (nut != null && _options.PromoteOnRead)
            {
                _hotTrunk.Save(id, nut);
            }

            return nut;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(string id)
        {
            // Delete from both tiers
            _hotTrunk.Delete(id);
            _coldTrunk.Delete(id);
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            // Query federation: combine both tiers
            var hotNuts = _hotTrunk.LoadAll().ToDictionary(n => n.Id);
            var coldNuts = _coldTrunk.LoadAll().ToDictionary(n => n.Id);

            // Merge (hot takes precedence for duplicates)
            foreach (var kvp in coldNuts)
            {
                if (!hotNuts.ContainsKey(kvp.Key))
                {
                    hotNuts[kvp.Key] = kvp.Value;
                }
            }

            return hotNuts.Values;
        }

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            // Try hot tier first (likely has most recent history)
            var hotCaps = _hotTrunk.GetCapabilities();
            if (hotCaps.SupportsHistory)
            {
                try
                {
                    return _hotTrunk.GetHistory(id);
                }
                catch { /* Fallback to cold */ }
            }

            // Fallback to cold tier
            var coldCaps = _coldTrunk.GetCapabilities();
            if (coldCaps.SupportsHistory)
            {
                return _coldTrunk.GetHistory(id);
            }

            throw new NotSupportedException("Neither tier supports history.");
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return LoadAll();
        }

        public void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            // Import to hot tier for fast writes
            _hotTrunk.ImportChanges(incoming);
        }

        public ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = false,
            TrunkType = "TieredTrunk"
        };

        /// <summary>
        /// Manually trigger data tiering (move old data from hot to cold)
        /// </summary>
        public void TierData()
        {
            var nuts = _hotTrunk.LoadAll().ToList();
            var oldNuts = new List<Nut<T>>();

            foreach (var nut in nuts)
            {
                // Check if nut should be archived
                if (ShouldArchive(nut))
                {
                    oldNuts.Add(nut);
                }
            }

            if (!oldNuts.Any())
            {
                return;
            }

            Console.WriteLine($"   🔥→❄️ Tiering {oldNuts.Count} nuts from hot to cold storage...");

            // Move to cold tier
            _coldTrunk.ImportChanges(oldNuts);

            // Remove from hot tier
            foreach (var nut in oldNuts)
            {
                _hotTrunk.Delete(nut.Id);
            }

            Console.WriteLine($"   ✅ Tiered {oldNuts.Count} nuts to cold storage");
        }

        /// <summary>
        /// Check if nut should be archived based on tiering strategy
        /// </summary>
        private bool ShouldArchive(Nut<T> nut)
        {
            // Age-based tiering
            if (_options.ArchiveAfter.HasValue)
            {
                var age = DateTime.UtcNow - nut.Timestamp;
                if (age > _options.ArchiveAfter.Value)
                {
                    return true;
                }
            }

            // Custom predicate
            if (_options.CustomArchivePredicate != null)
            {
                return _options.CustomArchivePredicate(nut);
            }

            return false;
        }

        private string GetTrunkType(ITrunk<T> trunk)
        {
            var caps = trunk.GetCapabilities();
            return caps.TrunkType;
        }

        // ITrunkCapabilities implementation
        public bool SupportsHistory
        {
            get
            {
                var hotCaps = _hotTrunk.GetCapabilities();
                var coldCaps = _coldTrunk.GetCapabilities();
                return hotCaps.SupportsHistory || coldCaps.SupportsHistory;
            }
        }

        public bool SupportsSync => true;

        public bool IsDurable
        {
            get
            {
                var hotCaps = _hotTrunk.GetCapabilities();
                var coldCaps = _coldTrunk.GetCapabilities();
                return hotCaps.IsDurable || coldCaps.IsDurable;
            }
        }

        public bool SupportsAsync
        {
            get
            {
                // TieredTrunk provides async wrappers even if underlying trunks don't
                return true;
            }
        }

        public string TrunkType
        {
            get
            {
                var hotCaps = _hotTrunk.GetCapabilities();
                var coldCaps = _coldTrunk.GetCapabilities();
                return $"TieredTrunk({hotCaps.TrunkType}+{coldCaps.TrunkType})";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _tieringTimer?.Dispose();

            if (_hotTrunk is IDisposable hotDisposable)
                hotDisposable.Dispose();

            if (_coldTrunk is IDisposable coldDisposable)
                coldDisposable.Dispose();
        }

        // IRoot support - stub implementation (to be fully implemented later)
        public IReadOnlyList<IRoot> Roots => Array.Empty<IRoot>();
        public void AddRoot(IRoot root) { /* TODO: Implement root support */ }
        public bool RemoveRoot(string name) => false;
    }

    /// <summary>
    /// Tiering configuration options
    /// </summary>
    public class TieringOptions<T>
    {
        /// <summary>
        /// Auto-tiering enabled (background task moves old data to cold tier)
        /// Default: false (manual tiering only)
        /// </summary>
        public bool AutoTiering { get; set; } = false;

        /// <summary>
        /// How often to run tiering process
        /// Default: 1 hour
        /// </summary>
        public TimeSpan TieringInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Archive data older than this age
        /// Default: null (no age-based archiving)
        /// </summary>
        public TimeSpan? ArchiveAfter { get; set; } = null;

        /// <summary>
        /// Custom predicate for determining if nut should be archived
        /// Default: null
        /// </summary>
        public Func<Nut<T>, bool>? CustomArchivePredicate { get; set; } = null;

        /// <summary>
        /// Promote nuts from cold to hot tier on read
        /// Default: false (read-through only)
        /// </summary>
        public bool PromoteOnRead { get; set; } = false;

        /// <summary>
        /// Default options (manual tiering only)
        /// </summary>
        public static TieringOptions<T> Default => new TieringOptions<T>();

        /// <summary>
        /// Auto-tiering with 30-day archive threshold
        /// </summary>
        public static TieringOptions<T> AutoArchive30Days => new TieringOptions<T>
        {
            AutoTiering = true,
            ArchiveAfter = TimeSpan.FromDays(30),
            TieringInterval = TimeSpan.FromHours(6)
        };

        /// <summary>
        /// Auto-tiering with 7-day archive threshold (fast-moving data)
        /// </summary>
        public static TieringOptions<T> AutoArchive7Days => new TieringOptions<T>
        {
            AutoTiering = true,
            ArchiveAfter = TimeSpan.FromDays(7),
            TieringInterval = TimeSpan.FromHours(1)
        };
    }
}
