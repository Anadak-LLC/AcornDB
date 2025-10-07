using System;
using System.Collections.Generic;
using MySqlConnector;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// MySQL-backed trunk implementation.
    /// Maps Tree&lt;T&gt; to a MySQL table with JSON support.
    /// </summary>
    public class MySqlTrunk<T> : ITrunk<T>, ITrunkCapabilities, IDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly string? _database;
        private bool _disposed;

        /// <summary>
        /// Create MySQL trunk
        /// </summary>
        /// <param name="connectionString">MySQL connection string</param>
        /// <param name="tableName">Optional custom table name. Default: acorn_{TypeName}</param>
        /// <param name="database">Optional database name (if not in connection string)</param>
        public MySqlTrunk(string connectionString, string? tableName = null, string? database = null)
        {
            _connectionString = connectionString;
            _database = database;
            _tableName = tableName ?? $"acorn_{typeof(T).Name}";

            EnsureTable();
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

        public void Save(string id, Nut<T> nut)
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            if (!string.IsNullOrEmpty(_database))
            {
                using var useDbCmd = new MySqlCommand($"USE `{_database}`", conn);
                useDbCmd.ExecuteNonQuery();
            }

            var json = JsonConvert.SerializeObject(nut);

            var sql = $@"
                INSERT INTO `{_tableName}` (id, json_data, timestamp, version, expires_at)
                VALUES (@id, @json, @timestamp, @version, @expiresAt)
                ON DUPLICATE KEY UPDATE
                    json_data = @json,
                    timestamp = @timestamp,
                    version = @version,
                    expires_at = @expiresAt";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@json", json);
            cmd.Parameters.AddWithValue("@timestamp", nut.Timestamp);
            cmd.Parameters.AddWithValue("@version", nut.Version);
            cmd.Parameters.AddWithValue("@expiresAt", nut.ExpiresAt.HasValue ? (object)nut.ExpiresAt.Value : DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public Nut<T>? Load(string id)
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            if (!string.IsNullOrEmpty(_database))
            {
                using var useDbCmd = new MySqlCommand($"USE `{_database}`", conn);
                useDbCmd.ExecuteNonQuery();
            }

            var sql = $"SELECT json_data FROM `{_tableName}` WHERE id = @id";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var json = reader.GetString(0);
                return JsonConvert.DeserializeObject<Nut<T>>(json);
            }

            return null;
        }

        public void Delete(string id)
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            if (!string.IsNullOrEmpty(_database))
            {
                using var useDbCmd = new MySqlCommand($"USE `{_database}`", conn);
                useDbCmd.ExecuteNonQuery();
            }

            var sql = $"DELETE FROM `{_tableName}` WHERE id = @id";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            cmd.ExecuteNonQuery();
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            if (!string.IsNullOrEmpty(_database))
            {
                using var useDbCmd = new MySqlCommand($"USE `{_database}`", conn);
                useDbCmd.ExecuteNonQuery();
            }

            var sql = $"SELECT json_data FROM `{_tableName}` ORDER BY timestamp DESC";

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
            foreach (var nut in incoming)
            {
                Save(nut.Id, nut);
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
        public bool SupportsAsync => false;
        public string TrunkType => "MySqlTrunk";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
