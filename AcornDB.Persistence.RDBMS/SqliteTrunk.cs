using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// SQLite-backed trunk implementation.
    /// Maps Tree&lt;T&gt; to a SQLite table with columns: id, json_data, timestamp, version, expires_at.
    /// Each tree type gets its own table named: acorn_{TypeName}
    /// </summary>
    public class SqliteTrunk<T> : ITrunk<T>, ITrunkCapabilities, IDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private bool _disposed;

        /// <summary>
        /// Create SQLite trunk with explicit database path
        /// </summary>
        /// <param name="databasePath">Path to SQLite database file (will be created if doesn't exist)</param>
        /// <param name="tableName">Optional custom table name. Default: acorn_{TypeName}</param>
        public SqliteTrunk(string databasePath, string? tableName = null)
        {
            var typeName = typeof(T).Name;
            _tableName = tableName ?? $"acorn_{typeName}";
            _connectionString = $"Data Source={databasePath}";

            EnsureDatabase();
        }

        private void EnsureDatabase()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

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

        public void Save(string id, Nut<T> nut)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var json = JsonConvert.SerializeObject(nut);
            var timestampStr = nut.Timestamp.ToString("O"); // ISO 8601 format
            var expiresAtStr = nut.ExpiresAt?.ToString("O");

            var sql = $@"
                INSERT INTO {_tableName} (id, json_data, timestamp, version, expires_at)
                VALUES (@id, @json, @timestamp, @version, @expiresAt)
                ON CONFLICT(id) DO UPDATE SET
                    json_data = @json,
                    timestamp = @timestamp,
                    version = @version,
                    expires_at = @expiresAt";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@json", json);
            cmd.Parameters.AddWithValue("@timestamp", timestampStr);
            cmd.Parameters.AddWithValue("@version", nut.Version);
            cmd.Parameters.AddWithValue("@expiresAt", expiresAtStr ?? (object)DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public Nut<T>? Load(string id)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var sql = $"SELECT json_data FROM {_tableName} WHERE id = @id";

            using var cmd = new SqliteCommand(sql, conn);
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
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var sql = $"DELETE FROM {_tableName} WHERE id = @id";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            cmd.ExecuteNonQuery();
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var sql = $"SELECT json_data FROM {_tableName} ORDER BY timestamp DESC";

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
            foreach (var nut in incoming)
            {
                Save(nut.Id, nut);
            }
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

        // ITrunkCapabilities implementation
        public bool SupportsHistory => false;
        public bool SupportsSync => true;
        public bool IsDurable => true;
        public bool SupportsAsync => false;
        public string TrunkType => "SqliteTrunk";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
