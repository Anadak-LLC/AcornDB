using AcornDB.Sync;
using AcornDB.Logging;

namespace AcornDB
{
    public partial class Tangle<T> : IDisposable where T : class
    {
        private readonly Tree<T> _local;
        private readonly Sync.Branch _remoteBranch;
        private readonly string _id;
        private bool _isDisposed;

        public Tangle(Tree<T> local, Branch remoteBranch, string id)
        {
            _local = local;
            _remoteBranch = remoteBranch;
            _id = id;
            _local.RegisterTangle(this);
        }

        public void PushUpdate(string key, T item)
        {
            ThrowIfDisposed();

            var shell = new Nut<T>
            {
                Id = key,
                Payload = item,
                Timestamp = DateTime.UtcNow
            };
            _remoteBranch.TryPush(key, shell);
        }

        public void PushDelete(string key)
        {
            ThrowIfDisposed();

            AcornLog.Info($"> 🔄 Tangle '{_id}': Push delete for '{key}'");
            _remoteBranch.TryDelete<T>(key);
        }

        public void PushAll(Tree<T> tree)
        {
            ThrowIfDisposed();

            AcornLog.Info($"> 🍃 Tangle '{_id}' pushing all to remote...");
            foreach (var shell in tree.ExportChanges())
            {
                _remoteBranch.TryPush(shell.Id, shell);
            }
        }

        /// <summary>
        /// Break the tangle connection (nutty alias for Dispose)
        /// Unregisters from tree and releases resources
        /// </summary>
        public void Break()
        {
            if (!_isDisposed)
            {
                AcornLog.Info($"> 💔 Tangle '{_id}' broken!");
            }
            Dispose();
        }

        /// <summary>
        /// Dispose of the tangle and release resources
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            // Unregister from tree
            _local?.UnregisterTangle(this);

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Check if this tangle has been disposed
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(Tangle<T>),
                    $"Cannot use tangle '{_id}' - it has been broken (disposed).");
            }
        }
    }
}
