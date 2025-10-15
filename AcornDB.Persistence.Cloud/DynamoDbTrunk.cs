using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// AWS DynamoDB trunk implementation.
    /// OPTIMIZED with batch operations, write buffering, and intelligent hash/sort keys.
    ///
    /// Hash/Sort Key Strategy:
    /// - Hash Key (Partition Key) = TypeName (e.g., "User", "Product") - enables efficient batch operations
    /// - Sort Key (Range Key) = Nut.Id - unique identifier within partition
    ///
    /// Benefits:
    /// - Batch operations (up to 25 items per request, can batch items from same partition)
    /// - Efficient queries by type (using partition key)
    /// - Point queries using both keys
    /// - Native TTL support (auto-deletion of expired items)
    /// - DynamoDB Streams support for change tracking/sync
    /// - Even distribution across partitions when using multiple types
    /// </summary>
    public class DynamoDbTrunk<T> : ITrunk<T>, ITrunkCapabilities, IDisposable
    {
        private readonly AmazonDynamoDBClient _client;
        private readonly string _tableName;
        private readonly string _partitionKey;
        private bool _disposed;

        // Write batching infrastructure
        private readonly List<PendingWrite> _writeBuffer = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Timer _flushTimer;
        private readonly int _batchSize;

        private struct PendingWrite
        {
            public string Id;
            public Nut<T> Nut;
        }

        /// <summary>
        /// Create DynamoDB trunk
        /// </summary>
        /// <param name="client">DynamoDB client (allows custom configuration for regions, credentials, etc.)</param>
        /// <param name="tableName">DynamoDB table name. Default: Acorns{TypeName}</param>
        /// <param name="createTableIfNotExists">Whether to create the table if it doesn't exist</param>
        /// <param name="batchSize">Write batch size (default: 25, max allowed by DynamoDB)</param>
        public DynamoDbTrunk(
            AmazonDynamoDBClient client,
            string? tableName = null,
            bool createTableIfNotExists = true,
            int batchSize = 25)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            var typeName = typeof(T).Name;
            _partitionKey = typeName; // Each type gets its own partition
            _batchSize = Math.Min(batchSize, 25); // DynamoDB batch limit

            _tableName = tableName ?? $"Acorns{typeName}";

            if (createTableIfNotExists)
            {
                EnsureTableExists().GetAwaiter().GetResult();
            }

            // Auto-flush every 200ms
            _flushTimer = new Timer(_ => FlushAsync().GetAwaiter().GetResult(), null, 200, 200);
        }

        /// <summary>
        /// Create DynamoDB trunk with default client for specified region
        /// </summary>
        /// <param name="region">AWS region (e.g., Amazon.RegionEndpoint.USEast1)</param>
        /// <param name="tableName">DynamoDB table name. Default: Acorns{TypeName}</param>
        /// <param name="createTableIfNotExists">Whether to create the table if it doesn't exist</param>
        /// <param name="batchSize">Write batch size (default: 25)</param>
        public DynamoDbTrunk(
            Amazon.RegionEndpoint region,
            string? tableName = null,
            bool createTableIfNotExists = true,
            int batchSize = 25)
            : this(new AmazonDynamoDBClient(region), tableName, createTableIfNotExists, batchSize)
        {
        }

        private async Task EnsureTableExists()
        {
            try
            {
                await _client.DescribeTableAsync(_tableName);
                // Table exists
            }
            catch (ResourceNotFoundException)
            {
                // Create table with optimal settings
                var request = new CreateTableRequest
                {
                    TableName = _tableName,
                    AttributeDefinitions = new List<AttributeDefinition>
                    {
                        new AttributeDefinition("TypeName", ScalarAttributeType.S), // Hash key
                        new AttributeDefinition("Id", ScalarAttributeType.S)        // Sort key
                    },
                    KeySchema = new List<KeySchemaElement>
                    {
                        new KeySchemaElement("TypeName", KeyType.HASH),  // Partition key
                        new KeySchemaElement("Id", KeyType.RANGE)        // Sort key
                    },
                    BillingMode = BillingMode.PAY_PER_REQUEST, // On-demand pricing (no capacity planning needed)
                    StreamSpecification = new StreamSpecification
                    {
                        StreamEnabled = true,
                        StreamViewType = StreamViewType.NEW_AND_OLD_IMAGES // For sync/change tracking
                    }
                };

                await _client.CreateTableAsync(request);

                // Wait for table to be active
                await WaitForTableActive();

                // Enable TTL on ExpiresAt attribute
                await _client.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
                {
                    TableName = _tableName,
                    TimeToLiveSpecification = new TimeToLiveSpecification
                    {
                        Enabled = true,
                        AttributeName = "ExpiresAt"
                    }
                });
            }
        }

        private async Task WaitForTableActive()
        {
            for (int i = 0; i < 30; i++) // Wait up to 30 seconds
            {
                var response = await _client.DescribeTableAsync(_tableName);
                if (response.Table.TableStatus == TableStatus.ACTIVE)
                    return;
                await Task.Delay(1000);
            }
            throw new TimeoutException($"Table {_tableName} did not become active within 30 seconds");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Save(string id, Nut<T> nut)
        {
            SaveAsync(id, nut).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task SaveAsync(string id, Nut<T> nut)
        {
            bool shouldFlush = false;
            lock (_writeBuffer)
            {
                _writeBuffer.Add(new PendingWrite { Id = id, Nut = nut });
                if (_writeBuffer.Count >= _batchSize)
                {
                    shouldFlush = true;
                }
            }

            if (shouldFlush)
            {
                await FlushAsync();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Load(string id)
        {
            return LoadAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Nut<T>?> LoadAsync(string id)
        {
            var request = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    { "TypeName", new AttributeValue { S = _partitionKey } },
                    { "Id", new AttributeValue { S = id } }
                },
                ConsistentRead = true // Strong consistency for single-item reads
            };

            var response = await _client.GetItemAsync(request);

            if (!response.IsItemSet)
                return null;

            var jsonData = response.Item["JsonData"].S;
            return JsonConvert.DeserializeObject<Nut<T>>(jsonData);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(string id)
        {
            DeleteAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task DeleteAsync(string id)
        {
            await _writeLock.WaitAsync();
            try
            {
                var request = new DeleteItemRequest
                {
                    TableName = _tableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "TypeName", new AttributeValue { S = _partitionKey } },
                        { "Id", new AttributeValue { S = id } }
                    }
                };

                await _client.DeleteItemAsync(request);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerable<Nut<T>> LoadAll()
        {
            return LoadAllAsync().GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<IEnumerable<Nut<T>>> LoadAllAsync()
        {
            var nuts = new List<Nut<T>>();

            // Efficient partition query - only scans our partition using hash key
            var request = new QueryRequest
            {
                TableName = _tableName,
                KeyConditionExpression = "TypeName = :typeName",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":typeName", new AttributeValue { S = _partitionKey } }
                }
            };

            QueryResponse response;
            do
            {
                response = await _client.QueryAsync(request);

                foreach (var item in response.Items)
                {
                    var jsonData = item["JsonData"].S;
                    var nut = JsonConvert.DeserializeObject<Nut<T>>(jsonData);
                    if (nut != null)
                        nuts.Add(nut);
                }

                request.ExclusiveStartKey = response.LastEvaluatedKey;
            }
            while (response.LastEvaluatedKey?.Count > 0);

            return nuts;
        }

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("DynamoDbTrunk does not support history. Use DocumentStoreTrunk for versioning.");
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return LoadAll();
        }

        public void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            ImportChangesAsync(incoming).GetAwaiter().GetResult();
        }

        public async Task ImportChangesAsync(IEnumerable<Nut<T>> incoming)
        {
            var incomingList = incoming.ToList();
            if (!incomingList.Any()) return;

            await _writeLock.WaitAsync();
            try
            {
                // DynamoDB BatchWriteItem supports up to 25 items per request
                var batches = incomingList.Chunk(25);

                foreach (var batch in batches)
                {
                    var writeRequests = batch.Select(nut => new WriteRequest
                    {
                        PutRequest = new PutRequest
                        {
                            Item = CreateItem(nut.Id, nut)
                        }
                    }).ToList();

                    var request = new BatchWriteItemRequest
                    {
                        RequestItems = new Dictionary<string, List<WriteRequest>>
                        {
                            { _tableName, writeRequests }
                        }
                    };

                    // Handle unprocessed items (with exponential backoff)
                    var response = await _client.BatchWriteItemAsync(request);
                    int retryCount = 0;

                    while (response.UnprocessedItems.Count > 0 && retryCount < 5)
                    {
                        await Task.Delay((int)Math.Pow(2, retryCount) * 100); // Exponential backoff
                        response = await _client.BatchWriteItemAsync(new BatchWriteItemRequest
                        {
                            RequestItems = response.UnprocessedItems
                        });
                        retryCount++;
                    }
                }

                Console.WriteLine($"   💾 Imported {incomingList.Count} nuts to DynamoDB");
            }
            finally
            {
                _writeLock.Release();
            }
        }

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
                // Batch operations - up to 25 items per request
                var batches = toWrite.Chunk(25);

                foreach (var batch in batches)
                {
                    var writeRequests = batch.Select(write => new WriteRequest
                    {
                        PutRequest = new PutRequest
                        {
                            Item = CreateItem(write.Id, write.Nut)
                        }
                    }).ToList();

                    var request = new BatchWriteItemRequest
                    {
                        RequestItems = new Dictionary<string, List<WriteRequest>>
                        {
                            { _tableName, writeRequests }
                        }
                    };

                    // Handle unprocessed items with retry
                    var response = await _client.BatchWriteItemAsync(request);
                    int retryCount = 0;

                    while (response.UnprocessedItems.Count > 0 && retryCount < 5)
                    {
                        await Task.Delay((int)Math.Pow(2, retryCount) * 100);
                        response = await _client.BatchWriteItemAsync(new BatchWriteItemRequest
                        {
                            RequestItems = response.UnprocessedItems
                        });
                        retryCount++;
                    }
                }

                Console.WriteLine($"   💾 Flushed {toWrite.Count} nuts to DynamoDB");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private Dictionary<string, AttributeValue> CreateItem(string id, Nut<T> nut)
        {
            var item = new Dictionary<string, AttributeValue>
            {
                { "TypeName", new AttributeValue { S = _partitionKey } },
                { "Id", new AttributeValue { S = id } },
                { "JsonData", new AttributeValue { S = JsonConvert.SerializeObject(nut) } },
                { "Version", new AttributeValue { N = nut.Version.ToString() } },
                { "Timestamp", new AttributeValue { N = new DateTimeOffset(nut.Timestamp).ToUnixTimeSeconds().ToString() } }
            };

            // Add TTL attribute if expiration is set (DynamoDB will auto-delete)
            if (nut.ExpiresAt.HasValue)
            {
                item.Add("ExpiresAt", new AttributeValue
                {
                    N = new DateTimeOffset(nut.ExpiresAt.Value).ToUnixTimeSeconds().ToString()
                });
            }

            return item;
        }

        // ITrunkCapabilities implementation
        public bool SupportsHistory => false;
        public bool SupportsSync => true; // Via DynamoDB Streams
        public bool IsDurable => true;
        public bool SupportsAsync => true;
        public string TrunkType => "DynamoDbTrunk";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Flush pending writes before disposal
            FlushAsync().GetAwaiter().GetResult();

            _flushTimer?.Dispose();
            _writeLock?.Dispose();
            _client?.Dispose();
        }
    }
}
