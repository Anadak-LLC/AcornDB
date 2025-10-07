using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AcornDB.Storage;
using Newtonsoft.Json;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// Cloud-backed trunk that works with any ICloudStorageProvider (S3, Azure Blob, etc.)
    /// Stores nuts as JSON files in cloud storage
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    public class CloudTrunk<T> : ITrunk<T>
    {
        private readonly ICloudStorageProvider _cloudStorage;
        private readonly ISerializer _serializer;
        private readonly string _prefix;

        /// <summary>
        /// Create a cloud trunk with the specified storage provider
        /// </summary>
        /// <param name="cloudStorage">Cloud storage provider (S3, Azure, etc.)</param>
        /// <param name="prefix">Optional prefix for all keys (like a folder path)</param>
        /// <param name="serializer">Custom serializer (defaults to Newtonsoft.Json)</param>
        public CloudTrunk(
            ICloudStorageProvider cloudStorage,
            string? prefix = null,
            ISerializer? serializer = null)
        {
            _cloudStorage = cloudStorage ?? throw new ArgumentNullException(nameof(cloudStorage));
            _serializer = serializer ?? new NewtonsoftJsonSerializer();
            _prefix = prefix ?? $"acorndb_{typeof(T).Name}";

            var info = _cloudStorage.GetInfo();
            Console.WriteLine($"☁️ CloudTrunk initialized:");
            Console.WriteLine($"   Provider: {info.ProviderName}");
            Console.WriteLine($"   Bucket: {info.BucketName}");
            Console.WriteLine($"   Prefix: {_prefix}");
        }

        public void Save(string id, Nut<T> nut)
        {
            // Use async bridge pattern
            Task.Run(async () => await SaveAsync(id, nut)).GetAwaiter().GetResult();
        }

        public async Task SaveAsync(string id, Nut<T> nut)
        {
            var key = GetKey(id);
            var json = _serializer.Serialize(nut);
            await _cloudStorage.UploadAsync(key, json);
            Console.WriteLine($"   ☁️ Uploaded {id} to cloud");
        }

        public Nut<T>? Load(string id)
        {
            // Use async bridge pattern
            return Task.Run(async () => await LoadAsync(id)).GetAwaiter().GetResult();
        }

        public async Task<Nut<T>?> LoadAsync(string id)
        {
            var key = GetKey(id);
            var json = await _cloudStorage.DownloadAsync(key);

            if (json == null)
                return null;

            return _serializer.Deserialize<Nut<T>>(json);
        }

        public void Delete(string id)
        {
            // Use async bridge pattern
            Task.Run(async () => await DeleteAsync(id)).GetAwaiter().GetResult();
        }

        public async Task DeleteAsync(string id)
        {
            var key = GetKey(id);
            await _cloudStorage.DeleteAsync(key);
            Console.WriteLine($"   ☁️ Deleted {id} from cloud");
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            // Use async bridge pattern
            return Task.Run(async () => await LoadAllAsync()).GetAwaiter().GetResult();
        }

        public async Task<IEnumerable<Nut<T>>> LoadAllAsync()
        {
            var keys = await _cloudStorage.ListAsync(_prefix);
            var nuts = new List<Nut<T>>();

            foreach (var key in keys)
            {
                try
                {
                    var json = await _cloudStorage.DownloadAsync(key);
                    if (json != null)
                    {
                        var nut = _serializer.Deserialize<Nut<T>>(json);
                        if (nut != null)
                            nuts.Add(nut);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠ Failed to load {key}: {ex.Message}");
                }
            }

            return nuts;
        }

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
            return LoadAll();
        }

        public void ImportChanges(IEnumerable<Nut<T>> changes)
        {
            // Use async bridge pattern
            Task.Run(async () => await ImportChangesAsync(changes)).GetAwaiter().GetResult();
        }

        public async Task ImportChangesAsync(IEnumerable<Nut<T>> changes)
        {
            foreach (var nut in changes)
            {
                await SaveAsync(nut.Id, nut);
            }

            Console.WriteLine($"   ☁️ Imported {changes.Count()} nuts to cloud");
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
    }
}
