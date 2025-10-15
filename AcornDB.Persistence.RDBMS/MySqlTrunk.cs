using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// MySQL-backed trunk implementation.
    /// Maps Tree&lt;T&gt; to a MySQL table with JSON support.
    /// OPTIMIZED with write batching, async support, and connection pooling.
    /// </summary>
    public class MySqlTrunk<T> : ITrunk<T>, ITrunkCapabilities, IDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly string? _database;
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
        /// Create MySQL trunk
        /// </summary>
        /// <param name="connectionString">MySQL connection string</param>
        /// <param name="tableName">Optional custom table name. Default: acorn_{TypeName}</param>
        /// <param name="database">Optional database name (if not in connection string)</param>
        /// <param name="batchSize">Write batch size (default: 100)</param>
        public MySqlTrunk(string connectionString, string? tableName = null, string? database = null, int batchSize = 100)
        {
            _database = database;
            _tableName = tableName ?? $"acorn_{typeof(T).Name}";
            _batchSize = batchSize;

            // Enable connection pooling in connection string
            var builder = new MySqlConnectionStringBuilder(connectionString)
            {
                Pooling = true,
                MinimumPoolSize = 2,
                MaximumPoolSize = 100
            };
            _connectionString = builder.ConnectionString;

            EnsureTable();

            // Auto-flush every 200ms
            _flushTimer = new Timer(_ => FlushAsync().GetAwaiter().GetResult(), null, 200, 200);
        }

        private void EnsureTable()
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            // Use database if specified
            if (!string.IsNullOrEmpty(_database))
            {
                using var useDbCmd = new MySqlCommand($"USE `{_database}`", conn);
                useDbCmd.ExecuteNonQuery();
            }

            // Create table if not exists (MySQL 5.7+)
            var createTableSql = $@"
                CREATE TABLE IF NOT EXISTS `{_tableName}` (
                    id VARCHAR(450) PRIMARY KEY NOT NULL,
                    json_data JSON NOT NULL,
                    timestamp DATETIME(6) NOT NULL,
                    version INT NOT NULL,
                    expires_at DATETIME(6) NULL,
                    INDEX idx_{_tableName}_timestamp (timestamp DESC)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

            using var cmd = new MySqlCommand(createTableSql, conn);
            cmd.ExecuteNonQuery();
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
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            if (!string.IsNullOrEmpty(_database))
            {
                using var useDbCmd = new MySqlCommand($"USE `{_database}`", conn);
                await useDbCmd.ExecuteNonQueryAsync();
            }

            var sql = $"SELECT json_data FROM `{_tableName}` WHERE id = @id";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var json = reader.GetString(0);
                return JsonConvert.DeserializeObject<Nut<T>>(json);
            }

            return null;
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
                using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync();

                if (!string.IsNullOrEmpty(_database))
                {
                    using var useDbCmd = new MySqlCommand($"USE `{_database}`", conn);
                    await useDbCmd.ExecuteNonQueryAsync();
                }

                var sql = $"DELETE FROM `{_tableName}` WHERE id = @id";

                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@id", id);

                await cmd.ExecuteNonQueryAsync();
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
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            if (!string.IsNullOrEmpty(_database))
            {
                using var useDbCmd = new MySqlCommand($"USE `{_database}`", conn);
                await useDbCmd.ExecuteNonQueryAsync();
            }

            var sql = $"SELECT json_data FROM `{_tableName}` ORDER BY timestamp DESC";

            using var cmd = new MySqlCommand(sql, conn);
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
            throw new NotSupportedException("MySqlTrunk does not support history. Use DocumentStoreTrunk for versioning.");
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
                using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync();

                if (!string.IsNullOrEmpty(_database))
                {
                    using var useDbCmd = new MySqlCommand($"USE `{_database}`", conn);
                    await useDbCmd.ExecuteNonQueryAsync();
                }

                // Use transaction for batch import (massive speedup!)
                using var transaction = await conn.BeginTransactionAsync();

                var sql = $@"
                    INSERT INTO `{_tableName}` (id, json_data, timestamp, version, expires_at)
                    VALUES (@id, @json, @timestamp, @version, @expiresAt)
                    ON DUPLICATE KEY UPDATE
                        json_data = @json,
                        timestamp = @timestamp,
                        version = @version,
                        expires_at = @expiresAt";

                using var cmd = new MySqlCommand(sql, conn, transaction);

                // Add parameters once, reuse for all writes
                cmd.Parameters.Add("@id", MySqlDbType.VarChar, 450);
                cmd.Parameters.Add("@json", MySqlDbType.JSON);
                cmd.Parameters.Add("@timestamp", MySqlDbType.DateTime);
                cmd.Parameters.Add("@version", MySqlDbType.Int32);
                cmd.Parameters.Add("@expiresAt", MySqlDbType.DateTime);

                foreach (var nut in incomingList)
                {
                    var json = JsonConvert.SerializeObject(nut);

                    cmd.Parameters["@id"].Value = nut.Id;
                    cmd.Parameters["@json"].Value = json;
                    cmd.Parameters["@timestamp"].Value = nut.Timestamp;
                    cmd.Parameters["@version"].Value = nut.Version;
                    cmd.Parameters["@expiresAt"].Value = nut.ExpiresAt.HasValue ? (object)nut.ExpiresAt.Value : DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                Console.WriteLine($"   💾 Imported {incomingList.Count} nuts to MySQL");
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
                using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync();

                if (!string.IsNullOrEmpty(_database))
                {
                    using var useDbCmd = new MySqlCommand($"USE `{_database}`", conn);
                    await useDbCmd.ExecuteNonQueryAsync();
                }

                // Use transaction for batch insert (massive speedup!)
                using var transaction = await conn.BeginTransactionAsync();

                var sql = $@"
                    INSERT INTO `{_tableName}` (id, json_data, timestamp, version, expires_at)
                    VALUES (@id, @json, @timestamp, @version, @expiresAt)
                    ON DUPLICATE KEY UPDATE
                        json_data = @json,
                        timestamp = @timestamp,
                        version = @version,
                        expires_at = @expiresAt";

                using var cmd = new MySqlCommand(sql, conn, transaction);

                // Add parameters once, reuse for all writes
                cmd.Parameters.Add("@id", MySqlDbType.VarChar, 450);
                cmd.Parameters.Add("@json", MySqlDbType.JSON);
                cmd.Parameters.Add("@timestamp", MySqlDbType.DateTime);
                cmd.Parameters.Add("@version", MySqlDbType.Int32);
                cmd.Parameters.Add("@expiresAt", MySqlDbType.DateTime);

                foreach (var write in toWrite)
                {
                    var json = JsonConvert.SerializeObject(write.Nut);

                    cmd.Parameters["@id"].Value = write.Id;
                    cmd.Parameters["@json"].Value = json;
                    cmd.Parameters["@timestamp"].Value = write.Nut.Timestamp;
                    cmd.Parameters["@version"].Value = write.Nut.Version;
                    cmd.Parameters["@expiresAt"].Value = write.Nut.ExpiresAt.HasValue ? (object)write.Nut.ExpiresAt.Value : DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                Console.WriteLine($"   💾 Flushed {toWrite.Count} nuts to MySQL");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Execute custom SQL query with WHERE clause
        /// </summary>
        public IEnumerable<Nut<T>> Query(string whereClause)
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            if (!string.IsNullOrEmpty(_database))
            {
                using var useDbCmd = new MySqlCommand($"USE `{_database}`", conn);
                useDbCmd.ExecuteNonQuery();
            }

            var sql = $"SELECT json_data FROM `{_tableName}` WHERE {whereClause} ORDER BY timestamp DESC";

            using var cmd = new MySqlCommand(sql, conn);
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
        public string TrunkType => "MySqlTrunk";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Flush pending writes before disposal
            FlushAsync().GetAwaiter().GetResult();

            _flushTimer?.Dispose();
            _writeLock?.Dispose();
        }
    }
}
