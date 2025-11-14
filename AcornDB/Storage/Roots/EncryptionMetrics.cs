using System.Threading;

namespace AcornDB.Storage.Roots
{
    /// <summary>
    /// Metrics for encryption operations (thread-safe)
    /// </summary>
    public class EncryptionMetrics
    {
        private long _totalEncryptions;
        private long _totalDecryptions;
        private long _totalErrors;

        public long TotalEncryptions => Interlocked.Read(ref _totalEncryptions);
        public long TotalDecryptions => Interlocked.Read(ref _totalDecryptions);
        public long TotalErrors => Interlocked.Read(ref _totalErrors);

        internal void RecordEncryption()
        {
            Interlocked.Increment(ref _totalEncryptions);
        }

        internal void RecordDecryption()
        {
            Interlocked.Increment(ref _totalDecryptions);
        }

        internal void RecordError()
        {
            Interlocked.Increment(ref _totalErrors);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _totalEncryptions, 0);
            Interlocked.Exchange(ref _totalDecryptions, 0);
            Interlocked.Exchange(ref _totalErrors, 0);
        }

        public override string ToString()
        {
            return $"Encryptions: {TotalEncryptions}, Decryptions: {TotalDecryptions}, Errors: {TotalErrors}";
        }
    }
}
