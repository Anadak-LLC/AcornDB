using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// Abstraction for cloud storage operations (S3, Azure Blob, etc.)
    /// Allows testability and swapping between cloud providers
    /// </summary>
    public interface ICloudStorageProvider
    {
        /// <summary>
        /// Upload object to cloud storage
        /// </summary>
        /// <param name="key">Object key/path</param>
        /// <param name="content">Content to upload</param>
        Task UploadAsync(string key, string content);

        /// <summary>
        /// Download object from cloud storage
        /// </summary>
        /// <param name="key">Object key/path</param>
        /// <returns>Content or null if not found</returns>
        Task<string?> DownloadAsync(string key);

        /// <summary>
        /// Delete object from cloud storage
        /// </summary>
        /// <param name="key">Object key/path</param>
        Task DeleteAsync(string key);

        /// <summary>
        /// Check if object exists in cloud storage
        /// </summary>
        /// <param name="key">Object key/path</param>
        Task<bool> ExistsAsync(string key);

        /// <summary>
        /// List all objects in cloud storage (with optional prefix)
        /// </summary>
        /// <param name="prefix">Optional prefix to filter keys</param>
        /// <returns>List of object keys</returns>
        Task<List<string>> ListAsync(string? prefix = null);

        /// <summary>
        /// Get metadata about the cloud storage
        /// </summary>
        CloudStorageInfo GetInfo();
    }

    /// <summary>
    /// Cloud storage provider metadata
    /// </summary>
    public class CloudStorageInfo
    {
        /// <summary>
        /// Provider name (e.g., "AWS S3", "Azure Blob Storage")
        /// </summary>
        public string ProviderName { get; set; } = "";

        /// <summary>
        /// Bucket/container name
        /// </summary>
        public string BucketName { get; set; } = "";

        /// <summary>
        /// Region (if applicable)
        /// </summary>
        public string? Region { get; set; }

        /// <summary>
        /// Endpoint URL (if custom)
        /// </summary>
        public string? Endpoint { get; set; }

        /// <summary>
        /// Whether this is a public or private storage
        /// </summary>
        public bool IsPublic { get; set; }
    }
}
