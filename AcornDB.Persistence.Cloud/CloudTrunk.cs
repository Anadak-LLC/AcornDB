using System;
using AcornDB.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Policy;
using AcornDB.Storage;
using AcornDB.Storage.Serialization;
using Newtonsoft.Json;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// High-performance cloud-backed trunk with async-first API, parallel operations,
    /// compression, and optional local caching.
    /// Works with any ICloudStorageProvider (S3, Azure Blob, etc.)
    ///
    /// Supports extensible IRoot processors for compression, encryption, policy enforcement, etc.
    ///
    /// Storage Pipeline:
    /// Write: Nut<T> → Serialize → Root Chain (ascending) → byte[] → Base64 → Upload to cloud
    /// Read: Download from cloud → Base64 decode → byte[] → Root Chain (descending) → Deserialize → Nut<T>
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    public class CloudTrunk<T> : TrunkBase<T> where T : class
    {
        private readonly ICloudStorageProvider _cloudStorage;
        private readonly string _prefix;
        private readonly bool _enableCompression;
        private readonly bool _enableLocalCache;
        private readonly int _batchSize;
        private readonly int _parallelDownloads;

        // Optional local cache
        private readonly ConcurrentDictionary<string, Nut<T>>? _localCache;

        private const int DEFAULT_BATCH_SIZE = 50;
        private const int DEFAULT_PARALLEL_DOWNLOADS = 10;
        private const int FLUSH_INTERVAL_MS = 500;

        /// <summary>
        /// Create a cloud trunk with the specified storage provider
        /// </summary>
        /// <param name="cloudStorage">Cloud storage provider (S3, Azure, etc.)</param>
        /// <param name="prefix">Optional prefix for all keys (like a folder path)</param>
        /// <param name="serializer">Custom serializer (defaults to Newtonsoft.Json)</param>
        /// <param name="enableCompression">Enable GZip compression (70-90% size reduction)</param>
        /// <param name="enableLocalCache">Enable in-memory caching of frequently accessed nuts</param>
        /// <param name="batchSize">Number of writes to buffer before auto-flush (default: 50)</param>
        /// <param name="parallelDownloads">Maximum parallel downloads for bulk operations (default: 10)</param>
        public CloudTrunk(
            ICloudStorageProvider cloudStorage,
            string? prefix = null,
            ISerializer? serializer = null,
            bool enableCompression = true,
            bool enableLocalCache = true,
            int batchSize = DEFAULT_BATCH_SIZE,
            int parallelDownloads = DEFAULT_PARALLEL_DOWNLOADS)
            : base(
                serializer,
                enableBatching: true,            // Enable batching via TrunkBase
                batchThreshold: batchSize,
                flushIntervalMs: FLUSH_INTERVAL_MS)
        {
            _cloudStorage = cloudStorage ?? throw new ArgumentNullException(nameof(cloudStorage));
            _prefix = prefix ?? $"acorndb_{typeof(T).Name}";
            _enableCompression = enableCompression;
            _enableLocalCache = enableLocalCache;
            _batchSize = batchSize;
            _parallelDownloads = parallelDownloads;

            if (_enableLocalCache)
            {
                _localCache = new ConcurrentDictionary<string, Nut<T>>();
            }

            var info = _cloudStorage.GetInfo();
            AcornLog.Info($"☁️ CloudTrunk initialized:");
            AcornLog.Info($"   Provider: {info.ProviderName}");
            AcornLog.Info($"   Bucket: {info.BucketName}");
            AcornLog.Info($"   Prefix: {_prefix}");
            AcornLog.Info($"   Compression: {(_enableCompression ? "Enabled" : "Disabled")}");
            AcornLog.Info($"   Local Cache: {(_enableLocalCache ? "Enabled" : "Disabled")}");
            AcornLog.Info($"   Batch Size: {_batchSize}");
        }

        // Synchronous methods - use sparingly, prefer async versions
        public override void Stash(string id, Nut<T> nut)
        {
            // Update local cache immediately
            if (_enableLocalCache)
            {
                _localCache![id] = nut;
            }

            // Use TrunkBase batching infrastructure
            // This handles IRoot pipeline processing, batching, and auto-flush
            StashWithBatchingAsync(id, nut).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task StashAsync(string id, Nut<T> nut)
        {
            // Update local cache immediately
            if (_enableLocalCache)
            {
                _localCache![id] = nut;
            }

            // Use TrunkBase batching infrastructure
            await StashWithBatchingAsync(id, nut);
        }

        [Obsolete("Use StashAsync() instead. This method will be removed in a future version.")]
        public async Task SaveAsync(string id, Nut<T> nut) => await StashAsync(id, nut);

        public override Nut<T>? Crack(string id)
        {
            return CrackAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Nut<T>?> CrackAsync(string id)
        {
            // Check local cache first
            if (_enableLocalCache && _localCache!.TryGetValue(id, out var cached))
            {
                return cached;
            }

            var key = GetKey(id);
            var data = await _cloudStorage.DownloadAsync(key);

            if (data == null)
                return null;

            // Step 1: Convert from storage format to bytes
            byte[] storedBytes;
            if (_enableCompression && !_roots.Any(r => r.Name.Contains("Compression", StringComparison.OrdinalIgnoreCase)))
            {
                // Legacy compression path
                var decompressedJson = Decompress(data);
                storedBytes = Encoding.UTF8.GetBytes(decompressedJson);
            }
            else
            {
                // IRoot pipeline path - data is base64
                storedBytes = Convert.FromBase64String(data);
            }

            // Step 2: Process through IRoot chain using base class helper
            var processedBytes = ProcessThroughRootsDescending(storedBytes, id);

            // Step 3: Deserialize from bytes to Nut<T>
            Nut<T>? nut;
            try
            {
                var json = Encoding.UTF8.GetString(processedBytes);
                nut = _serializer.Deserialize<Nut<T>>(json);
            }
            catch (Exception ex)
            {
                AcornLog.Info($"⚠️ Failed to deserialize nut '{id}': {ex.Message}");
                return null;
            }

            // Update cache
            if (_enableLocalCache && nut != null)
            {
                _localCache![id] = nut;
            }

            return nut;
        }

        [Obsolete("Use CrackAsync() instead. This method will be removed in a future version.")]
        public async Task<Nut<T>?> LoadAsync(string id) => await CrackAsync(id);

        public override void Toss(string id)
        {
            TossAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task TossAsync(string id)
        {
            // Remove from cache
            if (_enableLocalCache)
            {
                _localCache!.TryRemove(id, out _);
            }

            var key = GetKey(id);
            await _cloudStorage.DeleteAsync(key);
            AcornLog.Info($"   ☁️ Deleted {id} from cloud");
        }

        [Obsolete("Use TossAsync() instead. This method will be removed in a future version.")]
        public async Task DeleteAsync(string id) => await TossAsync(id);

        public override IEnumerable<Nut<T>> CrackAll()
        {
            // Use async bridge pattern
            return Task.Run(async () => await CrackAllAsync()).GetAwaiter().GetResult();
        }

        public async Task<IEnumerable<Nut<T>>> CrackAllAsync()
        {
            var keys = await _cloudStorage.ListAsync(_prefix);
            var nuts = new ConcurrentBag<Nut<T>>();

            // Parallel downloads for better performance
            var keyGroups = keys.Chunk(_parallelDownloads);

            foreach (var keyGroup in keyGroups)
            {
                var downloadTasks = keyGroup.Select(async key =>
                {
                    try
                    {
                        var data = await _cloudStorage.DownloadAsync(key);
                        if (data != null)
                        {
                            // Step 1: Convert from storage format to bytes
                            byte[] storedBytes;
                            if (_enableCompression && !_roots.Any(r => r.Name.Contains("Compression", StringComparison.OrdinalIgnoreCase)))
                            {
                                // Legacy compression path
                                var decompressedJson = Decompress(data);
                                storedBytes = Encoding.UTF8.GetBytes(decompressedJson);
                            }
                            else
                            {
                                // IRoot pipeline path - data is base64
                                storedBytes = Convert.FromBase64String(data);
                            }

                            // Step 2: Process through IRoot chain using base class helper
                            var processedBytes = ProcessThroughRootsDescending(storedBytes, key);

                            // Step 3: Deserialize from bytes to Nut<T>
                            var json = Encoding.UTF8.GetString(processedBytes);
                            var nut = _serializer.Deserialize<Nut<T>>(json);
                            if (nut != null)
                                nuts.Add(nut);
                        }
                    }
                    catch (Exception ex)
                    {
                        AcornLog.Info($"   ⚠ Failed to load {key}: {ex.Message}");
                    }
                });

                await Task.WhenAll(downloadTasks);
            }

            return nuts.ToList();
        }

        [Obsolete("Use CrackAllAsync() instead. This method will be removed in a future version.")]
        public async Task<IEnumerable<Nut<T>>> LoadAllAsync() => await CrackAllAsync();

        public override IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            // Cloud storage doesn't natively support versioning in this implementation
            // For versioning, use S3 versioning feature or implement custom history logic
            throw new NotSupportedException(
                "CloudTrunk doesn't support history by default. " +
                "Enable S3 versioning or use a different trunk for history support.");
        }

        public override IEnumerable<Nut<T>> ExportChanges()
        {
            return CrackAll();
        }

        public override void ImportChanges(IEnumerable<Nut<T>> changes)
        {
            // Use async bridge pattern
            Task.Run(async () => await ImportChangesAsync(changes)).GetAwaiter().GetResult();
        }

        public override ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = true,
            TrunkType = "CloudTrunk"
        };

        public async Task ImportChangesAsync(IEnumerable<Nut<T>> changes)
        {
            var changesList = changes.ToList();

            // Stash all nuts (batching handled by TrunkBase)
            foreach (var nut in changesList)
            {
                await StashAsync(nut.Id, nut);
            }

            // Force flush
            await FlushBatchAsync();

            AcornLog.Info($"   ☁️ Imported {changesList.Count} nuts to cloud");
        }

        public ITrunkCapabilities GetCapabilities()
        {
            return new TrunkCapabilities
            {
                TrunkType = "CloudTrunk",
                SupportsHistory = false, // Unless S3 versioning is enabled
                SupportsSync = true,
                IsDurable = true,
                SupportsAsync = true
            };
        }

        /// <summary>
        /// Check if a nut exists in cloud storage
        /// </summary>
        public bool Exists(string id)
        {
            return Task.Run(async () => await ExistsAsync(id)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Check if a nut exists in cloud storage (async)
        /// </summary>
        public async Task<bool> ExistsAsync(string id)
        {
            var key = GetKey(id);
            return await _cloudStorage.ExistsAsync(key);
        }

        /// <summary>
        /// Get cloud storage provider info
        /// </summary>
        public CloudStorageInfo GetCloudInfo()
        {
            return _cloudStorage.GetInfo();
        }

        private string GetKey(string id)
        {
            // Sanitize ID for cloud storage key
            var sanitized = string.Join("_", id.Split(Path.GetInvalidFileNameChars()));
            return $"{_prefix}/{sanitized}.json";
        }

        /// <summary>
        /// Write a single item to cloud storage (used by TrunkBase for immediate writes if needed)
        /// </summary>
        protected override async Task WriteToStorageAsync(string id, byte[] processedBytes, DateTime timestamp, int version)
        {
            await UploadToCloud(id, processedBytes);
        }

        /// <summary>
        /// Write a batch of items to cloud storage (optimized with parallel uploads)
        /// </summary>
        protected override async Task WriteBatchToStorageAsync(List<PendingWrite> batch)
        {
            // Upload all buffered writes in parallel for performance
            var uploadTasks = batch.Select(async write =>
            {
                try
                {
                    await UploadToCloud(write.Id, write.ProcessedData);
                }
                catch (Exception ex)
                {
                    AcornLog.Info($"   ⚠ Failed to upload {write.Id}: {ex.Message}");
                }
            });

            await Task.WhenAll(uploadTasks);
            AcornLog.Info($"   ☁️ Flushed {batch.Count} nuts to cloud");
        }

        /// <summary>
        /// Helper method to upload processed data to cloud storage
        /// </summary>
        private async Task UploadToCloud(string id, byte[] processedBytes)
        {
            var key = GetKey(id);

            // Legacy compression (if enabled AND no compression root)
            // This maintains backward compatibility
            string data;
            if (_enableCompression && !_roots.Any(r => r.Name.Contains("Compression", StringComparison.OrdinalIgnoreCase)))
            {
                data = Compress(Encoding.UTF8.GetString(processedBytes));
            }
            else
            {
                // Convert processed bytes to base64 for cloud storage
                data = Convert.ToBase64String(processedBytes);
            }

            await _cloudStorage.UploadAsync(key, data);
        }

        /// <summary>
        /// Compress string data using GZip (70-90% size reduction)
        /// Returns Base64-encoded compressed data
        /// </summary>
        private string Compress(string data)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }
            return Convert.ToBase64String(output.ToArray());
        }

        /// <summary>
        /// Decompress Base64-encoded GZip data to string
        /// </summary>
        private string Decompress(string data)
        {
            var bytes = Convert.FromBase64String(data);
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray());
        }

        /// <summary>
        /// Dispose and flush any pending writes
        /// </summary>
        public override void Dispose()
        {
            if (_disposed) return;

            // Base class handles timer disposal and flush
            // This ensures proper batching cleanup
            base.Dispose();

            // CloudTrunk has no additional resources to dispose
        }
    }
}
