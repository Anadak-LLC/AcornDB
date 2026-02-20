using System.Diagnostics;
using System.Reflection;
using AcornDB.Storage.Serialization;
using AcornDB.Query;
using AcornDB.Policy;
using AcornDB.Storage;
using AcornDB.Cache;

namespace AcornDB.Models;

/// <summary>
/// Quick reference for default implementations of core AcornDB interfaces.
/// Use this class to access recommended default implementations for serialization, query planning, policy enforcement, trunk storage, and cache strategies.
/// </summary>
public static class AcornDefaults
{
    /// <summary>
    /// Default JSON serializer (System.Text.Json, recommended for performance).
    /// </summary>
    public static readonly ISerializer Serializer = new DefaultJsonSerializer();

    /// <summary>
    /// Default query planner (cost-based, index-aware).
    /// </summary>
    public static IQueryPlanner<T> QueryPlanner<T>(Tree<T> tree) where T : class => new DefaultQueryPlanner<T>(tree);

    /// <summary>
    /// Default policy engine (local, tag-based, thread-safe).
    /// </summary>
    public static readonly IPolicyEngine PolicyEngine = new LocalPolicyEngine();

    /// <summary>
    /// Default trunk for file-based persistence (simple, not recommended for high performance).
    /// </summary>
    public static ITrunk<T> File<T>(string? customPath = null) where T : class => new BitcaskTrunk<T>(customPath);

    /// <summary>
    /// Default cache strategy (LRU, 10,000 items).
    /// </summary>
    public static ICacheStrategy<T> CacheStrategy<T>(int maxSize = 10_000) => new LRUCacheStrategy<T>(maxSize);
}