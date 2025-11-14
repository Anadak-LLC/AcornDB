using System;
using AcornDB.Logging;
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
                AcornLog.Info($"⚠️ Compression failed for document '{context.DocumentId}': {ex.Message}");
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
                AcornLog.Info($"⚠️ Decompression failed for document '{context.DocumentId}': {ex.Message}");
                throw new InvalidOperationException($"Failed to decompress data", ex);
            }
        }
    }
}
