using System;
using System.Threading;
using AcornDB.Sync;

namespace AcornDB
{
    public class AutoSync<T>
    {
        private readonly ISyncableCollection<T> _local;
        private readonly ISyncableCollection<T> _remote;
        private readonly TimeSpan _interval;
        private Timer? _timer;

        public AutoSync(ISyncableCollection<T> local, ISyncableCollection<T> remote, TimeSpan interval)
        {
            _local = local;
            _remote = remote;
            _interval = interval;
        }

        public void Start()
        {
            _timer = new Timer(SyncNow, null, TimeSpan.Zero, _interval);
        }

        private void SyncNow(object? state)
        {
            try
            {
                var outbound = _local.ExportChanges();
                _remote.ImportChanges(outbound);

                var inbound = _remote.ExportChanges();
                _local.ImportChanges(inbound);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoSync] Sync error: {ex.Message}");
            }
        }

        public void Stop()
        {
            _timer?.Dispose();
        }
    }
}
