using Parquet;

namespace AcornDB.Persistence.DataLake
{
    /// <summary>
    /// Configuration options for ParquetTrunk
    /// </summary>
    public class ParquetOptions
    {
        /// <summary>
        /// Compression method for Parquet files
        /// Default: Snappy (fast compression/decompression, good compression ratio)
        /// </summary>
        public CompressionMethod CompressionMethod { get; set; } = CompressionMethod.Snappy;

        /// <summary>
        /// Partition strategy for data lake organization
        /// Default: null (no partitioning, single file)
        /// </summary>
        public IPartitionStrategy? PartitionStrategy { get; set; } = null;

        /// <summary>
        /// Append mode - merge new data with existing files
        /// Default: true (data lake pattern)
        /// </summary>
        public bool AppendMode { get; set; } = true;

        /// <summary>
        /// Row group size (number of rows per row group in Parquet file)
        /// Default: 100,000 (good balance for most use cases)
        /// </summary>
        public int RowGroupSize { get; set; } = 100_000;

        /// <summary>
        /// Default options with Snappy compression and no partitioning
        /// </summary>
        public static ParquetOptions Default => new ParquetOptions();

        /// <summary>
        /// Optimized for analytics (GZip compression, large row groups)
        /// </summary>
        public static ParquetOptions Analytics => new ParquetOptions
        {
            CompressionMethod = CompressionMethod.Gzip,
            RowGroupSize = 1_000_000,
            AppendMode = false // Immutable files
        };

        /// <summary>
        /// Optimized for streaming (Snappy compression, small row groups)
        /// </summary>
        public static ParquetOptions Streaming => new ParquetOptions
        {
            CompressionMethod = CompressionMethod.Snappy,
            RowGroupSize = 10_000,
            AppendMode = true
        };
    }
}
