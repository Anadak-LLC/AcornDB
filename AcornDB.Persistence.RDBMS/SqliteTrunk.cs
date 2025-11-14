using System;
using AcornDB.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Policy;
using AcornDB.Storage;
using AcornDB.Storage.Serialization;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// High-performance SQLite-backed trunk with connection pooling, WAL mode, batching, and async support.
    /// Maps Tree&lt;T&gt; to a SQLite table with columns: id, json_data, timestamp, version, expires_at.
    /// Each tree type gets its own table named: acorn_{TypeName}
    /// Supports extensible IRoot processors for compression, encryption, policy enforcement, etc.
    ///
    /// Storage Pipeline:
    /// Write: Nut<T> → Serialize to JSON → Root Chain (ascending) → byte[] → Store in database
    /// Read: Read from database → byte[] → Root Chain (descending) → Deserialize → Nut<T>
    /// </summary>
    public class SqliteTrunk<T> : TrunkBase<T>, IDisposable
        where T : class
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        private const int BATCH_SIZE = 100;
        private const int FLUSH_INTERVAL_MS = 200;

        /// <summary>
        /// Create high-performance SQLite trunk with connection pooling and WAL mode
        /// </summary>
        /// <param name="databasePath">Path to SQLite database file (will be created if doesn't exist)</param>
        /// <param name="tableName">Optional custom table name. Default: acorn_{TypeName}</param>
        /// <param name="serializer">Optional custom serializer. Default: NewtonsoftJsonSerializer</param>
        public SqliteTrunk(string databasePath, string? tableName = null, ISerializer? serializer = null)
            : base(
                serializer,
                enableBatching: true,            // Enable batching via TrunkBase
                batchThreshold: BATCH_SIZE,
                flushIntervalMs: FLUSH_INTERVAL_MS)
        {
            var typeName = typeof(T).Name;
            _tableName = tableName ?? $"acorn_{typeName}";

            // Connection string with pooling and optimization
            _connectionString = $"Data Source={databasePath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True";

            EnsureDatabase();

            AcornLog.Info($"💾 SqliteTrunk initialized:");
            AcornLog.Info($"   Database: {databasePath}");
            AcornLog.Info($"   Table: {_tableName}");
            AcornLog.Info($"   WAL Mode: Enabled");
            AcornLog.Info($"   Batch Size: {BATCH_SIZE}");
        }

        private void EnsureDatabase()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // Enable WAL mode for better concurrency (10-50x faster writes!)
            ExecutePragma(conn, "PRAGMA journal_mode=WAL");

            // Performance optimizations
            ExecutePragma(conn, "PRAGMA synchronous=NORMAL");  // Faster, still crash-safe with WAL
            ExecutePragma(conn, "PRAGMA cache_size=-64000");   // 64MB cache
            ExecutePragma(conn, "PRAGMA temp_store=MEMORY");   // In-memory temp tables
            ExecutePragma(conn, "PRAGMA mmap_size=268435456"); // 256MB memory-mapped I/O
            ExecutePragma(conn, "PRAGMA page_size=4096");      // Optimal page size

            var createTableSql = $@"
                CREATE TABLE IF NOT EXISTS {_tableName} (
                    id TEXT PRIMARY KEY NOT NULL,
                    json_data TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    expires_at TEXT NULL
                )";

            using var cmd = new SqliteCommand(createTableSql, conn);
            cmd.ExecuteNonQuery();

            // Create index on timestamp for performance
            var createIndexSql = $@"
                CREATE INDEX IF NOT EXISTS idx_{_tableName}_timestamp
                ON {_tableName}(timestamp DESC)";

            using var idxCmd = new SqliteCommand(createIndexSql, conn);
            idxCmd.ExecuteNonQuery();
        }

        private void ExecutePragma(SqliteConnection conn, string pragma)
        {
            using var cmd = new SqliteCommand(pragma, conn);
            cmd.ExecuteNonQuery();
        }

        public override void Stash(string id, Nut<T> nut)
        {
            // Use TrunkBase batching infrastructure
            // This handles IRoot pipeline processing, batching, and auto-flush
            StashWithBatchingAsync(id, nut).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task StashAsync(string id, Nut<T> nut)
        {
            // Use TrunkBase batching infrastructure
            await StashWithBatchingAsync(id, nut);
        }

        public override Nut<T>? Crack(string id)
        {
            return CrackAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Nut<T>?> CrackAsync(string id)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"SELECT json_data FROM {_tableName} WHERE id = @id";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var dataStr = reader.GetString(0);

                // Step 1: Decode from base64 or use as plain JSON
                byte[] storedBytes;
                try
                {
                    storedBytes = Convert.FromBase64String(dataStr);
                }
                catch
                {
                    // Fallback for backward compatibility with plain JSON
                    storedBytes = Encoding.UTF8.GetBytes(dataStr);
                }

                // Step 2: Process through root chain in descending sequence order (reverse)
                var processedBytes = ProcessThroughRootsDescending(storedBytes, id);

                // Step 3: Deserialize bytes to Nut<T>
                try
                {
                    var json = Encoding.UTF8.GetString(processedBytes);
                    return _serializer.Deserialize<Nut<T>>(json);
                }
                catch (Exception ex)
                {
                    AcornLog.Info($"⚠️ Failed to deserialize nut '{id}': {ex.Message}");
                    return null;
                }
            }

            return null;
        }

        public override void Toss(string id)
        {
            TossAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task TossAsync(string id)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"DELETE FROM {_tableName} WHERE id = @id";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            await cmd.ExecuteNonQueryAsync();
        }

        public override IEnumerable<Nut<T>> CrackAll()
        {
            return CrackAllAsync().GetAwaiter().GetResult();
        }

        public async Task<IEnumerable<Nut<T>>> CrackAllAsync()
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"SELECT json_data FROM {_tableName} ORDER BY timestamp DESC";

            using var cmd = new SqliteCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var nuts = new List<Nut<T>>();
            while (await reader.ReadAsync())
            {
                var json = reader.GetString(0);
                var nut = JsonConvert.DeserializeObject<Nut<T>>(json);
                if (nut != null)
                    nuts.Add(nut);
            }

            return nuts;
        }

        public override IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            // SQLite trunk doesn't maintain history by default
            // For history support, use DocumentStoreTrunk or GitHubTrunk
            throw new NotSupportedException("SqliteTrunk does not support history. Use DocumentStoreTrunk for versioning.");
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
            SupportsNativeIndexes = true,
            SupportsFullTextSearch = false,  // TODO: Add FTS5 support
            SupportsComputedIndexes = true,  // SQLite supports expression indexes
            TrunkType = "SqliteTrunk"
        };

        public async Task ImportChangesAsync(IEnumerable<Nut<T>> incoming)
        {
            var changesList = incoming.ToList();

            // Stash all nuts (batching handled by TrunkBase)
            foreach (var nut in changesList)
            {
                await StashAsync(nut.Id, nut);
            }

            // Force flush
            await FlushBatchAsync();

            AcornLog.Info($"   💾 Imported {changesList.Count} nuts to SQLite");
        }

        /// <summary>
        /// Execute custom SQL query and return nuts
        /// Advanced: Allows querying by timestamp, version, etc.
        /// </summary>
        /// <param name="whereClause">SQL WHERE clause (e.g., "timestamp > '2025-01-01'")</param>
        public IEnumerable<Nut<T>> Query(string whereClause)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var sql = $"SELECT json_data FROM {_tableName} WHERE {whereClause} ORDER BY timestamp DESC";

            using var cmd = new SqliteCommand(sql, conn);
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

        /// <summary>
        /// Get count of nuts in trunk
        /// </summary>
        public int Count()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var sql = $"SELECT COUNT(*) FROM {_tableName}";
            using var cmd = new SqliteCommand(sql, conn);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Execute raw SQL command (for migrations, cleanup, etc.)
        /// </summary>
        public int ExecuteCommand(string sql)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = new SqliteCommand(sql, conn);
            return cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Vacuum database to reclaim space and optimize
        /// </summary>
        public void Vacuum()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = new SqliteCommand("VACUUM", conn);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Flush pending writes to database immediately (synchronous)
        /// </summary>
        public void Flush()
        {
            FlushBatchAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Flush pending writes to database using a transaction
        /// </summary>
        /// <summary>
        /// Write a single item to SQLite (used by TrunkBase for immediate writes if needed)
        /// </summary>
        protected override async Task WriteToStorageAsync(string id, byte[] processedBytes, DateTime timestamp, int version)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await WriteToSqlite(conn, null, id, processedBytes, timestamp, version);
        }

        /// <summary>
        /// Write a batch of items to SQLite (optimized with transaction)
        /// </summary>
        protected override async Task WriteBatchToStorageAsync(List<PendingWrite> batch)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Use transaction for batch insert (massive speedup!)
            using var transaction = conn.BeginTransaction();

            try
            {
                foreach (var write in batch)
                {
                    await WriteToSqlite(conn, transaction, write.Id, write.ProcessedData, write.Timestamp, write.Version);
                }

                await transaction.CommitAsync();
                AcornLog.Info($"   💾 Flushed {batch.Count} nuts to SQLite");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Helper method to write processed data to SQLite
        /// </summary>
        private async Task WriteToSqlite(SqliteConnection conn, SqliteTransaction? transaction, string id, byte[] processedBytes, DateTime timestamp, int version)
        {
            // Convert to base64 for storage
            var dataStr = Convert.ToBase64String(processedBytes);
            var timestampStr = timestamp.ToString("O");

            // Get the full nut to extract payload and expires_at
            // Note: This is a bit inefficient, but maintains compatibility
            // In the future, we could pass these values directly
            var json = Encoding.UTF8.GetString(processedBytes);
            var nut = _serializer.Deserialize<Nut<T>>(json);
            var expiresAtStr = nut?.ExpiresAt?.ToString("O");
            var payloadJson = nut != null ? _serializer.Serialize(nut.Payload) : "{}";

            var sql = $@"
                INSERT INTO {_tableName} (id, json_data, payload_json, timestamp, version, expires_at)
                VALUES (@id, @json, @payloadJson, @timestamp, @version, @expiresAt)
                ON CONFLICT(id) DO UPDATE SET
                    json_data = @json,
                    payload_json = @payloadJson,
                    timestamp = @timestamp,
                    version = @version,
                    expires_at = @expiresAt";

            using var cmd = transaction != null
                ? new SqliteCommand(sql, conn, transaction)
                : new SqliteCommand(sql, conn);

            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@json", dataStr);
            cmd.Parameters.AddWithValue("@payloadJson", payloadJson);
            cmd.Parameters.AddWithValue("@timestamp", timestampStr);
            cmd.Parameters.AddWithValue("@version", version);
            cmd.Parameters.AddWithValue("@expiresAt", expiresAtStr ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        // Native Index Support

        /// <summary>
        /// Create a native SQLite index for a property.
        /// This uses CREATE INDEX with json_extract() for efficient database-level indexing.
        /// </summary>
        /// <typeparam name="TProperty">Type of the indexed property</typeparam>
        /// <param name="name">Index name (e.g., "IX_User_Email")</param>
        /// <param name="propertySelector">Expression selecting the property to index</param>
        /// <param name="isUnique">Whether this is a unique index</param>
        /// <returns>The created native index</returns>
        public SqliteNativeIndex<T, TProperty> CreateNativeIndex<TProperty>(
            string name,
            System.Linq.Expressions.Expression<Func<T, TProperty>> propertySelector,
            bool isUnique = false)
        {
            var index = new SqliteNativeIndex<T, TProperty>(
                _connectionString,
                _tableName,
                name,
                propertySelector,
                isUnique);

            index.CreateInDatabase();
            return index;
        }

        /// <summary>
        /// Drop a native SQLite index by name.
        /// </summary>
        public void DropNativeIndex(string indexName)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"DROP INDEX IF EXISTS {indexName}";
            command.ExecuteNonQuery();

            AcornLog.Info($"✓ Dropped SQLite index: {indexName}");
        }

        /// <summary>
        /// List all indexes on this table.
        /// </summary>
        public List<string> ListNativeIndexes()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT name
                FROM sqlite_master
                WHERE type='index'
                  AND tbl_name=@tableName
                  AND name NOT LIKE 'sqlite_%'";
            command.Parameters.AddWithValue("@tableName", _tableName);

            var indexes = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                indexes.Add(reader.GetString(0));
            }

            return indexes;
        }

        // ITrunkCapabilities implementation
        public bool SupportsHistory => false;
        public bool SupportsSync => true;
        public bool IsDurable => true;
        public bool SupportsAsync => true;  // Now supports async!
        public string TrunkType => "SqliteTrunk";

        public override void Dispose()
        {
            if (_disposed) return;

            // Base class handles timer disposal and flush
            // This ensures proper batching cleanup
            base.Dispose();

            // Dispose SqliteTrunk-specific resources
            _connectionLock?.Dispose();
        }
    }
}
