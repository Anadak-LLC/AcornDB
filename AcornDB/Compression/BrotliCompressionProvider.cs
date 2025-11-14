using System.IO;
using System.IO.Compression;

namespace AcornDB.Compression
{
    /// <summary>
    /// Brotli compression provider (better compression, slower than Gzip)
    /// Available in .NET Core 2.1+
    /// </summary>
    public class BrotliCompressionProvider : ICompressionProvider
    {
        private readonly CompressionLevel _compressionLevel;

        public bool IsEnabled => true;
        public string AlgorithmName => "Brotli";

        /// <summary>
        /// Create with default compression level (Optimal)
        /// </summary>
        public BrotliCompressionProvider() : this(CompressionLevel.Optimal)
        {
        }

        /// <summary>
        /// Create with specific compression level
        /// </summary>
        public BrotliCompressionProvider(CompressionLevel compressionLevel)
        {
            _compressionLevel = compressionLevel;
        }

        public byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return data;

            using var outputStream = new MemoryStream();
            using (var brotliStream = new BrotliStream(outputStream, _compressionLevel))
            {
                brotliStream.Write(data, 0, data.Length);
            }
            return outputStream.ToArray();
        }

        public byte[] Decompress(byte[] compressedData)
        {
            if (compressedData == null || compressedData.Length == 0)
                return compressedData;

            using var inputStream = new MemoryStream(compressedData);
            using var brotliStream = new BrotliStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();

            brotliStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }
    }
}
