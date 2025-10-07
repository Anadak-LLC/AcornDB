using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// Azure Blob Storage implementation of cloud storage provider
    /// </summary>
    public class AzureBlobProvider : ICloudStorageProvider, IDisposable
    {
        private readonly BlobContainerClient _containerClient;
        private readonly string _containerName;
        private readonly string? _accountName;
        private bool _disposed;

        /// <summary>
        /// Create Azure Blob provider with connection string
        /// </summary>
        /// <param name="connectionString">Azure Storage connection string</param>
        /// <param name="containerName">Blob container name</param>
        public AzureBlobProvider(string connectionString, string containerName)
        {
            _containerName = containerName;
            _containerClient = new BlobContainerClient(connectionString, containerName);

            // Extract account name from connection string (if possible)
            if (connectionString.Contains("AccountName="))
            {
                var start = connectionString.IndexOf("AccountName=") + "AccountName=".Length;
                var end = connectionString.IndexOf(";", start);
                if (end > start)
                {
                    _accountName = connectionString.Substring(start, end - start);
                }
            }
        }

        /// <summary>
        /// Create Azure Blob provider with SAS URI
        /// </summary>
        /// <param name="sasUri">Shared Access Signature URI with container access</param>
        public AzureBlobProvider(Uri sasUri)
        {
            _containerClient = new BlobContainerClient(sasUri);
            _containerName = _containerClient.Name;
            _accountName = _containerClient.AccountName;
        }

        /// <summary>
        /// Create Azure Blob provider with custom BlobContainerClient (for testing/mocking)
        /// </summary>
        public AzureBlobProvider(BlobContainerClient containerClient)
        {
            _containerClient = containerClient;
            _containerName = containerClient.Name;
            _accountName = containerClient.AccountName;
        }

        /// <summary>
        /// Ensure the container exists (call once at startup)
        /// </summary>
        public async Task EnsureContainerExistsAsync()
        {
            await _containerClient.CreateIfNotExistsAsync();
        }

        public async Task UploadAsync(string key, string content)
        {
            var blobClient = _containerClient.GetBlobClient(key);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            await blobClient.UploadAsync(stream, overwrite: true);
        }

        public async Task<string?> DownloadAsync(string key)
        {
            try
            {
                var blobClient = _containerClient.GetBlobClient(key);

                var response = await blobClient.DownloadContentAsync();
                return response.Value.Content.ToString();
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task DeleteAsync(string key)
        {
            var blobClient = _containerClient.GetBlobClient(key);
            await blobClient.DeleteIfExistsAsync();
        }

        public async Task<bool> ExistsAsync(string key)
        {
            var blobClient = _containerClient.GetBlobClient(key);
            return await blobClient.ExistsAsync();
        }

        public async Task<List<string>> ListAsync(string? prefix = null)
        {
            var keys = new List<string>();

            await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: prefix))
            {
                keys.Add(blobItem.Name);
            }

            return keys;
        }

        public CloudStorageInfo GetInfo()
        {
            return new CloudStorageInfo
            {
                ProviderName = "Azure Blob Storage",
                BucketName = _containerName,
                Region = null, // Azure doesn't expose region directly from client
                Endpoint = _containerClient.Uri.ToString(),
                IsPublic = false
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            // BlobContainerClient doesn't need explicit disposal
            _disposed = true;
        }
    }
}
