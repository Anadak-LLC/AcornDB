using System;
using AcornDB.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AcornDB.Sync
{
    /// <summary>
    /// AuditBranch: Example observability branch that logs all changes
    /// Demonstrates non-sync use of IBranch for auditing/observability
    /// </summary>
    public class AuditBranch : IBranch
    {
        public string BranchId { get; }
        public BranchCapabilities Capabilities => BranchCapabilities.All;

        private readonly List<AuditEntry> _auditLog = new();
        private readonly int _maxLogSize;
        private bool _isDisposed;

        public AuditBranch(int maxLogSize = 1000)
        {
            BranchId = $"audit-{Guid.NewGuid().ToString().Substring(0, 8)}";
            _maxLogSize = maxLogSize;
        }

        /// <summary>
        /// Get all audit entries (for inspection and testing)
        /// </summary>
        public IReadOnlyList<AuditEntry> GetAuditLog() => _auditLog.AsReadOnly();

        /// <summary>
        /// Get recent audit entries
        /// </summary>
        public IEnumerable<AuditEntry> GetRecentEntries(int count)
        {
            var startIndex = Math.Max(0, _auditLog.Count - count);
            return _auditLog.GetRange(startIndex, _auditLog.Count - startIndex);
        }

        /// <summary>
        /// Clear the audit log
        /// </summary>
        public void ClearLog()
        {
            _auditLog.Clear();
        }

        public void OnStash<T>(Leaf<T> leaf)
        {
            ThrowIfDisposed();
            LogLeaf("STASH", leaf);
        }

        public void OnToss<T>(Leaf<T> leaf)
        {
            ThrowIfDisposed();
            LogLeaf("TOSS", leaf);
        }

        public void OnSquabble<T>(Leaf<T> leaf)
        {
            ThrowIfDisposed();
            LogLeaf("SQUABBLE", leaf);
        }

        public Task OnShakeAsync<T>(Tree<T> tree) where T : class
        {
            ThrowIfDisposed();
            _auditLog.Add(new AuditEntry
            {
                Timestamp = DateTime.UtcNow,
                Action = "SHAKE",
                TreeType = typeof(T).Name,
                Details = $"Shake operation performed on tree"
            });
            return Task.CompletedTask;
        }

        public void FlushBatch()
        {
            // No batching needed for audit logs
        }

        public string? GetRemoteTreeId()
        {
            // Audit branches don't have remote trees
            return null;
        }

        private void LogLeaf<T>(string action, Leaf<T> leaf)
        {
            // Implement LRU eviction if log gets too large
            if (_auditLog.Count >= _maxLogSize)
            {
                _auditLog.RemoveAt(0);
            }

            _auditLog.Add(new AuditEntry
            {
                Timestamp = leaf.Timestamp,
                Action = action,
                LeafId = leaf.LeafId,
                OriginTreeId = leaf.OriginTreeId,
                Key = leaf.Key,
                TreeType = typeof(T).Name,
                HopCount = leaf.HopCount,
                Details = $"{action} on key '{leaf.Key}' from tree {leaf.OriginTreeId} (hop {leaf.HopCount})"
            });

            AcornLog.Info($"> 📝 AUDIT [{BranchId}]: {action} - {leaf.Key} from {leaf.OriginTreeId}");
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(AuditBranch),
                    $"Cannot use audit branch {BranchId} - it has been disposed.");
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            AcornLog.Info($"> 📝 AuditBranch {BranchId} disposed ({_auditLog.Count} entries logged)");
            _auditLog.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
