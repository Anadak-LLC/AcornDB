using System;

namespace AcornDB.Compression
{
    /// <summary>
    /// Interface for pluggable compression providers
    /// </summary>
    public interface ICompressionProvider
    {
        /// <summary>
        /// Compress data
        /// </summary>
        byte[] Compress(byte[] data);

        /// <summary>
        /// Decompress data
        /// </summary>
        byte[] Decompress(byte[] compressedData);

        /// <summary>
        /// Check if this provider is enabled
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Name of the compression algorithm
        /// </summary>
        string AlgorithmName { get; }
    }

    /// <summary>
    /// No-op compression provider (passes through data unchanged)
    /// </summary>
    public class NoCompressionProvider : ICompressionProvider
    {
        public bool IsEnabled => false;
        public string AlgorithmName => "None";

        public byte[] Compress(byte[] data) => data;
        public byte[] Decompress(byte[] compressedData) => compressedData;
    }
}
