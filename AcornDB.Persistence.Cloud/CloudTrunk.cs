using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Storage;
using Newtonsoft.Json;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// High-performance cloud-backed trunk with async-first API, parallel operations,
    /// compression, and optional local caching.
    /// Works with any ICloudStorageProvider (S3, Azure Blob, etc.)
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    public class CloudTrunk<T> : ITrunk<T>, IDisposable
    {
        private readonly ICloudStorageProvider _cloudStorage;
        private readonly ISerializer _serializer;
        private readonly string _prefix;
        private readonly bool _enableCompression;
        private readonly bool _enableLocalCache;
        private readonly int _batchSize;
        private readonly int _parallelDownloads;

        // Batching support
        private readonly List<PendingWrite> _writeBuffer = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Timer? _flushTimer;

        // Optional local cache
        private readonly ConcurrentDictionary<string, Nut<T>>? _localCache;

        private bool _disposed = false;

        private struct PendingWrite
        {
            public string Id;
            public Nut<T> Nut;
        }

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
        {
            _cloudStorage = cloudStorage ?? throw new ArgumentNullException(nameof(cloudStorage));
            _serializer = serializer ?? new NewtonsoftJsonSerializer();
            _prefix = prefix ?? $"acorndb_{typeof(T).Name}";
            _enableCompression = enableCompression;
            _enableLocalCache = enableLocalCache;
            _batchSize = batchSize;
            _parallelDownloads = parallelDownloads;

            if (_enableLocalCache)
            {
                _localCache = new ConcurrentDictionary<string, Nut<T>>();
            }

            // Auto-flush timer for write batching
            _flushTimer = new Timer(_ =>
            {
                try { FlushAsync().Wait(); }
                catch { /* Swallow timer exceptions */ }
            }, null, FLUSH_INTERVAL_MS, FLUSH_INTERVAL_MS);

            var info = _cloudStorage.GetInfo();
            Console.WriteLine($"☁️ CloudTrunk initialized:");
            Console.WriteLine($"   Provider: {info.ProviderName}");
            Console.WriteLine($"   Bucket: {info.BucketName}");
            Console.WriteLine($"   Prefix: {_prefix}");
            Console.WriteLine($"   Compression: {(_enableCompression ? "Enabled" : "Disabled")}");
            Console.WriteLine($"   Local Cache: {(_enableLocalCache ? "Enabled" : "Disabled")}");
            Console.WriteLine($"   Batch Size: {_batchSize}");
        }

        // Synchronous methods - use sparingly, prefer async versions
        public void Stash(string id, Nut<T> nut)
        {
            StashAsync(id, nut).GetAwaiter().GetResult();
        }

        [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
        public void Save(string id, Nut<T> nut) => Stash(id, nut);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task StashAsync(string id, Nut<T> nut)
        {
            // Update local cache immediately
            if (_enableLocalCache)
            {
                _localCache![id] = nut;
            }

            // Add to write buffer for batching
            bool shouldFlush = false;
            lock (_writeBuffer)
            {
                _writeBuffer.Add(new PendingWrite { Id = id, Nut = nut });

                // Check if buffer is full
                if (_writeBuffer.Count >= _batchSize)
                {
                    shouldFlush = true;
                }
            }

            // Flush outside the lock
            if (shouldFlush)
            {
                await FlushAsync();
            }
        }

        [Obsolete("Use StashAsync() instead. This method will be removed in a future version.")]
        public async Task SaveAsync(string id, Nut<T> nut) => await StashAsync(id, nut);

        public Nut<T>? Crack(string id)
        {
            return CrackAsync(id).GetAwaiter().GetResult();
        }

        [Obsolete("Use Crack() instead. This method will be removed in a future version.")]
        public Nut<T>? Load(string id) => Crack(id);

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

            // Decompress if enabled
            if (_enableCompression)
            {
                data = Decompress(data);
            }

            var nut = _serializer.Deserialize<Nut<T>>(data);

            // Update cache
            if (_enableLocalCache && nut != null)
            {
                _localCache![id] = nut;
            }

            return nut;
        }

        [Obsolete("Use CrackAsync() instead. This method will be removed in a future version.")]
        public async Task<Nut<T>?> LoadAsync(string id) => await CrackAsync(id);

        public void Toss(string id)
        {
            TossAsync(id).GetAwaiter().GetResult();
        }

        [Obsolete("Use Toss() instead. This method will be removed in a future version.")]
        public void Delete(string id) => Toss(id);

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
            Console.WriteLine($"   ☁️ Deleted {id} from cloud");
        }

        [Obsolete("Use TossAsync() instead. This method will be removed in a future version.")]
        public async Task DeleteAsync(string id) => await TossAsync(id);

        public IEnumerable<Nut<T>> CrackAll()
        {
            // Use async bridge pattern
            return Task.Run(async () => await CrackAllAsync()).GetAwaiter().GetResult();
        }

        [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
        public IEnumerable<Nut<T>> LoadAll() => CrackAll();

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
                            // Decompress if enabled
                            if (_enableCompression)
                            {
                                data = Decompress(data);
                            }

                            var nut = _serializer.Deserialize<Nut<T>>(data);
                            if (nut != null)
                                nuts.Add(nut);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ⚠ Failed to load {key}: {ex.Message}");
                    }
                });

                await Task.WhenAll(downloadTasks);
            }

            return nuts.ToList();
        }

        [Obsolete("Use CrackAllAsync() instead. This method will be removed in a future version.")]
        public async Task<IEnumerable<Nut<T>>> LoadAllAsync() => await CrackAllAsync();

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            // Cloud storage doesn't natively support versioning in this implementation
            // For versioning, use S3 versioning feature or implement custom history logic
            throw new NotSupportedException(
                "CloudTrunk doesn't support history by default. " +
                "Enable S3 versioning or use a different trunk for history support.");
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return CrackAll();
        }

        public void ImportChanges(IEnumerable<Nut<T>> changes)
        {
            // Use async bridge pattern
            Task.Run(async () => await ImportChangesAsync(changes)).GetAwaiter().GetResult();
        }

        public ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
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

            // Add all to write buffer
            foreach (var nut in changesList)
            {
                // Update local cache
                if (_enableLocalCache)
                {
                    _localCache![nut.Id] = nut;
                }

                lock (_writeBuffer)
                {
                    _writeBuffer.Add(new PendingWrite { Id = nut.Id, Nut = nut });
                }
            }

            // Flush everything
            await FlushAsync();

            Console.WriteLine($"   ☁️ Imported {changesList.Count} nuts to cloud");
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
        /// Flush pending writes to cloud storage
        /// </summary>
        private async Task FlushAsync()
        {
            List<PendingWrite> toWrite;

            lock (_writeBuffer)
            {
                if (_writeBuffer.Count == 0) return;
                toWrite = new List<PendingWrite>(_writeBuffer);
                _writeBuffer.Clear();
            }

            await _writeLock.WaitAsync();
            try
            {
                // Upload all buffered writes in parallel
                var uploadTasks = toWrite.Select(async write =>
                {
                    try
                    {
                        var key = GetKey(write.Id);
                        var data = _serializer.Serialize(write.Nut);

                        // Compress if enabled
                        if (_enableCompression)
                        {
                            data = Compress(data);
                        }

                        await _cloudStorage.UploadAsync(key, data);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ⚠ Failed to upload {write.Id}: {ex.Message}");
                    }
                });

                await Task.WhenAll(uploadTasks);
                Console.WriteLine($"   ☁️ Flushed {toWrite.Count} nuts to cloud");
            }
            finally
            {
                _writeLock.Release();
            }
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
        public void Dispose()
        {
            if (_disposed) return;

            _flushTimer?.Dispose();

            // Flush any pending writes
            try { FlushAsync().Wait(); } catch { }

            _writeLock?.Dispose();

            _disposed = true;
        }

        // IRoot support - stub implementation (to be fully implemented later)
        public IReadOnlyList<IRoot> Roots => Array.Empty<IRoot>();
        public void AddRoot(IRoot root) { /* TODO: Implement root support */ }
        public bool RemoveRoot(string name) => false;
    }
}
