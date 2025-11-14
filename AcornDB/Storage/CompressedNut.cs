using System;

namespace AcornDB.Storage
{
    /// <summary>
    /// Wrapper for compressed payload data with metadata
    /// </summary>
    public class CompressedNut
    {
        public byte[] CompressedData { get; set; } = Array.Empty<byte>();
        public int OriginalSize { get; set; }
        public int CompressedSize { get; set; }
        public string Algorithm { get; set; } = "";
        public string OriginalType { get; set; } = "";

        /// <summary>
        /// Compression ratio (e.g., 0.5 = 50% of original size)
        /// </summary>
        public double CompressionRatio => OriginalSize > 0
            ? (double)CompressedSize / OriginalSize
            : 1.0;

        /// <summary>
        /// Space saved in bytes
        /// </summary>
        public int SpaceSaved => OriginalSize - CompressedSize;
    }
}
