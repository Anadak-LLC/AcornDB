using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// AWS S3 implementation of cloud storage provider.
    /// Supports AWS S3, MinIO, DigitalOcean Spaces, and any S3-compatible storage.
    /// </summary>
    public class AwsS3Provider : ICloudStorageProvider, IDisposable
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private readonly string _region;
        private readonly string? _endpoint;
        private bool _disposed;

        /// <summary>
        /// Create AWS S3 provider with explicit credentials
        /// </summary>
        /// <param name="accessKey">AWS Access Key ID</param>
        /// <param name="secretKey">AWS Secret Access Key</param>
        /// <param name="bucketName">S3 bucket name</param>
        /// <param name="region">AWS region (e.g., "us-east-1")</param>
        public AwsS3Provider(string accessKey, string secretKey, string bucketName, string region = "us-east-1")
        {
            _bucketName = bucketName;
            _region = region;

            var config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(region)
            };

            _s3Client = new AmazonS3Client(accessKey, secretKey, config);
        }

        /// <summary>
        /// Create AWS S3 provider with default credential chain (IAM roles, environment vars, etc.)
        /// </summary>
        /// <param name="bucketName">S3 bucket name</param>
        /// <param name="region">AWS region (e.g., "us-east-1")</param>
        public AwsS3Provider(string bucketName, string region = "us-east-1")
        {
            _bucketName = bucketName;
            _region = region;

            var config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(region)
            };

            _s3Client = new AmazonS3Client(config);
        }

        /// <summary>
        /// Create S3-compatible provider (MinIO, DigitalOcean Spaces, etc.)
        /// </summary>
        /// <param name="accessKey">Access key</param>
        /// <param name="secretKey">Secret key</param>
        /// <param name="bucketName">Bucket name</param>
        /// <param name="serviceUrl">Service endpoint URL (e.g., "https://s3.us-west-2.amazonaws.com")</param>
        public AwsS3Provider(string accessKey, string secretKey, string bucketName, Uri serviceUrl)
        {
            _bucketName = bucketName;
            _endpoint = serviceUrl.ToString();
            _region = "us-east-1"; // Default region for S3-compatible services

            var config = new AmazonS3Config
            {
                ServiceURL = serviceUrl.ToString(),
                ForcePathStyle = true // Required for MinIO and some S3-compatible services
            };

            _s3Client = new AmazonS3Client(accessKey, secretKey, config);
        }

        /// <summary>
        /// Create S3 provider with custom IAmazonS3 client (for testing/mocking)
        /// </summary>
        public AwsS3Provider(IAmazonS3 s3Client, string bucketName, string region = "us-east-1")
        {
            _s3Client = s3Client;
            _bucketName = bucketName;
            _region = region;
        }

        public async Task UploadAsync(string key, string content)
        {
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                ContentBody = content,
                ContentType = "application/json"
            };

            await _s3Client.PutObjectAsync(request);
        }

        public async Task<string?> DownloadAsync(string key)
        {
            try
            {
                var request = new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key
                };

                using var response = await _s3Client.GetObjectAsync(request);
                using var reader = new StreamReader(response.ResponseStream);
                return await reader.ReadToEndAsync();
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task DeleteAsync(string key)
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            await _s3Client.DeleteObjectAsync(request);
        }

        public async Task<bool> ExistsAsync(string key)
        {
            try
            {
                var request = new GetObjectMetadataRequest
                {
                    BucketName = _bucketName,
                    Key = key
                };

                await _s3Client.GetObjectMetadataAsync(request);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public async Task<List<string>> ListAsync(string? prefix = null)
        {
            var keys = new List<string>();
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = prefix
            };

            ListObjectsV2Response response;
            do
            {
                response = await _s3Client.ListObjectsV2Async(request);
                keys.AddRange(response.S3Objects.Select(obj => obj.Key));
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated == true);

            return keys;
        }

        public CloudStorageInfo GetInfo()
        {
            return new CloudStorageInfo
            {
                ProviderName = "AWS S3",
                BucketName = _bucketName,
                Region = _region,
                Endpoint = _endpoint,
                IsPublic = false
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _s3Client?.Dispose();
            _disposed = true;
        }
    }
}
