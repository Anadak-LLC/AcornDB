using System;
using AcornDB.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Policy;
using AcornDB.Storage;
using AcornDB.Storage.Serialization;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// PostgreSQL-backed trunk implementation with full IRoot pipeline support.
    /// Maps Tree&lt;T&gt; to a PostgreSQL table with native JSON support.
    /// OPTIMIZED with write batching, async support, and connection pooling.
    ///
    /// Storage Pipeline:
    /// Write: Nut&lt;T&gt; → Serialize → Root Chain (ascending) → byte[] → Base64 → Store in json_data
    /// Read: Read json_data → Base64 decode → byte[] → Root Chain (descending) → Deserialize → Nut&lt;T&gt;
    ///
    /// Supports compression, encryption, and policy enforcement via IRoot processors.
    /// Backward compatible: Reads plain JSON data from before IRoot adoption.
    /// </summary>
    public class PostgreSqlTrunk<T> : TrunkBase<T> where T : class
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly string _schema;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        private const int BATCH_SIZE = 100;
        private const int FLUSH_INTERVAL_MS = 200;

        /// <summary>
        /// Create PostgreSQL trunk
        /// </summary>
        /// <param name="connectionString">PostgreSQL connection string</param>
        /// <param name="tableName">Optional custom table name. Default: acorn_{type_name}</param>
        /// <param name="schema">Database schema. Default: public</param>
        /// <param name="batchSize">Write batch size (default: 100)</param>
        /// <param name="serializer">Optional custom serializer. Default: NewtonsoftJsonSerializer</param>
        public PostgreSqlTrunk(string connectionString, string? tableName = null, string schema = "public", int batchSize = 100, ISerializer? serializer = null)
            : base(
                serializer,
                enableBatching: true,
                batchThreshold: batchSize,
                flushIntervalMs: FLUSH_INTERVAL_MS)
        {
            _schema = schema;
            _tableName = tableName ?? $"acorn_{typeof(T).Name.ToLower()}";

            // Enable connection pooling in connection string
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
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
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            // Create schema if not exists
            var createSchemaSql = $"CREATE SCHEMA IF NOT EXISTS {_schema}";
            using (var schemaCmd = new NpgsqlCommand(createSchemaSql, conn))
            {
                schemaCmd.ExecuteNonQuery();
            }

            // Create table if not exists
            var createTableSql = $@"
                CREATE TABLE IF NOT EXISTS {_schema}.{_tableName} (
                    id TEXT PRIMARY KEY NOT NULL,
                    json_data JSONB NOT NULL,
                    timestamp TIMESTAMPTZ NOT NULL,
                    version INTEGER NOT NULL,
                    expires_at TIMESTAMPTZ NULL
                )";

            using (var tableCmd = new NpgsqlCommand(createTableSql, conn))
            {
                tableCmd.ExecuteNonQuery();
            }

            // Create index on timestamp
            var createIndexSql = $@"
                CREATE INDEX IF NOT EXISTS idx_{_tableName}_timestamp
                ON {_schema}.{_tableName} (timestamp DESC)";

            using (var idxCmd = new NpgsqlCommand(createIndexSql, conn))
            {
                idxCmd.ExecuteNonQuery();
            }
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
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"SELECT json_data::text FROM {_schema}.{_tableName} WHERE id = @id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

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
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = $"DELETE FROM {_schema}.{_tableName} WHERE id = @id";

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@id", id);

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
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"SELECT json_data::text FROM {_schema}.{_tableName} ORDER BY timestamp DESC";

            using var cmd = new NpgsqlCommand(sql, conn);
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
            throw new NotSupportedException("PostgreSqlTrunk does not support history. Use DocumentStoreTrunk for versioning.");
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
            TrunkType = "PostgreSqlTrunk"
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

            AcornLog.Info($"   💾 Imported {incomingList.Count} nuts to PostgreSQL");
        }

        protected override async Task WriteToStorageAsync(string id, byte[] processedBytes, DateTime timestamp, int version)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            await WriteToPostgreSQL(conn, null, id, processedBytes, timestamp, version);
        }

        protected override async Task WriteBatchToStorageAsync(List<PendingWrite> batch)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Use transaction for batch insert (massive speedup!)
            using var transaction = conn.BeginTransaction();

            try
            {
                foreach (var write in batch)
                {
                    await WriteToPostgreSQL(conn, transaction, write.Id, write.ProcessedData, write.Timestamp, write.Version);
                }

                await transaction.CommitAsync();
                AcornLog.Info($"   💾 Flushed {batch.Count} nuts to PostgreSQL");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task WriteToPostgreSQL(NpgsqlConnection conn, NpgsqlTransaction? transaction, string id, byte[] processedBytes, DateTime timestamp, int version)
        {
            var dataToStore = EncodeForStorage(processedBytes, "");

            // Get expires_at from the nut if needed
            var json = Encoding.UTF8.GetString(processedBytes);
            var nut = _serializer.Deserialize<Nut<T>>(json);
            var expiresAt = nut?.ExpiresAt;

            var sql = $@"
                INSERT INTO {_schema}.{_tableName} (id, json_data, timestamp, version, expires_at)
                VALUES (@id, @json::jsonb, @timestamp, @version, @expiresAt)
                ON CONFLICT (id) DO UPDATE SET
                    json_data = @json::jsonb,
                    timestamp = @timestamp,
                    version = @version,
                    expires_at = @expiresAt";

            using var cmd = transaction != null
                ? new NpgsqlCommand(sql, conn, transaction)
                : new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@json", dataToStore);
            cmd.Parameters.AddWithValue("@timestamp", timestamp);
            cmd.Parameters.AddWithValue("@version", version);
            cmd.Parameters.AddWithValue("@expiresAt", expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Execute custom SQL query with WHERE clause
        /// </summary>
        public IEnumerable<Nut<T>> Query(string whereClause)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            var sql = $"SELECT json_data::text FROM {_schema}.{_tableName} WHERE {whereClause} ORDER BY timestamp DESC";

            using var cmd = new NpgsqlCommand(sql, conn);
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
        public string TrunkType => "PostgreSqlTrunk";

        public override void Dispose()
        {
            if (_disposed) return;

            // Base class handles timer disposal and flush
            base.Dispose();
        }
    }
}
