using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// High-performance SQLite-backed trunk with connection pooling, WAL mode, batching, and async support.
    /// Maps Tree&lt;T&gt; to a SQLite table with columns: id, json_data, timestamp, version, expires_at.
    /// Each tree type gets its own table named: acorn_{TypeName}
    /// </summary>
    public class SqliteTrunk<T> : ITrunk<T>, ITrunkCapabilities, IDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private readonly List<PendingWrite> _writeBuffer = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Timer? _flushTimer;
        private bool _disposed;

        private struct PendingWrite
        {
            public string Id;
            public Nut<T> Nut;
        }

        private const int BATCH_SIZE = 100;
        private const int FLUSH_INTERVAL_MS = 200;

        /// <summary>
        /// Create high-performance SQLite trunk with connection pooling and WAL mode
        /// </summary>
        /// <param name="databasePath">Path to SQLite database file (will be created if doesn't exist)</param>
        /// <param name="tableName">Optional custom table name. Default: acorn_{TypeName}</param>
        public SqliteTrunk(string databasePath, string? tableName = null)
        {
            var typeName = typeof(T).Name;
            _tableName = tableName ?? $"acorn_{typeName}";

            // Connection string with pooling and optimization
            _connectionString = $"Data Source={databasePath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True";

            EnsureDatabase();

            // Auto-flush timer for write batching
            _flushTimer = new Timer(_ =>
            {
                try { FlushAsync().Wait(); }
                catch { /* Swallow timer exceptions */ }
            }, null, FLUSH_INTERVAL_MS, FLUSH_INTERVAL_MS);

            Console.WriteLine($"💾 SqliteTrunk initialized:");
            Console.WriteLine($"   Database: {databasePath}");
            Console.WriteLine($"   Table: {_tableName}");
            Console.WriteLine($"   WAL Mode: Enabled");
            Console.WriteLine($"   Batch Size: {BATCH_SIZE}");
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

        public void Save(string id, Nut<T> nut)
        {
            SaveAsync(id, nut).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task SaveAsync(string id, Nut<T> nut)
        {
            // Add to write buffer for batching
            bool shouldFlush = false;
            lock (_writeBuffer)
            {
                _writeBuffer.Add(new PendingWrite { Id = id, Nut = nut });

                // Check if buffer is full
                if (_writeBuffer.Count >= BATCH_SIZE)
                {
                    shouldFlush = true;
                }
            }

            // Flush outside the lock
            if (shouldFlush)
            {
                await FlushAsync();
            }
        }

        public Nut<T>? Load(string id)
        {
            return LoadAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Nut<T>?> LoadAsync(string id)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"SELECT json_data FROM {_tableName} WHERE id = @id";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var json = reader.GetString(0);
                return JsonConvert.DeserializeObject<Nut<T>>(json);
            }

            return null;
        }

        public void Delete(string id)
        {
            DeleteAsync(id).GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task DeleteAsync(string id)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"DELETE FROM {_tableName} WHERE id = @id";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            await cmd.ExecuteNonQueryAsync();
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            return LoadAllAsync().GetAwaiter().GetResult();
        }

        public async Task<IEnumerable<Nut<T>>> LoadAllAsync()
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

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            // SQLite trunk doesn't maintain history by default
            // For history support, use DocumentStoreTrunk or GitHubTrunk
            throw new NotSupportedException("SqliteTrunk does not support history. Use DocumentStoreTrunk for versioning.");
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
            var changesList = incoming.ToList();

            // Add all to write buffer
            lock (_writeBuffer)
            {
                foreach (var nut in changesList)
                {
                    _writeBuffer.Add(new PendingWrite { Id = nut.Id, Nut = nut });
                }
            }

            // Flush everything
            await FlushAsync();

            Console.WriteLine($"   💾 Imported {changesList.Count} nuts to SQLite");
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
        /// Flush pending writes to database using a transaction
        /// </summary>
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
                using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();

                // Use transaction for batch insert (massive speedup!)
                using var transaction = conn.BeginTransaction();

                var sql = $@"
                    INSERT INTO {_tableName} (id, json_data, timestamp, version, expires_at)
                    VALUES (@id, @json, @timestamp, @version, @expiresAt)
                    ON CONFLICT(id) DO UPDATE SET
                        json_data = @json,
                        timestamp = @timestamp,
                        version = @version,
                        expires_at = @expiresAt";

                using var cmd = new SqliteCommand(sql, conn, transaction);

                // Add parameters once, reuse for all writes
                cmd.Parameters.Add("@id", SqliteType.Text);
                cmd.Parameters.Add("@json", SqliteType.Text);
                cmd.Parameters.Add("@timestamp", SqliteType.Text);
                cmd.Parameters.Add("@version", SqliteType.Integer);
                cmd.Parameters.Add("@expiresAt", SqliteType.Text);

                foreach (var write in toWrite)
                {
                    var json = JsonConvert.SerializeObject(write.Nut);
                    var timestampStr = write.Nut.Timestamp.ToString("O");
                    var expiresAtStr = write.Nut.ExpiresAt?.ToString("O");

                    cmd.Parameters["@id"].Value = write.Id;
                    cmd.Parameters["@json"].Value = json;
                    cmd.Parameters["@timestamp"].Value = timestampStr;
                    cmd.Parameters["@version"].Value = write.Nut.Version;
                    cmd.Parameters["@expiresAt"].Value = expiresAtStr ?? (object)DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                Console.WriteLine($"   💾 Flushed {toWrite.Count} nuts to SQLite");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // ITrunkCapabilities implementation
        public bool SupportsHistory => false;
        public bool SupportsSync => true;
        public bool IsDurable => true;
        public bool SupportsAsync => true;  // Now supports async!
        public string TrunkType => "SqliteTrunk";

        public void Dispose()
        {
            if (_disposed) return;

            _flushTimer?.Dispose();

            // Flush any pending writes
            try { FlushAsync().Wait(); } catch { }

            _connectionLock?.Dispose();
            _writeLock?.Dispose();

            _disposed = true;
        }
    }
}
