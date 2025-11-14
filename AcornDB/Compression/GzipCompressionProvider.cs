using System;
using System.IO;
using System.IO.Compression;

namespace AcornDB.Compression
{
    /// <summary>
    /// Gzip compression provider (fast, good compression ratio)
    /// </summary>
    public class GzipCompressionProvider : ICompressionProvider
    {
        private readonly CompressionLevel _compressionLevel;

        public bool IsEnabled => true;
        public string AlgorithmName => "Gzip";

        /// <summary>
        /// Create with default compression level (Optimal)
        /// </summary>
        public GzipCompressionProvider() : this(CompressionLevel.Optimal)
        {
        }

        /// <summary>
        /// Create with specific compression level
        /// </summary>
        /// <param name="compressionLevel">Fastest, Optimal, or SmallestSize</param>
        public GzipCompressionProvider(CompressionLevel compressionLevel)
        {
            _compressionLevel = compressionLevel;
        }

        public byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return data;

            using var outputStream = new MemoryStream();
            using (var gzipStream = new GZipStream(outputStream, _compressionLevel))
            {
                gzipStream.Write(data, 0, data.Length);
            }
            return outputStream.ToArray();
        }

        public byte[] Decompress(byte[] compressedData)
        {
            if (compressedData == null || compressedData.Length == 0)
                return compressedData;

            using var inputStream = new MemoryStream(compressedData);
            using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();

            gzipStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }
    }
}
