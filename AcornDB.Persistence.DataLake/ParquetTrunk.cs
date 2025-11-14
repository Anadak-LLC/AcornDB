using System;
using AcornDB.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Persistence.Cloud;
using AcornDB.Storage.Serialization;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Newtonsoft.Json;

namespace AcornDB.Persistence.DataLake
{
    /// <summary>
    /// Apache Parquet trunk implementation for data lake interoperability.
    /// Supports local files, S3, Azure Data Lake with columnar storage and partitioning.
    /// Extends TrunkBase to support IRoot pipeline (compression, encryption, policy enforcement).
    ///
    /// Key Features:
    /// - Columnar storage format (optimized for analytics)
    /// - Date/value-based partitioning (data lake pattern)
    /// - Compression (Snappy, GZip, LZ4)
    /// - Cloud storage integration (S3, Azure Data Lake via ICloudStorageProvider)
    /// - Bidirectional sync with data lakes
    ///
    /// Use Cases:
    /// - Export AcornDB data to data lakes for analytics
    /// - Import data lake datasets into AcornDB
    /// - Cold storage with columnar compression
    /// - Interoperability with Spark, Athena, Synapse Analytics
    /// </summary>
    public class ParquetTrunk<T> : TrunkBase<T> where T : class
    {
        private readonly string _basePath;
        private readonly ICloudStorageProvider? _cloudStorage;
        private readonly ParquetOptions _options;
        private readonly string _typeName;

        /// <summary>
        /// Create Parquet trunk for local file system
        /// </summary>
        /// <param name="basePath">Base directory path for Parquet files</param>
        /// <param name="options">Parquet options (compression, partitioning, etc.)</param>
        /// <param name="serializer">Custom serializer (defaults to Newtonsoft.Json)</param>
        public ParquetTrunk(string basePath, ParquetOptions? options = null, ISerializer? serializer = null)
            : base(serializer, enableBatching: false)
        {
            _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
            _cloudStorage = null;
            _options = options ?? new ParquetOptions();
            _typeName = typeof(T).Name;

            // Create base directory for local files
            if (_cloudStorage == null && !string.IsNullOrEmpty(_basePath))
            {
                Directory.CreateDirectory(_basePath);
            }

            AcornLog.Info($"📊 ParquetTrunk initialized:");
            AcornLog.Info($"   Type: {_typeName}");
            AcornLog.Info($"   Path: {_basePath}");
            AcornLog.Info($"   Compression: {_options.CompressionMethod}");
            AcornLog.Info($"   Partitioning: {(_options.PartitionStrategy != null ? "Enabled" : "Disabled")}");
        }

