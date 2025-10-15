using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using AcornDB;
using AcornDB.Storage;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// SQL Server-backed trunk implementation.
    /// Maps Tree&lt;T&gt; to a SQL Server table with JSON support.
    /// OPTIMIZED with write batching, async support, and connection pooling.
    /// </summary>
    public class SqlServerTrunk<T> : ITrunk<T>, ITrunkCapabilities, IDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly string _schema;
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
        /// Create SQL Server trunk
        /// </summary>
        /// <param name="connectionString">SQL Server connection string</param>
        /// <param name="tableName">Optional custom table name. Default: Acorn_{TypeName}</param>
        /// <param name="schema">Database schema. Default: dbo</param>
        /// <param name="batchSize">Write batch size (default: 100)</param>
        public SqlServerTrunk(string connectionString, string? tableName = null, string schema = "dbo", int batchSize = 100)
        {
            _schema = schema;
            _tableName = tableName ?? $"Acorn_{typeof(T).Name}";
            _batchSize = batchSize;

            // Enable connection pooling in connection string
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                Pooling = true,
                MinPoolSize = 2,
                MaxPoolSize = 100
            };
            _connectionString = builder.ConnectionString;

            EnsureTable();

            // Auto-flush every 200ms
            _flushTimer = new Timer(_ => FlushAsync().GetAwaiter().GetResult(), null, 200, 200);
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
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"SELECT JsonData FROM [{_schema}].[{_tableName}] WHERE Id = @Id";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

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
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = $"DELETE FROM [{_schema}].[{_tableName}] WHERE Id = @Id";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Id", id);

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
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"SELECT JsonData FROM [{_schema}].[{_tableName}] ORDER BY Timestamp DESC";

            using var cmd = new SqlCommand(sql, conn);
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
            throw new NotSupportedException("SqlServerTrunk does not support history. Use DocumentStoreTrunk for versioning.");
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
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                // Use transaction for batch import (massive speedup!)
                using var transaction = conn.BeginTransaction();

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

                using var cmd = new SqlCommand(sql, conn, transaction);

                // Add parameters once, reuse for all writes
                cmd.Parameters.Add("@Id", SqlDbType.NVarChar, 450);
                cmd.Parameters.Add("@JsonData", SqlDbType.NVarChar, -1);
                cmd.Parameters.Add("@Timestamp", SqlDbType.DateTime2);
                cmd.Parameters.Add("@Version", SqlDbType.Int);
                cmd.Parameters.Add("@ExpiresAt", SqlDbType.DateTime2);

                foreach (var nut in incomingList)
                {
                    var json = JsonConvert.SerializeObject(nut);

                    cmd.Parameters["@Id"].Value = nut.Id;
                    cmd.Parameters["@JsonData"].Value = json;
                    cmd.Parameters["@Timestamp"].Value = nut.Timestamp;
                    cmd.Parameters["@Version"].Value = nut.Version;
                    cmd.Parameters["@ExpiresAt"].Value = nut.ExpiresAt.HasValue ? (object)nut.ExpiresAt.Value : DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                Console.WriteLine($"   💾 Imported {incomingList.Count} nuts to SQL Server");
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
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                // Use transaction for batch insert (massive speedup!)
                using var transaction = conn.BeginTransaction();

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

                using var cmd = new SqlCommand(sql, conn, transaction);

                // Add parameters once, reuse for all writes
                cmd.Parameters.Add("@Id", SqlDbType.NVarChar, 450);
                cmd.Parameters.Add("@JsonData", SqlDbType.NVarChar, -1);
                cmd.Parameters.Add("@Timestamp", SqlDbType.DateTime2);
                cmd.Parameters.Add("@Version", SqlDbType.Int);
                cmd.Parameters.Add("@ExpiresAt", SqlDbType.DateTime2);

                foreach (var write in toWrite)
                {
                    var json = JsonConvert.SerializeObject(write.Nut);

                    cmd.Parameters["@Id"].Value = write.Id;
                    cmd.Parameters["@JsonData"].Value = json;
                    cmd.Parameters["@Timestamp"].Value = write.Nut.Timestamp;
                    cmd.Parameters["@Version"].Value = write.Nut.Version;
                    cmd.Parameters["@ExpiresAt"].Value = write.Nut.ExpiresAt.HasValue ? (object)write.Nut.ExpiresAt.Value : DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                Console.WriteLine($"   💾 Flushed {toWrite.Count} nuts to SQL Server");
            }
            finally
            {
                _writeLock.Release();
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
        public bool SupportsAsync => true;
        public string TrunkType => "SqlServerTrunk";

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
