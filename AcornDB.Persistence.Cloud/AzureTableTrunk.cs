using System;
using AcornDB.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Storage.Serialization;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// Azure Table Storage trunk implementation.
    /// OPTIMIZED with batch operations, write buffering, and intelligent partition/row keys.
    /// Extends TrunkBase to support IRoot pipeline (compression, encryption, policy enforcement).
    ///
    /// Partition/Row Key Strategy:
    /// - PartitionKey = TypeName (e.g., "User", "Product") - enables efficient batch operations
    /// - RowKey = Nut.Id - unique identifier within partition
    ///
    /// Benefits:
    /// - Batch operations (up to 100 entities per partition)
    /// - Efficient queries by type
    /// - Point queries for single entity lookups
    /// - Natural multi-tenancy (different types in different partitions)
    /// </summary>
    public class AzureTableTrunk<T> : TrunkBase<T> where T : class
    {
        private readonly TableClient _tableClient;
        private readonly string _partitionKey;

        /// <summary>
        /// Create Azure Table Storage trunk
        /// </summary>
        /// <param name="connectionString">Azure Storage connection string</param>
        /// <param name="tableName">Optional table name. Default: Acorns{TypeName}</param>
        /// <param name="batchSize">Write batch size (default: 100, max allowed by Azure)</param>
        /// <param name="serializer">Custom serializer (defaults to Newtonsoft.Json)</param>
        public AzureTableTrunk(string connectionString, string? tableName = null, int batchSize = 100, ISerializer? serializer = null)
            : base(serializer, enableBatching: true, batchThreshold: Math.Min(batchSize, 100), flushIntervalMs: 200)
        {
            var typeName = typeof(T).Name;
            _partitionKey = typeName; // Each type gets its own partition for efficient batch ops

            tableName ??= $"Acorns{typeName}";

            var tableServiceClient = new TableServiceClient(connectionString);
            _tableClient = tableServiceClient.GetTableClient(tableName);

            // Create table if it doesn't exist
            _tableClient.CreateIfNotExists();
        }

        /// <summary>
        /// Create Azure Table Storage trunk with SAS URI
        /// </summary>
        /// <param name="tableUri">Table SAS URI</param>
        /// <param name="batchSize">Write batch size (default: 100)</param>
        /// <param name="serializer">Custom serializer (defaults to Newtonsoft.Json)</param>
        public AzureTableTrunk(Uri tableUri, int batchSize = 100, ISerializer? serializer = null)
            : base(serializer, enableBatching: true, batchThreshold: Math.Min(batchSize, 100), flushIntervalMs: 200)
        {
            var typeName = typeof(T).Name;
            _partitionKey = typeName;

            _tableClient = new TableClient(tableUri);
            _tableClient.CreateIfNotExists();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Stash(string id, Nut<T> nut)
        {
            // Use TrunkBase batching infrastructure
            StashWithBatchingAsync(id, nut).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Nut<T>? Crack(string id)
        {
            return CrackAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Nut<T>?> CrackAsync(string id)
        {
            try
            {
                // Point query using both partition and row key for maximum efficiency
                var response = await _tableClient.GetEntityAsync<TableEntity>(_partitionKey, id);
                var entity = response.Value;

                var storedData = entity.GetString("JsonData");
                var bytes = DecodeStoredData(storedData);
                var processedBytes = ProcessThroughRootsDescending(bytes, id);
                var json = Encoding.UTF8.GetString(processedBytes);
                return _serializer.Deserialize<Nut<T>>(json);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Toss(string id)
        {
            TossAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task TossAsync(string id)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(_partitionKey, id);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Already deleted, ignore
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override IEnumerable<Nut<T>> CrackAll()
        {
            return CrackAllAsync().GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<IEnumerable<Nut<T>>> CrackAllAsync()
        {
            var nuts = new List<Nut<T>>();

            // Efficient partition query - only scans our partition
            var filter = $"PartitionKey eq '{_partitionKey}'";
            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter))
            {
                var storedData = entity.GetString("JsonData");
                var id = entity.GetString("RowKey");
                var bytes = DecodeStoredData(storedData);
                var processedBytes = ProcessThroughRootsDescending(bytes, id);
                var json = Encoding.UTF8.GetString(processedBytes);
                var nut = _serializer.Deserialize<Nut<T>>(json);
                if (nut != null)
                    nuts.Add(nut);
            }

            return nuts;
        }

        public override IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("AzureTableTrunk does not support history. Use DocumentStoreTrunk for versioning.");
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
            SupportsNativeIndexes = false,
            SupportsFullTextSearch = false,
            SupportsComputedIndexes = false,
            TrunkType = "AzureTableTrunk"
        };

        public async Task ImportChangesAsync(IEnumerable<Nut<T>> incoming)
        {
            var incomingList = incoming.ToList();
            if (!incomingList.Any()) return;

            // Convert nuts to pending writes (process through IRoot pipeline)
            var pendingWrites = new List<PendingWrite>();
            foreach (var nut in incomingList)
            {
                var json = _serializer.Serialize(nut);
                var bytes = Encoding.UTF8.GetBytes(json);
                var processedBytes = ProcessThroughRootsAscending(bytes, nut.Id);

                pendingWrites.Add(new PendingWrite
                {
                    Id = nut.Id,
                    ProcessedData = processedBytes,
                    Timestamp = nut.Timestamp,
                    Version = nut.Version
                });
            }

            // Write using TrunkBase batch infrastructure
            await WriteBatchToStorageAsync(pendingWrites);

            AcornLog.Info($"   💾 Imported {incomingList.Count} nuts to Azure Table Storage");
        }

        protected override async Task WriteBatchToStorageAsync(List<PendingWrite> batch)
        {
            // Azure Table Storage supports batch operations up to 100 entities in same partition
            // Since all our entities are in the same partition (TypeName), we can batch them all!
            var batches = batch.Chunk(100);

            foreach (var batchChunk in batches)
            {
                var batchOperation = new List<TableTransactionAction>();

                foreach (var write in batchChunk)
                {
                    var entity = CreateEntity(write.Id, write.ProcessedData, write.Version, write.Timestamp);
                    batchOperation.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, entity));
                }

                // Execute batch transaction (all succeed or all fail)
                await _tableClient.SubmitTransactionAsync(batchOperation);
            }
        }

        private TableEntity CreateEntity(string id, byte[] processedData, int version, DateTime timestamp)
        {
            var json = _serializer.Serialize(new { Version = version, Timestamp = timestamp });
            var dataToStore = EncodeForStorage(processedData, json);

            var entity = new TableEntity(_partitionKey, id)
            {
                { "JsonData", dataToStore },
                { "Version", version }
            };

            return entity;
        }

        // ITrunkCapabilities implementation
        public bool SupportsHistory => false;
        public bool SupportsSync => true;
        public bool IsDurable => true;
        public bool SupportsAsync => true;
        public bool SupportsNativeIndexes => false;
        public bool SupportsFullTextSearch => false;
        public bool SupportsComputedIndexes => false;
        public string TrunkType => "AzureTableTrunk";

        public override void Dispose()
        {
            if (_disposed) return;

            // Call base class disposal (flushes pending writes)
            base.Dispose();
        }
    }
}
