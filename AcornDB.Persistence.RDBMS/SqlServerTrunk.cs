using System;
using AcornDB.Logging;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Policy;
using AcornDB.Storage;
using AcornDB.Storage.Serialization;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// SQL Server-backed trunk implementation with full IRoot pipeline support.
    /// Maps Tree&lt;T&gt; to a SQL Server table with JSON support.
    /// OPTIMIZED with write batching, async support, and connection pooling.
    ///
    /// Storage Pipeline:
    /// Write: Nut&lt;T&gt; → Serialize → Root Chain (ascending) → byte[] → Base64 → Store in JsonData
    /// Read: Read JsonData → Base64 decode → byte[] → Root Chain (descending) → Deserialize → Nut&lt;T&gt;
    ///
    /// Supports compression, encryption, and policy enforcement via IRoot processors.
    /// Backward compatible: Reads plain JSON data from before IRoot adoption.
    /// </summary>
    public class SqlServerTrunk<T> : TrunkBase<T> where T : class
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly string _schema;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        private const int BATCH_SIZE = 100;
        private const int FLUSH_INTERVAL_MS = 200;

        /// <summary>
        /// Create SQL Server trunk
        /// </summary>
        /// <param name="connectionString">SQL Server connection string</param>
        /// <param name="tableName">Optional custom table name. Default: Acorn_{TypeName}</param>
        /// <param name="schema">Database schema. Default: dbo</param>
        /// <param name="batchSize">Write batch size (default: 100)</param>
        /// <param name="serializer">Optional custom serializer. Default: NewtonsoftJsonSerializer</param>
        public SqlServerTrunk(string connectionString, string? tableName = null, string schema = "dbo", int batchSize = 100, ISerializer? serializer = null)
            : base(
                serializer,
                enableBatching: true,
                batchThreshold: batchSize,
                flushIntervalMs: FLUSH_INTERVAL_MS)
        {
            _schema = schema;
            _tableName = tableName ?? $"Acorn_{typeof(T).Name}";

            // Enable connection pooling in connection string
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                Pooling = true,
                MinPoolSize = 2,
                MaxPoolSize = 100
            };
            _connectionString = builder.ConnectionString;

            EnsureTable();
        }

        private void EnsureTable()
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            // Check if table exists
            var checkTableSql = $@"
                IF NOT EXISTS (SELECT * FROM sys.objects
                               WHERE object_id = OBJECT_ID(N'[{_schema}].[{_tableName}]')
                               AND type in (N'U'))
                BEGIN
                    CREATE TABLE [{_schema}].[{_tableName}] (
                        Id NVARCHAR(450) PRIMARY KEY NOT NULL,
                        JsonData NVARCHAR(MAX) NOT NULL,
                        Timestamp DATETIME2 NOT NULL,
                        Version INT NOT NULL,
                        ExpiresAt DATETIME2 NULL
                    );

                    CREATE NONCLUSTERED INDEX IX_{_tableName}_Timestamp
                    ON [{_schema}].[{_tableName}] (Timestamp DESC);
                END";

            using var cmd = new SqlCommand(checkTableSql, conn);
            cmd.ExecuteNonQuery();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Stash(string id, Nut<T> nut)
        {
            StashWithBatchingAsync(id, nut).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task StashAsync(string id, Nut<T> nut)
        {
            await StashWithBatchingAsync(id, nut);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Nut<T>? Crack(string id)
        {
            return CrackAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Nut<T>?> CrackAsync(string id)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"SELECT JsonData FROM [{_schema}].[{_tableName}] WHERE Id = @Id";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var data = reader.GetString(0);
                var storedBytes = DecodeStoredData(data);
                var processedBytes = ProcessThroughRootsDescending(storedBytes, id);

                // Deserialize from bytes to Nut<T>
                var json = Encoding.UTF8.GetString(processedBytes);
                return _serializer.Deserialize<Nut<T>>(json);
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Toss(string id)
        {
            TossAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task TossAsync(string id)
        {
            await _connectionLock.WaitAsync();
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = $"DELETE FROM [{_schema}].[{_tableName}] WHERE Id = @Id";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);

                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _connectionLock.Release();
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
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"SELECT JsonData FROM [{_schema}].[{_tableName}] ORDER BY Timestamp DESC";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var nuts = new List<Nut<T>>();
            while (await reader.ReadAsync())
            {
                var data = reader.GetString(0);
                var storedBytes = DecodeStoredData(data);
                var processedBytes = ProcessThroughRootsDescending(storedBytes, null);

                var json = Encoding.UTF8.GetString(processedBytes);
                var nut = _serializer.Deserialize<Nut<T>>(json);
                if (nut != null)
                    nuts.Add(nut);
            }

            return nuts;
        }

        public override IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("SqlServerTrunk does not support history. Use DocumentStoreTrunk for versioning.");
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
            SupportsHistory = true,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = true,
            TrunkType = "SqlServerTrunk"
        };

        public async Task ImportChangesAsync(IEnumerable<Nut<T>> incoming)
        {
            var incomingList = incoming.ToList();
            if (!incomingList.Any()) return;

            // Stash all nuts (batching handled by TrunkBase)
            foreach (var nut in incomingList)
            {
                await StashAsync(nut.Id, nut);
            }

            // Force flush
            await FlushBatchAsync();

            AcornLog.Info($"   💾 Imported {incomingList.Count} nuts to SQL Server");
        }

        protected override async Task WriteToStorageAsync(string id, byte[] processedBytes, DateTime timestamp, int version)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await WriteToSqlServer(conn, null, id, processedBytes, timestamp, version);
        }

        protected override async Task WriteBatchToStorageAsync(List<PendingWrite> batch)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Use transaction for batch insert (massive speedup!)
            using var transaction = conn.BeginTransaction();

            try
            {
                foreach (var write in batch)
                {
                    await WriteToSqlServer(conn, transaction, write.Id, write.ProcessedData, write.Timestamp, write.Version);
                }

                await transaction.CommitAsync();
                AcornLog.Info($"   💾 Flushed {batch.Count} nuts to SQL Server");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task WriteToSqlServer(SqlConnection conn, SqlTransaction? transaction, string id, byte[] processedBytes, DateTime timestamp, int version)
        {
            var dataToStore = EncodeForStorage(processedBytes, "");

            // Get expires_at from the nut if needed
            var json = Encoding.UTF8.GetString(processedBytes);
            var nut = _serializer.Deserialize<Nut<T>>(json);
            var expiresAt = nut?.ExpiresAt;

            var sql = $@"
                MERGE [{_schema}].[{_tableName}] AS target
                USING (SELECT @Id AS Id) AS source
                ON target.Id = source.Id
                WHEN MATCHED THEN
                    UPDATE SET
                        JsonData = @JsonData,
                        Timestamp = @Timestamp,
                        Version = @Version,
                        ExpiresAt = @ExpiresAt
                WHEN NOT MATCHED THEN
                    INSERT (Id, JsonData, Timestamp, Version, ExpiresAt)
                    VALUES (@Id, @JsonData, @Timestamp, @Version, @ExpiresAt);";

            using var cmd = transaction != null
                ? new SqlCommand(sql, conn, transaction)
                : new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@JsonData", dataToStore);
            cmd.Parameters.AddWithValue("@Timestamp", timestamp);
            cmd.Parameters.AddWithValue("@Version", version);
            cmd.Parameters.AddWithValue("@ExpiresAt", expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Execute custom SQL query
        /// </summary>
        public IEnumerable<Nut<T>> Query(string whereClause)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            var sql = $"SELECT JsonData FROM [{_schema}].[{_tableName}] WHERE {whereClause} ORDER BY Timestamp DESC";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            var nuts = new List<Nut<T>>();
            while (reader.Read())
            {
                var json = reader.GetString(0);
                var nut = JsonConvert.DeserializeObject<Nut<T>>(json);
                if (nut != null)
                    nuts.Add(nut);
            }

            return nuts;
        }

        // ITrunkCapabilities implementation
        public bool SupportsHistory => false;
        public bool SupportsSync => true;
        public bool IsDurable => true;
        public bool SupportsAsync => true;
        public string TrunkType => "SqlServerTrunk";

        public override void Dispose()
        {
            if (_disposed) return;

            // Base class handles timer disposal and flush
            base.Dispose();
        }
    }
}
