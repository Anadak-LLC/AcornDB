using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Persistence.Cloud
{
    /// <summary>
    /// Azure Table Storage trunk implementation.
    /// OPTIMIZED with batch operations, write buffering, and intelligent partition/row keys.
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
    public class AzureTableTrunk<T> : ITrunk<T>, ITrunkCapabilities, IDisposable
    {
        private readonly TableClient _tableClient;
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
        /// Create Azure Table Storage trunk
        /// </summary>
        /// <param name="connectionString">Azure Storage connection string</param>
        /// <param name="tableName">Optional table name. Default: Acorns{TypeName}</param>
        /// <param name="batchSize">Write batch size (default: 100, max allowed by Azure)</param>
        public AzureTableTrunk(string connectionString, string? tableName = null, int batchSize = 100)
        {
            var typeName = typeof(T).Name;
            _partitionKey = typeName; // Each type gets its own partition for efficient batch ops
            _batchSize = Math.Min(batchSize, 100); // Azure Table limit

            tableName ??= $"Acorns{typeName}";

            var tableServiceClient = new TableServiceClient(connectionString);
            _tableClient = tableServiceClient.GetTableClient(tableName);

            // Create table if it doesn't exist
            _tableClient.CreateIfNotExists();

            // Auto-flush every 200ms
            _flushTimer = new Timer(_ => FlushAsync().GetAwaiter().GetResult(), null, 200, 200);
        }

        /// <summary>
        /// Create Azure Table Storage trunk with SAS URI
        /// </summary>
        /// <param name="tableUri">Table SAS URI</param>
        /// <param name="batchSize">Write batch size (default: 100)</param>
        public AzureTableTrunk(Uri tableUri, int batchSize = 100)
        {
            var typeName = typeof(T).Name;
            _partitionKey = typeName;
            _batchSize = Math.Min(batchSize, 100);

            _tableClient = new TableClient(tableUri);
            _tableClient.CreateIfNotExists();

            _flushTimer = new Timer(_ => FlushAsync().GetAwaiter().GetResult(), null, 200, 200);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Stash(string id, Nut<T> nut)
        {
            StashAsync(id, nut).GetAwaiter().GetResult();
        }

        [Obsolete("Use Stash instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Save(string id, Nut<T> nut)
        {
            Stash(id, nut);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task StashAsync(string id, Nut<T> nut)
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
        public Nut<T>? Crack(string id)
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
            try
            {
                // Point query using both partition and row key for maximum efficiency
                var response = await _tableClient.GetEntityAsync<TableEntity>(_partitionKey, id);
                var entity = response.Value;

                var jsonData = entity.GetString("JsonData");
                return JsonConvert.DeserializeObject<Nut<T>>(jsonData);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Toss(string id)
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
            await _writeLock.WaitAsync();
            try
            {
                await _tableClient.DeleteEntityAsync(_partitionKey, id);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Already deleted, ignore
            }
            finally
            {
                _writeLock.Release();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerable<Nut<T>> CrackAll()
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

            // Efficient partition query - only scans our partition
            var filter = $"PartitionKey eq '{_partitionKey}'";
            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter))
            {
                var jsonData = entity.GetString("JsonData");
                var nut = JsonConvert.DeserializeObject<Nut<T>>(jsonData);
                if (nut != null)
                    nuts.Add(nut);
            }

            return nuts;
        }

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("AzureTableTrunk does not support history. Use DocumentStoreTrunk for versioning.");
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return CrackAll();
        }

        public void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            ImportChangesAsync(incoming).GetAwaiter().GetResult();
        }

        public ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
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

            await _writeLock.WaitAsync();
            try
            {
                // Azure Table Storage supports batch operations up to 100 entities in same partition
                // Since all our entities are in the same partition (TypeName), we can batch them all!
                var batches = incomingList.Chunk(100);

                foreach (var batch in batches)
                {
                    var batchOperation = new List<TableTransactionAction>();

                    foreach (var nut in batch)
                    {
                        var entity = CreateEntity(nut.Id, nut);
                        batchOperation.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, entity));
                    }

                    // Execute batch transaction (all succeed or all fail)
                    await _tableClient.SubmitTransactionAsync(batchOperation);
                }

                Console.WriteLine($"   💾 Imported {incomingList.Count} nuts to Azure Table Storage");
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
                // Batch operations - up to 100 entities per transaction in same partition
                var batches = toWrite.Chunk(100);

                foreach (var batch in batches)
                {
                    var batchOperation = new List<TableTransactionAction>();

                    foreach (var write in batch)
                    {
                        var entity = CreateEntity(write.Id, write.Nut);
                        batchOperation.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, entity));
                    }

                    // Execute batch transaction
                    await _tableClient.SubmitTransactionAsync(batchOperation);
                }

                Console.WriteLine($"   💾 Flushed {toWrite.Count} nuts to Azure Table Storage");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private TableEntity CreateEntity(string id, Nut<T> nut)
        {
            var entity = new TableEntity(_partitionKey, id)
            {
                { "JsonData", JsonConvert.SerializeObject(nut) },
                { "Version", nut.Version }
            };

            // Add optional expiration
            if (nut.ExpiresAt.HasValue)
            {
                entity.Add("ExpiresAt", nut.ExpiresAt.Value);
            }

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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Flush pending writes before disposal
            FlushAsync().GetAwaiter().GetResult();

            _flushTimer?.Dispose();
            _writeLock?.Dispose();
        }

        // IRoot support - stub implementation (to be fully implemented later)
        public IReadOnlyList<IRoot> Roots => Array.Empty<IRoot>();
        public void AddRoot(IRoot root) { /* TODO: Implement root support */ }
        public bool RemoveRoot(string name) => false;
    }
}
