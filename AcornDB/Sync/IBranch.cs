using System;
using System.Threading.Tasks;

namespace AcornDB.Sync
{
    /// <summary>
    /// Interface for tree branches that handle change events.
    /// Branches can be sync endpoints, audit loggers, metrics collectors, etc.
    /// </summary>
    public interface IBranch : IDisposable
    {
        /// <summary>
        /// Unique identifier for this branch
        /// </summary>
        string BranchId { get; }

        /// <summary>
        /// Capabilities this branch supports
        /// </summary>
        BranchCapabilities Capabilities { get; }

        /// <summary>
        /// Called when a nut is stashed (added/updated)
        /// </summary>
        void OnStash<T>(Leaf<T> leaf);

        /// <summary>
        /// Called when a nut is tossed (deleted)
        /// </summary>
        void OnToss<T>(Leaf<T> leaf);

        /// <summary>
        /// Called when a conflict is resolved
        /// </summary>
        void OnSquabble<T>(Leaf<T> leaf);

        /// <summary>
        /// Called during shake operations (full sync)
        /// </summary>
        Task OnShakeAsync<T>(Tree<T> tree) where T : class;

        /// <summary>
        /// Flush any batched leaves (if batching is supported)
        /// Called periodically or when batch size limit is reached
        /// </summary>
        void FlushBatch();

        /// <summary>
        /// Get the remote tree ID (if applicable, for loop prevention)
        /// Returns null for non-sync branches like audit/metrics
        /// </summary>
        string? GetRemoteTreeId();
    }
}
