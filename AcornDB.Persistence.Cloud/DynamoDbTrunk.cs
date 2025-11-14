using System;
using AcornDB.Logging;
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
using AcornDB.Storage.Serialization;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// AWS DynamoDB trunk implementation.
    /// OPTIMIZED with batch operations, write buffering, and intelligent hash/sort keys.
    /// Extends TrunkBase to support IRoot pipeline (compression, encryption, policy enforcement).
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
    public class DynamoDbTrunk<T> : TrunkBase<T> where T : class
    {
        private readonly AmazonDynamoDBClient _client;
        private readonly string _tableName;
        private readonly string _partitionKey;

        /// <summary>
        /// Create DynamoDB trunk
        /// </summary>
        /// <param name="client">DynamoDB client (allows custom configuration for regions, credentials, etc.)</param>
        /// <param name="tableName">DynamoDB table name. Default: Acorns{TypeName}</param>
        /// <param name="createTableIfNotExists">Whether to create the table if it doesn't exist</param>
        /// <param name="batchSize">Write batch size (default: 25, max allowed by DynamoDB)</param>
        /// <param name="serializer">Custom serializer (defaults to Newtonsoft.Json)</param>
        public DynamoDbTrunk(
            AmazonDynamoDBClient client,
            string? tableName = null,
            bool createTableIfNotExists = true,
            int batchSize = 25,
            ISerializer? serializer = null)
            : base(serializer, enableBatching: true, batchThreshold: Math.Min(batchSize, 25), flushIntervalMs: 200)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            var typeName = typeof(T).Name;
            _partitionKey = typeName; // Each type gets its own partition

            _tableName = tableName ?? $"Acorns{typeName}";

            if (createTableIfNotExists)
            {
                EnsureTableExists().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Create DynamoDB trunk with default client for specified region
        /// </summary>
        /// <param name="region">AWS region (e.g., Amazon.RegionEndpoint.USEast1)</param>
        /// <param name="tableName">DynamoDB table name. Default: Acorns{TypeName}</param>
        /// <param name="createTableIfNotExists">Whether to create the table if it doesn't exist</param>
        /// <param name="batchSize">Write batch size (default: 25)</param>
        /// <param name="serializer">Custom serializer (defaults to Newtonsoft.Json)</param>
        public DynamoDbTrunk(
            Amazon.RegionEndpoint region,
            string? tableName = null,
            bool createTableIfNotExists = true,
            int batchSize = 25,
            ISerializer? serializer = null)
            : this(new AmazonDynamoDBClient(region), tableName, createTableIfNotExists, batchSize, serializer)
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
        public override void Stash(string id, Nut<T> nut)
        {
            // Use TrunkBase batching infrastructure
            StashWithBatchingAsync(id, nut).GetAwaiter().GetResult();
        }

        [Obsolete("Use Stash instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Save(string id, Nut<T> nut)
        {
            Stash(id, nut);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Nut<T>? Crack(string id)
        {
            return CrackAsync(id).GetAwaiter().GetResult();
        }

        [Obsolete("Use Crack instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Load(string id)
        {
            return Crack(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Nut<T>?> CrackAsync(string id)
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
        public override void Toss(string id)
        {
            TossAsync(id).GetAwaiter().GetResult();
        }

        [Obsolete("Use Toss instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(string id)
        {
            Toss(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task TossAsync(string id)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override IEnumerable<Nut<T>> CrackAll()
        {
            return CrackAllAsync().GetAwaiter().GetResult();
        }

        [Obsolete("Use CrackAll instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerable<Nut<T>> LoadAll()
        {
            return CrackAll();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<IEnumerable<Nut<T>>> CrackAllAsync()
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

        public override IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("DynamoDbTrunk does not support history. Use DocumentStoreTrunk for versioning.");
        }

        public override IEnumerable<Nut<T>> ExportChanges()
        {
            return CrackAll();
        }

        public override void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            ImportChangesAsync(incoming).GetAwaiter().GetResult();
        }

        public override ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = true,
            TrunkType = "DynamoDbTrunk"
        };

        public async Task ImportChangesAsync(IEnumerable<Nut<T>> incoming)
        {
            var incomingList = incoming.ToList();
            if (!incomingList.Any()) return;

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

            AcornLog.Info($"   💾 Imported {incomingList.Count} nuts to DynamoDB");
        }

        /// <summary>
        /// Write batch to DynamoDB storage (called by TrunkBase batching infrastructure)
        /// </summary>
        protected override async Task WriteBatchToStorageAsync(List<PendingWrite> batch)
        {
            // Batch operations - up to 25 items per request
            var batches = batch.Chunk(25);

            foreach (var batchChunk in batches)
            {
                var writeRequests = batchChunk.Select(write => new WriteRequest
                {
                    PutRequest = new PutRequest
                    {
                        Item = CreateItemFromProcessedData(write.Id, write.ProcessedData, write.Timestamp, write.Version)
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

            AcornLog.Info($"   💾 Flushed {batch.Count} nuts to DynamoDB");
        }

        /// <summary>
        /// Create DynamoDB item from IRoot-processed data (used by TrunkBase batching)
        /// </summary>
        private Dictionary<string, AttributeValue> CreateItemFromProcessedData(string id, byte[] processedData, DateTime timestamp, int version)
        {
            // ProcessedData is already through IRoot pipeline (compressed, encrypted, etc.)
            var item = new Dictionary<string, AttributeValue>
            {
                { "TypeName", new AttributeValue { S = _partitionKey } },
                { "Id", new AttributeValue { S = id } },
                { "JsonData", new AttributeValue { S = System.Text.Encoding.UTF8.GetString(processedData) } },
                { "Version", new AttributeValue { N = version.ToString() } },
                { "Timestamp", new AttributeValue { N = new DateTimeOffset(timestamp).ToUnixTimeSeconds().ToString() } }
            };

            return item;
        }

        /// <summary>
        /// Create DynamoDB item from Nut (used by non-batched operations like ImportChanges)
        /// </summary>
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

        public new void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Flush pending writes before disposal (handled by base class)
            base.Dispose();

            _client?.Dispose();
        }
    }
}
