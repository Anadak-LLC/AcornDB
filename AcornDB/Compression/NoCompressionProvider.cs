namespace AcornDB.Compression
{
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
