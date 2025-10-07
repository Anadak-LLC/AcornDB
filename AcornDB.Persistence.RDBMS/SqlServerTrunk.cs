using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// SQL Server-backed trunk implementation.
    /// Maps Tree&lt;T&gt; to a SQL Server table with JSON support.
    /// </summary>
    public class SqlServerTrunk<T> : ITrunk<T>, ITrunkCapabilities, IDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly string _schema;
        private bool _disposed;

        /// <summary>
        /// Create SQL Server trunk
        /// </summary>
        /// <param name="connectionString">SQL Server connection string</param>
        /// <param name="tableName">Optional custom table name. Default: Acorn_{TypeName}</param>
        /// <param name="schema">Database schema. Default: dbo</param>
        public SqlServerTrunk(string connectionString, string? tableName = null, string schema = "dbo")
        {
            _connectionString = connectionString;
            _schema = schema;
            _tableName = tableName ?? $"Acorn_{typeof(T).Name}";

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

        public void Save(string id, Nut<T> nut)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            var json = JsonConvert.SerializeObject(nut);

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

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@JsonData", json);
            cmd.Parameters.AddWithValue("@Timestamp", nut.Timestamp);
            cmd.Parameters.AddWithValue("@Version", nut.Version);
            cmd.Parameters.AddWithValue("@ExpiresAt", nut.ExpiresAt.HasValue ? (object)nut.ExpiresAt.Value : DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public Nut<T>? Load(string id)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            var sql = $"SELECT JsonData FROM [{_schema}].[{_tableName}] WHERE Id = @Id";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

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
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            var sql = $"DELETE FROM [{_schema}].[{_tableName}] WHERE Id = @Id";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

            cmd.ExecuteNonQuery();
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            var sql = $"SELECT JsonData FROM [{_schema}].[{_tableName}] ORDER BY Timestamp DESC";

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

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("SqlServerTrunk does not support history. Use DocumentStoreTrunk for versioning.");
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
        public bool SupportsAsync => false;
        public string TrunkType => "SqlServerTrunk";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
