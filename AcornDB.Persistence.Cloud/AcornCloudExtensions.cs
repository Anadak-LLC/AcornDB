using System;
using AcornDB;
using AcornDB.Models;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// Extension methods for Acorn builder to support cloud storage
    /// </summary>
    public static class AcornCloudExtensions
    {
        /// <summary>
        /// Use AWS S3 storage with explicit credentials
        /// </summary>
        /// <param name="accessKey">AWS Access Key ID</param>
        /// <param name="secretKey">AWS Secret Access Key</param>
        /// <param name="bucketName">S3 bucket name</param>
        /// <param name="region">AWS region (e.g., "us-east-1")</param>
        /// <param name="prefix">Optional prefix for all keys (like a folder path)</param>
        public static Acorn<T> WithS3Storage<T>(
            this Acorn<T> acorn,
            string accessKey,
            string secretKey,
            string bucketName,
            string region = "us-east-1",
            string? prefix = null) where T : class
        {
            var s3Provider = new AwsS3Provider(accessKey, secretKey, bucketName, region);
            var cloudTrunk = new CloudTrunk<T>(s3Provider, prefix);
            return acorn.WithTrunk(cloudTrunk);
        }

        /// <summary>
        /// Use AWS S3 storage with default credential chain (IAM roles, env vars, etc.)
        /// </summary>
        /// <param name="bucketName">S3 bucket name</param>
        /// <param name="region">AWS region (e.g., "us-east-1")</param>
        /// <param name="prefix">Optional prefix for all keys (like a folder path)</param>
        public static Acorn<T> WithS3Storage<T>(
            this Acorn<T> acorn,
            string bucketName,
            string region = "us-east-1",
            string? prefix = null) where T : class
        {
            var s3Provider = new AwsS3Provider(bucketName, region);
            var cloudTrunk = new CloudTrunk<T>(s3Provider, prefix);
            return acorn.WithTrunk(cloudTrunk);
        }

        /// <summary>
        /// Use S3-compatible storage (MinIO, DigitalOcean Spaces, etc.)
        /// </summary>
        /// <param name="accessKey">Access key</param>
        /// <param name="secretKey">Secret key</param>
        /// <param name="bucketName">Bucket name</param>
        /// <param name="serviceUrl">Service endpoint URL (e.g., "https://nyc3.digitaloceanspaces.com")</param>
        /// <param name="prefix">Optional prefix for all keys (like a folder path)</param>
        public static Acorn<T> WithS3CompatibleStorage<T>(
            this Acorn<T> acorn,
            string accessKey,
            string secretKey,
            string bucketName,
            string serviceUrl,
            string? prefix = null) where T : class
        {
            var s3Provider = new AwsS3Provider(accessKey, secretKey, bucketName, new Uri(serviceUrl));
            var cloudTrunk = new CloudTrunk<T>(s3Provider, prefix);
            return acorn.WithTrunk(cloudTrunk);
        }

        /// <summary>
        /// Use Azure Blob Storage with connection string
        /// </summary>
        /// <param name="connectionString">Azure Storage connection string</param>
        /// <param name="containerName">Blob container name</param>
        /// <param name="prefix">Optional prefix for all keys (like a folder path)</param>
        public static Acorn<T> WithAzureBlobStorage<T>(
            this Acorn<T> acorn,
            string connectionString,
            string containerName,
            string? prefix = null) where T : class
        {
            var azureProvider = new AzureBlobProvider(connectionString, containerName);

            // Ensure container exists (do this synchronously on setup)
            azureProvider.EnsureContainerExistsAsync().GetAwaiter().GetResult();

            var cloudTrunk = new CloudTrunk<T>(azureProvider, prefix);
            return acorn.WithTrunk(cloudTrunk);
        }

        /// <summary>
        /// Use Azure Blob Storage with SAS URI
        /// </summary>
        /// <param name="sasUri">Shared Access Signature URI with container access</param>
        /// <param name="prefix">Optional prefix for all keys (like a folder path)</param>
        public static Acorn<T> WithAzureBlobStorage<T>(
            this Acorn<T> acorn,
            Uri sasUri,
            string? prefix = null) where T : class
        {
            var azureProvider = new AzureBlobProvider(sasUri);
            var cloudTrunk = new CloudTrunk<T>(azureProvider, prefix);
            return acorn.WithTrunk(cloudTrunk);
        }

        /// <summary>
        /// Use custom cloud storage provider
        /// </summary>
        public static Acorn<T> WithCloudStorage<T>(
            this Acorn<T> acorn,
            ICloudStorageProvider cloudProvider,
            string? prefix = null) where T : class
        {
            var cloudTrunk = new CloudTrunk<T>(cloudProvider, prefix);
            return acorn.WithTrunk(cloudTrunk);
        }
    }
}
