using System.Threading;

namespace AcornDB.Storage.Roots
{
    /// <summary>
    /// Metrics for compression operations (thread-safe)
    /// </summary>
    public class CompressionMetrics
    {
        private long _totalCompressions;
        private long _totalDecompressions;
        private long _totalBytesIn;
        private long _totalBytesOut;
        private long _totalErrors;

        public long TotalCompressions => Interlocked.Read(ref _totalCompressions);
        public long TotalDecompressions => Interlocked.Read(ref _totalDecompressions);
        public long TotalBytesIn => Interlocked.Read(ref _totalBytesIn);
        public long TotalBytesOut => Interlocked.Read(ref _totalBytesOut);
        public long TotalErrors => Interlocked.Read(ref _totalErrors);

        public double AverageCompressionRatio
        {
            get
            {
                var bytesIn = TotalBytesIn;
                return bytesIn > 0 ? (double)TotalBytesOut / bytesIn : 1.0;
            }
        }

        public long TotalBytesSaved => TotalBytesIn - TotalBytesOut;

        internal void RecordCompression(int originalSize, int compressedSize)
        {
            Interlocked.Increment(ref _totalCompressions);
            Interlocked.Add(ref _totalBytesIn, originalSize);
            Interlocked.Add(ref _totalBytesOut, compressedSize);
        }

        internal void RecordDecompression(int compressedSize, int originalSize)
        {
            Interlocked.Increment(ref _totalDecompressions);
        }

        internal void RecordError()
        {
            Interlocked.Increment(ref _totalErrors);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _totalCompressions, 0);
            Interlocked.Exchange(ref _totalDecompressions, 0);
            Interlocked.Exchange(ref _totalBytesIn, 0);
            Interlocked.Exchange(ref _totalBytesOut, 0);
            Interlocked.Exchange(ref _totalErrors, 0);
        }

        public override string ToString()
        {
            return $"Compressions: {TotalCompressions}, Decompressions: {TotalDecompressions}, " +
                   $"Ratio: {AverageCompressionRatio:P2}, Saved: {TotalBytesSaved:N0} bytes, Errors: {TotalErrors}";
        }
    }
}
