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
    public class AzureTrunk<T> : ITrunk<T>, IDisposable where T : class
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
        public void Stash(string id, Nut<T> shell) => _cloudTrunk.Stash(id, shell);
        [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
        public void Save(string id, Nut<T> shell) => Stash(id, shell);

        public Nut<T>? Crack(string id) => _cloudTrunk.Crack(id);
        [Obsolete("Use Crack() instead. This method will be removed in a future version.")]
        public Nut<T>? Load(string id) => Crack(id);

        public void Toss(string id) => _cloudTrunk.Toss(id);
        [Obsolete("Use Toss() instead. This method will be removed in a future version.")]
        public void Delete(string id) => Toss(id);

        public IEnumerable<Nut<T>> CrackAll() => _cloudTrunk.CrackAll();
        [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
        public IEnumerable<Nut<T>> LoadAll() => CrackAll();

        public IReadOnlyList<Nut<T>> GetHistory(string id) => _cloudTrunk.GetHistory(id);
        public IEnumerable<Nut<T>> ExportChanges() => _cloudTrunk.ExportChanges();
        public void ImportChanges(IEnumerable<Nut<T>> incoming) => _cloudTrunk.ImportChanges(incoming);
        public ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = true,
            TrunkType = "AzureTrunk"
        };

        // Async variants
        public Task StashAsync(string id, Nut<T> shell) => _cloudTrunk.StashAsync(id, shell);
        [Obsolete("Use StashAsync() instead. This method will be removed in a future version.")]
        public Task SaveAsync(string id, Nut<T> shell) => StashAsync(id, shell);

        public Task<Nut<T>?> CrackAsync(string id) => _cloudTrunk.CrackAsync(id);
        [Obsolete("Use CrackAsync() instead. This method will be removed in a future version.")]
        public Task<Nut<T>?> LoadAsync(string id) => CrackAsync(id);

        public Task TossAsync(string id) => _cloudTrunk.TossAsync(id);
        [Obsolete("Use TossAsync() instead. This method will be removed in a future version.")]
        public Task DeleteAsync(string id) => TossAsync(id);

        public Task<IEnumerable<Nut<T>>> CrackAllAsync() => _cloudTrunk.CrackAllAsync();
        [Obsolete("Use CrackAllAsync() instead. This method will be removed in a future version.")]
        public Task<IEnumerable<Nut<T>>> LoadAllAsync() => CrackAllAsync();

        public Task ImportChangesAsync(IEnumerable<Nut<T>> incoming) => _cloudTrunk.ImportChangesAsync(incoming);

        public void Dispose() => _cloudTrunk?.Dispose();

        // IRoot support - delegate to CloudTrunk
        public IReadOnlyList<IRoot> Roots => _cloudTrunk.Roots;
        public void AddRoot(IRoot root) => _cloudTrunk.AddRoot(root);
        public bool RemoveRoot(string name) => _cloudTrunk.RemoveRoot(name);
    }
}
