using System;
using System.Collections.Generic;
using Npgsql;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// PostgreSQL-backed trunk implementation.
    /// Maps Tree&lt;T&gt; to a PostgreSQL table with native JSON support.
    /// </summary>
    public class PostgreSqlTrunk<T> : ITrunk<T>, ITrunkCapabilities, IDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly string _schema;
        private bool _disposed;

        /// <summary>
        /// Create PostgreSQL trunk
        /// </summary>
        /// <param name="connectionString">PostgreSQL connection string</param>
        /// <param name="tableName">Optional custom table name. Default: acorn_{type_name}</param>
        /// <param name="schema">Database schema. Default: public</param>
        public PostgreSqlTrunk(string connectionString, string? tableName = null, string schema = "public")
        {
            _connectionString = connectionString;
            _schema = schema;
            _tableName = tableName ?? $"acorn_{typeof(T).Name.ToLower()}";

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

        public void Save(string id, Nut<T> nut)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            var json = JsonConvert.SerializeObject(nut);

            var sql = $@"
                INSERT INTO {_schema}.{_tableName} (id, json_data, timestamp, version, expires_at)
                VALUES (@id, @json::jsonb, @timestamp, @version, @expiresAt)
                ON CONFLICT (id) DO UPDATE SET
                    json_data = @json::jsonb,
                    timestamp = @timestamp,
                    version = @version,
                    expires_at = @expiresAt";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@json", json);
            cmd.Parameters.AddWithValue("@timestamp", nut.Timestamp);
            cmd.Parameters.AddWithValue("@version", nut.Version);
            cmd.Parameters.AddWithValue("@expiresAt", nut.ExpiresAt.HasValue ? (object)nut.ExpiresAt.Value : DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public Nut<T>? Load(string id)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            var sql = $"SELECT json_data::text FROM {_schema}.{_tableName} WHERE id = @id";

            using var cmd = new NpgsqlCommand(sql, conn);
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
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            var sql = $"DELETE FROM {_schema}.{_tableName} WHERE id = @id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            cmd.ExecuteNonQuery();
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            var sql = $"SELECT json_data::text FROM {_schema}.{_tableName} ORDER BY timestamp DESC";

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

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("PostgreSqlTrunk does not support history. Use DocumentStoreTrunk for versioning.");
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
        public bool SupportsAsync => false;
        public string TrunkType => "PostgreSqlTrunk";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
