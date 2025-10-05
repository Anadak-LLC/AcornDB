
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace AcornDB
{
    public partial class NutShell<T>
    {
        public Guid ChangeId { get; set; } = Guid.NewGuid();
    }

    public partial class Tree<T>
    {
        private readonly ConcurrentQueue<Guid> _recentChangeIds = new();
        private const int ChangeIdMemoryLimit = 100;

        private bool HasSeenChange(Guid changeId)
        {
            return _recentChangeIds.Contains(changeId);
        }

        private void RememberChange(Guid changeId)
        {
            _recentChangeIds.Enqueue(changeId);
            while (_recentChangeIds.Count > ChangeIdMemoryLimit)
                _recentChangeIds.TryDequeue(out _);
        }

        internal void PushToAllTangles(string key, T item)
        {
            if (item is NutShell<T> nut)
            {
                RememberChange(nut.ChangeId);
                foreach (var tangle in _tangles)
                {
                    tangle.PushUpdate(key, item);
                }
            }
        }

        internal bool ShouldApplyChange(Guid changeId)
        {
            if (HasSeenChange(changeId)) return false;
            RememberChange(changeId);
            return true;
        }
    }

    public partial class Tangle<T>
    {
        public void ApplyRemoteChange(string key, NutShell<T> remoteNut, Tree<T> tree)
        {
            if (tree.ShouldApplyChange(remoteNut.ChangeId))
            {
                tree.Squabble(key, remoteNut);
            }
        }
    }
}