        /// <summary>
        /// Create Parquet trunk for cloud storage (S3, Azure Data Lake)
        /// </summary>
        /// <param name="basePath">Base path/prefix in cloud storage</param>
        /// <param name="cloudStorage">Cloud storage provider</param>
        /// <param name="options">Parquet options</param>
        /// <param name="serializer">Custom serializer (defaults to Newtonsoft.Json)</param>
        public ParquetTrunk(string basePath, ICloudStorageProvider cloudStorage, ParquetOptions? options = null, ISerializer? serializer = null)
            : this(basePath, options, serializer)
        {
            _cloudStorage = cloudStorage ?? throw new ArgumentNullException(nameof(cloudStorage));

            var info = _cloudStorage.GetInfo();
            AcornLog.Info($"   Cloud Provider: {info.ProviderName}");
            AcornLog.Info($"   Bucket: {info.BucketName}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Stash(string id, Nut<T> nut)
        {
            StashAsync(id, nut).GetAwaiter().GetResult();
        }

        public async Task StashAsync(string id, Nut<T> nut)
        {
            // Parquet is optimized for batch writes (columnar format)
            // Single-item writes are inefficient - buffer and flush periodically
            var filePath = GetPartitionedPath(nut);
            var nuts = new List<Nut<T>> { nut };

            // For append mode, we need to read existing data, append, and rewrite
            // This is expensive for single items - consider using in-memory buffer
            if (_options.AppendMode && await FileExistsAsync(filePath))
            {
                var existing = await ReadParquetFileAsync(filePath);

                // Replace or append based on ID
                var existingDict = existing.ToDictionary(n => n.Id);
                existingDict[id] = nut;
                nuts = existingDict.Values.ToList();
            }

            await WriteParquetFileAsync(filePath, nuts);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Nut<T>? Crack(string id)
        {
            return CrackAsync(id).GetAwaiter().GetResult();
        }

        public async Task<Nut<T>?> CrackAsync(string id)
        {
            // Parquet doesn't support indexed lookups - need to scan files
            // For production: maintain a separate index (Hive metastore, Delta Lake, etc.)
            var allNuts = await CrackAllAsync();
            return allNuts.FirstOrDefault(n => n.Id == id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Toss(string id)
        {
            TossAsync(id).GetAwaiter().GetResult();
        }

        public async Task TossAsync(string id)
        {
            // Parquet is immutable - need to read, filter, and rewrite
            // Expensive operation - consider using Delta Lake format for ACID deletes
            var files = await ListParquetFilesAsync();

            foreach (var file in files)
            {
                var nuts = await ReadParquetFileAsync(file);
                var filtered = nuts.Where(n => n.Id != id).ToList();

                if (filtered.Count != nuts.Count)
                {
                    if (filtered.Count > 0)
                    {
                        await WriteParquetFileAsync(file, filtered);
                    }
                    else
                    {
                        // Delete empty partition
                        await DeleteFileAsync(file);
                    }
                }
            }
        }

        public override IEnumerable<Nut<T>> CrackAll()
        {
            return CrackAllAsync().GetAwaiter().GetResult();
        }

        public async Task<IEnumerable<Nut<T>>> CrackAllAsync()
        {
            var files = await ListParquetFilesAsync();
            var allNuts = new List<Nut<T>>();

            foreach (var file in files)
            {
                var nuts = await ReadParquetFileAsync(file);
                allNuts.AddRange(nuts);
            }

            return allNuts;
        }

        public override IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException(
                "ParquetTrunk does not support history. " +
                "Parquet is immutable/append-only. Use Delta Lake format for time travel.");
        }

        public override IEnumerable<Nut<T>> ExportChanges()
        {
            return CrackAll();
        }

        public override void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            ImportChangesAsync(incoming).GetAwaiter().GetResult();
        }

        public override ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = true,
            TrunkType = "ParquetTrunk"
        };

        public async Task ImportChangesAsync(IEnumerable<Nut<T>> incoming)
        {
            var incomingList = incoming.ToList();
            if (!incomingList.Any()) return;

            // Group by partition for efficient writes
            var partitionedNuts = incomingList
                .GroupBy(nut => GetPartitionedPath(nut))
                .ToList();

            AcornLog.Info($"   📊 Importing {incomingList.Count} nuts across {partitionedNuts.Count} partitions");

            foreach (var partition in partitionedNuts)
            {
                var filePath = partition.Key;
                var nuts = partition.ToList();

                // Merge with existing data if append mode
                if (_options.AppendMode && await FileExistsAsync(filePath))
                {
                    var existing = await ReadParquetFileAsync(filePath);
                    var existingDict = existing.ToDictionary(n => n.Id);

                    foreach (var nut in nuts)
                    {
                        existingDict[nut.Id] = nut; // Upsert
                    }

                    nuts = existingDict.Values.ToList();
                }

                await WriteParquetFileAsync(filePath, nuts);
            }

            AcornLog.Info($"   📊 Import complete: {incomingList.Count} nuts");
        }

        /// <summary>
        /// Get partitioned file path based on partition strategy
        /// </summary>
        private string GetPartitionedPath(Nut<T> nut)
        {
            var fileName = $"{_typeName}.parquet";

            if (_options.PartitionStrategy == null)
            {
                return _cloudStorage == null
                    ? Path.Combine(_basePath, fileName)
                    : $"{_basePath}/{fileName}";
            }

            var partitionPath = _options.PartitionStrategy.GetPartitionPath(nut);

            return _cloudStorage == null
                ? Path.Combine(_basePath, partitionPath, fileName)
                : $"{_basePath}/{partitionPath}/{fileName}";
        }

        /// <summary>
        /// Write nuts to Parquet file
        /// </summary>
        private async Task WriteParquetFileAsync(string filePath, List<Nut<T>> nuts)
        {
            if (!nuts.Any()) return;

            // Create schema from Nut<T> structure
            var schema = CreateParquetSchema();

            using var ms = new MemoryStream();

            // Write to Parquet format
            using (var writer = await ParquetWriter.CreateAsync(schema, ms))
            {
                writer.CompressionMethod = _options.CompressionMethod;

                using var groupWriter = writer.CreateRowGroup();

                // Write each field as a column
                await WriteColumn(groupWriter, "Id", nuts.Select(n => n.Id).ToArray());
                await WriteColumn(groupWriter, "Version", nuts.Select(n => n.Version).ToArray());
                await WriteColumn(groupWriter, "Timestamp", nuts.Select(n => n.Timestamp.Ticks).ToArray());
                await WriteColumn(groupWriter, "Payload", nuts.Select(n => JsonConvert.SerializeObject(n.Payload)).ToArray());

                // Optional fields
                await WriteColumn(groupWriter, "ExpiresAt",
                    nuts.Select(n => n.ExpiresAt?.Ticks).ToArray());
            }

            ms.Position = 0;

            // Write to local file or cloud storage
            if (_cloudStorage == null)
            {
                // Local file system
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await using var fs = File.Create(filePath);
                await ms.CopyToAsync(fs);
            }
            else
            {
                // Cloud storage - upload as byte array or stream
                var bytes = ms.ToArray();
                var base64 = Convert.ToBase64String(bytes);
                await _cloudStorage.UploadAsync(filePath, base64);
            }
        }

        /// <summary>
        /// Read nuts from Parquet file
        /// </summary>
        private async Task<List<Nut<T>>> ReadParquetFileAsync(string filePath)
        {
            Stream stream;

            if (_cloudStorage == null)
            {
                // Local file system
                if (!File.Exists(filePath))
                    return new List<Nut<T>>();

                stream = File.OpenRead(filePath);
            }
            else
            {
                // Cloud storage
                var base64 = await _cloudStorage.DownloadAsync(filePath);
                if (base64 == null)
                    return new List<Nut<T>>();

                var bytes = Convert.FromBase64String(base64);
                stream = new MemoryStream(bytes);
            }

            using (stream)
            {
                using var reader = await ParquetReader.CreateAsync(stream);
                var nuts = new List<Nut<T>>();

                for (int i = 0; i < reader.RowGroupCount; i++)
                {
                    using var rowGroupReader = reader.OpenRowGroupReader(i);

                    var ids = await ReadColumn<string>(rowGroupReader, "Id");
                    var versions = await ReadColumn<int>(rowGroupReader, "Version");
                    var timestamps = await ReadColumn<long>(rowGroupReader, "Timestamp");
                    var payloads = await ReadColumn<string>(rowGroupReader, "Payload");
                    var expiresAtTicks = await ReadColumn<long?>(rowGroupReader, "ExpiresAt");

                    for (int j = 0; j < ids.Length; j++)
                    {
                        var nut = new Nut<T>
                        {
                            Id = ids[j],
                            Version = versions[j],
                            Timestamp = new DateTime(timestamps[j]),
                            Payload = JsonConvert.DeserializeObject<T>(payloads[j])!,
                            ExpiresAt = expiresAtTicks?[j] != null
                                ? new DateTime(expiresAtTicks[j]!.Value)
                                : null
                        };

                        nuts.Add(nut);
                    }
                }

                return nuts;
            }
        }

        /// <summary>
        /// Create Parquet schema for Nut
        /// </summary>
        private ParquetSchema CreateParquetSchema()
        {
            return new ParquetSchema(
                new DataField<string>("Id"),
                new DataField<int>("Version"),
                new DataField<long>("Timestamp"),
                new DataField<string>("Payload"),
                new DataField<long?>("ExpiresAt")
            );
        }

        /// <summary>
        /// Write column to Parquet
        /// </summary>
        private async Task WriteColumn<TCol>(ParquetRowGroupWriter groupWriter, string columnName, TCol[] values)
        {
            await groupWriter.WriteColumnAsync(new DataColumn(
                new DataField<TCol>(columnName),
                values));
        }

        /// <summary>
        /// Read column from Parquet
        /// </summary>
        private async Task<TCol[]> ReadColumn<TCol>(ParquetRowGroupReader groupReader, string columnName)
        {
            var dataField = new DataField<TCol>(columnName);
            var column = await groupReader.ReadColumnAsync(dataField);
            return column.Data.Cast<TCol>().ToArray();
        }

        /// <summary>
        /// List all Parquet files in base path
        /// </summary>
        private async Task<List<string>> ListParquetFilesAsync()
        {
            if (_cloudStorage == null)
            {
                // Local file system
                if (!Directory.Exists(_basePath))
                    return new List<string>();

                return Directory.GetFiles(_basePath, "*.parquet", SearchOption.AllDirectories).ToList();
            }
            else
            {
                // Cloud storage
                var keys = await _cloudStorage.ListAsync(_basePath);
                return keys.Where(k => k.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        private async Task<bool> FileExistsAsync(string filePath)
        {
            if (_cloudStorage == null)
            {
                return File.Exists(filePath);
            }
            else
            {
                return await _cloudStorage.ExistsAsync(filePath);
            }
        }

        private async Task DeleteFileAsync(string filePath)
        {
            if (_cloudStorage == null)
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            else
            {
                await _cloudStorage.DeleteAsync(filePath);
            }
        }

        // ITrunkCapabilities implementation
        public bool SupportsHistory => false; // Parquet is immutable
        public bool SupportsSync => true;
        public bool IsDurable => true;
        public bool SupportsAsync => true;
        public string TrunkType => "ParquetTrunk";

        public override void Dispose()
        {
            if (_disposed) return;

            // Call base class disposal
            base.Dispose();
        }
    }
}
