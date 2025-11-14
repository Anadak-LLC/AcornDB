using System;
using System.Threading;

namespace AcornDB.Storage.Roots
{
    /// <summary>
    /// Metrics for managed index operations (thread-safe)
    /// </summary>
    public class ManagedIndexMetrics
    {
        private long _totalStashes;
        private long _totalCracks;
        private long _totalErrors;
        private DateTime _lastStash;
        private DateTime _lastCrack;

        public long TotalStashes => Interlocked.Read(ref _totalStashes);
        public long TotalCracks => Interlocked.Read(ref _totalCracks);
        public long TotalErrors => Interlocked.Read(ref _totalErrors);
        public DateTime LastStash => _lastStash;
        public DateTime LastCrack => _lastCrack;

        internal void RecordStash(string? documentId)
        {
            Interlocked.Increment(ref _totalStashes);
            _lastStash = DateTime.UtcNow;
        }

        internal void RecordCrack(string? documentId)
        {
            Interlocked.Increment(ref _totalCracks);
            _lastCrack = DateTime.UtcNow;
        }

        internal void RecordError()
        {
            Interlocked.Increment(ref _totalErrors);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _totalStashes, 0);
            Interlocked.Exchange(ref _totalCracks, 0);
            Interlocked.Exchange(ref _totalErrors, 0);
            _lastStash = DateTime.MinValue;
            _lastCrack = DateTime.MinValue;
        }

        public override string ToString()
        {
            return $"Stashes: {TotalStashes}, Cracks: {TotalCracks}, Errors: {TotalErrors}, " +
                   $"Last Stash: {LastStash:yyyy-MM-dd HH:mm:ss}, Last Crack: {LastCrack:yyyy-MM-dd HH:mm:ss}";
        }
    }
}
