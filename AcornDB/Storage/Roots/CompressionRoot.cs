using System;
using AcornDB.Compression;

namespace AcornDB.Storage.Roots
{
    /// <summary>
    /// Root processor that compresses byte streams before storage and decompresses on retrieval.
    /// Reduces storage footprint at the cost of CPU cycles.
    /// Works at the byte level - completely agnostic to data type.
    /// Recommended sequence: 100-199
    /// </summary>
    public class CompressionRoot : IRoot
    {
        private readonly ICompressionProvider _compression;
        private readonly CompressionMetrics _metrics;

        public string Name => "Compression";
        public int Sequence { get; }

        /// <summary>
        /// Compression metrics for monitoring
        /// </summary>
        public CompressionMetrics Metrics => _metrics;

        public CompressionRoot(
            ICompressionProvider compression,
            int sequence = 100)
        {
            _compression = compression ?? throw new ArgumentNullException(nameof(compression));
            Sequence = sequence;
            _metrics = new CompressionMetrics();
        }

        public string GetSignature()
        {
            return $"{_compression.AlgorithmName}";
        }

        public byte[] OnStash(byte[] data, RootProcessingContext context)
        {
            try
            {
                var originalSize = data.Length;

                // Compress the byte array
                var compressed = _compression.Compress(data);
                var compressedSize = compressed.Length;

                // Update metrics
                _metrics.RecordCompression(originalSize, compressedSize);

                // Add signature to transformation chain
                context.TransformationSignatures.Add(GetSignature());

                return compressed;
            }
            catch (Exception ex)
            {
                _metrics.RecordError();
                Console.WriteLine($"⚠️ Compression failed for document '{context.DocumentId}': {ex.Message}");
                throw new InvalidOperationException($"Failed to compress data", ex);
            }
        }

        public byte[] OnCrack(byte[] data, RootProcessingContext context)
        {
            try
            {
                var compressedSize = data.Length;

                // Decompress the byte array
                var decompressed = _compression.Decompress(data);
                var originalSize = decompressed.Length;

                // Update metrics
                _metrics.RecordDecompression(compressedSize, originalSize);

                return decompressed;
            }
            catch (Exception ex)
            {
                _metrics.RecordError();
                Console.WriteLine($"⚠️ Decompression failed for document '{context.DocumentId}': {ex.Message}");
                throw new InvalidOperationException($"Failed to decompress data", ex);
            }
        }
    }

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
