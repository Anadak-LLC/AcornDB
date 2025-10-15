using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AcornDB.Storage;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// Azure Blob Storage trunk - convenient wrapper over CloudTrunk with AzureBlobProvider.
    /// Provides simple API while leveraging all CloudTrunk optimizations (compression, batching, caching, parallel downloads).
    /// </summary>
    public class AzureTrunk<T> : ITrunk<T>, IDisposable
    {
        private readonly CloudTrunk<T> _cloudTrunk;

        /// <summary>
        /// Create Azure trunk with connection string
        /// </summary>
        /// <param name="connectionString">Azure Storage connection string</param>
        /// <param name="containerName">Optional container name. Default: {TypeName}-acorns</param>
        /// <param name="enableCompression">Enable GZip compression (70-90% size reduction, default: true)</param>
        /// <param name="enableLocalCache">Enable in-memory caching (default: true)</param>
        /// <param name="batchSize">Write batch size (default: 50)</param>
        public AzureTrunk(
            string connectionString,
            string? containerName = null,
            bool enableCompression = true,
            bool enableLocalCache = true,
            int batchSize = 50)
        {
            containerName ??= typeof(T).Name.ToLower() + "-acorns";

            var provider = new AzureBlobProvider(connectionString, containerName);

            // Ensure container exists
            provider.EnsureContainerExistsAsync().GetAwaiter().GetResult();

            // Create optimized CloudTrunk with provider
            _cloudTrunk = new CloudTrunk<T>(
                provider,
                prefix: null, // AzureBlobProvider handles container
                serializer: null,
                enableCompression: enableCompression,
                enableLocalCache: enableLocalCache,
                batchSize: batchSize);
        }

        /// <summary>
        /// Create Azure trunk with SAS URI
        /// </summary>
        /// <param name="sasUri">Shared Access Signature URI with container access</param>
        /// <param name="enableCompression">Enable GZip compression (70-90% size reduction, default: true)</param>
        /// <param name="enableLocalCache">Enable in-memory caching (default: true)</param>
        /// <param name="batchSize">Write batch size (default: 50)</param>
        public AzureTrunk(
            Uri sasUri,
            bool enableCompression = true,
            bool enableLocalCache = true,
            int batchSize = 50)
        {
            var provider = new AzureBlobProvider(sasUri);

            // Ensure container exists
            provider.EnsureContainerExistsAsync().GetAwaiter().GetResult();

            _cloudTrunk = new CloudTrunk<T>(
                provider,
                prefix: null,
                serializer: null,
                enableCompression: enableCompression,
                enableLocalCache: enableLocalCache,
                batchSize: batchSize);
        }

        // Delegate all operations to CloudTrunk (with optimizations!)
        public void Save(string id, Nut<T> shell) => _cloudTrunk.Save(id, shell);
        public Nut<T>? Load(string id) => _cloudTrunk.Load(id);
        public void Delete(string id) => _cloudTrunk.Delete(id);
        public IEnumerable<Nut<T>> LoadAll() => _cloudTrunk.LoadAll();
        public IReadOnlyList<Nut<T>> GetHistory(string id) => _cloudTrunk.GetHistory(id);
        public IEnumerable<Nut<T>> ExportChanges() => _cloudTrunk.ExportChanges();
        public void ImportChanges(IEnumerable<Nut<T>> incoming) => _cloudTrunk.ImportChanges(incoming);

        // Async variants
        public Task SaveAsync(string id, Nut<T> shell) => _cloudTrunk.SaveAsync(id, shell);
        public Task<Nut<T>?> LoadAsync(string id) => _cloudTrunk.LoadAsync(id);
        public Task DeleteAsync(string id) => _cloudTrunk.DeleteAsync(id);
        public Task<IEnumerable<Nut<T>>> LoadAllAsync() => _cloudTrunk.LoadAllAsync();
        public Task ImportChangesAsync(IEnumerable<Nut<T>> incoming) => _cloudTrunk.ImportChangesAsync(incoming);

        public void Dispose() => _cloudTrunk?.Dispose();
    }
}
