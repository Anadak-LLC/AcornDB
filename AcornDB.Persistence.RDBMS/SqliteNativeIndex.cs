using System;
using AcornDB.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using AcornDB.Indexing;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// SQLite-native index implementation that uses CREATE INDEX statements
    /// and json_extract() to index properties within the JSON document.
    ///
    /// Example DDL: CREATE INDEX idx_user_email ON acorn_user(json_extract(json_data, '$.Email'))
    ///
    /// Performance: Leverages SQLite's B-tree indexes for O(log n) lookups.
    /// </summary>
    public class SqliteNativeIndex<T, TProperty> : INativeScalarIndex<T, TProperty>
        where T : class
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private readonly Func<T, TProperty> _propertyExtractor;
        private bool _isCreated;

        public string Name { get; }
        public IndexType IndexType => IndexType.Scalar;
        public bool IsUnique { get; }
        public IndexState State { get; private set; }
        public Expression<Func<T, TProperty>> PropertySelector { get; }
        public string JsonPath { get; }
        public string CreateIndexDdl { get; }
        public string DropIndexDdl { get; }
        public bool IsCreated => _isCreated;

        /// <summary>
        /// Create a new SQLite native index
        /// </summary>
        /// <param name="connectionString">SQLite connection string</param>
        /// <param name="tableName">Table name (e.g., "acorn_user")</param>
        /// <param name="name">Index name (e.g., "IX_User_Email")</param>
        /// <param name="propertySelector">Expression to extract the indexed property</param>
        /// <param name="isUnique">Whether this is a unique index</param>
        public SqliteNativeIndex(
            string connectionString,
            string tableName,
            string name,
            Expression<Func<T, TProperty>> propertySelector,
            bool isUnique = false)
        {
            _connectionString = connectionString;
            _tableName = tableName;
            Name = name;
            IsUnique = isUnique;
            PropertySelector = propertySelector;
            _propertyExtractor = propertySelector.Compile();

            // Extract property name from expression
            var propertyName = ExtractPropertyName(propertySelector);
            if (string.IsNullOrEmpty(propertyName))
            {
                throw new ArgumentException("Could not extract property name from selector expression", nameof(propertySelector));
            }

            // Build JSON path: $.PropertyName
            JsonPath = $"$.{propertyName}";

            // Build CREATE INDEX DDL
            var uniqueKeyword = isUnique ? "UNIQUE " : "";
            CreateIndexDdl = $"CREATE {uniqueKeyword}INDEX IF NOT EXISTS {name} ON {tableName}(json_extract(payload_json, '{JsonPath}'))";

            // Build DROP INDEX DDL
            DropIndexDdl = $"DROP INDEX IF EXISTS {name}";

            State = IndexState.Building;
        }

        private string? ExtractPropertyName(Expression<Func<T, TProperty>> expression)
        {
            if (expression.Body is MemberExpression member)
            {
                return member.Member.Name;
            }
            if (expression.Body is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
            {
                return unaryMember.Member.Name;
            }
            return null;
        }

        public void Build(IEnumerable<object> documents)
        {
            // Native indexes don't need explicit building - they're maintained by SQLite
            // Just ensure the index is created in the database
            CreateInDatabase();
        }

        public void Add(string id, object document)
        {
            // Native indexes are automatically maintained by SQLite on INSERT/UPDATE
            // No action needed here
        }

        public void Remove(string id)
        {
            // Native indexes are automatically maintained by SQLite on DELETE
            // No action needed here
        }

        public void Clear()
        {
            // For native indexes, clearing means dropping and recreating
            DropFromDatabase();
            CreateInDatabase();
        }

        public void CreateInDatabase()
        {
            if (_isCreated) return;

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = CreateIndexDdl;
                command.ExecuteNonQuery();

                _isCreated = true;
                State = IndexState.Ready;

                AcornLog.Info($"✓ Created SQLite index: {Name}");
            }
            catch (Exception ex)
            {
                State = IndexState.Error;
                throw new InvalidOperationException($"Failed to create SQLite index '{Name}': {ex.Message}", ex);
            }
        }

        public void DropFromDatabase()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = DropIndexDdl;
                command.ExecuteNonQuery();

                _isCreated = false;
                State = IndexState.Building;

                AcornLog.Info($"✓ Dropped SQLite index: {Name}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to drop SQLite index '{Name}': {ex.Message}", ex);
            }
        }

        public bool VerifyInDatabase()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@name";
                command.Parameters.AddWithValue("@name", Name);

                var count = Convert.ToInt32(command.ExecuteScalar());
                _isCreated = count > 0;
                return _isCreated;
            }
            catch
            {
                return false;
            }
        }

        public IEnumerable<string> Lookup(TProperty value)
        {
            if (!_isCreated)
            {
                return Enumerable.Empty<string>();
            }

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = $@"
                    SELECT id
                    FROM {_tableName}
                    WHERE json_extract(payload_json, @jsonPath) = @value";
                command.Parameters.AddWithValue("@jsonPath", JsonPath);
                command.Parameters.AddWithValue("@value", value);

                var results = new List<string>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(reader.GetString(0));
                }

                return results;
            }
            catch (Exception ex)
            {
                AcornLog.Info($"Error in SQLite index lookup: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        public IEnumerable<string> Range(TProperty min, TProperty max)
        {
            if (!_isCreated)
            {
                return Enumerable.Empty<string>();
            }

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = $@"
                    SELECT id
                    FROM {_tableName}
                    WHERE json_extract(payload_json, @jsonPath) >= @min
                      AND json_extract(payload_json, @jsonPath) <= @max
                    ORDER BY json_extract(payload_json, @jsonPath)";
                command.Parameters.AddWithValue("@jsonPath", JsonPath);
                command.Parameters.AddWithValue("@min", min);
                command.Parameters.AddWithValue("@max", max);

                var results = new List<string>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(reader.GetString(0));
                }

                return results;
            }
            catch (Exception ex)
            {
                AcornLog.Info($"Error in SQLite index range query: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        public IEnumerable<string> GetAllSorted(bool ascending = true)
        {
            if (!_isCreated)
            {
                return Enumerable.Empty<string>();
            }

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var order = ascending ? "ASC" : "DESC";
                using var command = connection.CreateCommand();
                command.CommandText = $@"
                    SELECT id
                    FROM {_tableName}
                    ORDER BY json_extract(payload_json, @jsonPath) {order}";
                command.Parameters.AddWithValue("@jsonPath", JsonPath);

                var results = new List<string>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(reader.GetString(0));
                }

                return results;
            }
            catch (Exception ex)
            {
                AcornLog.Info($"Error in SQLite index sorted query: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        public TProperty? GetMin()
        {
            if (!_isCreated)
            {
                return default;
            }

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = $@"
                    SELECT MIN(json_extract(payload_json, @jsonPath))
                    FROM {_tableName}";
                command.Parameters.AddWithValue("@jsonPath", JsonPath);

                var result = command.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                {
                    return default;
                }

                return (TProperty)Convert.ChangeType(result, typeof(TProperty));
            }
            catch
            {
                return default;
            }
        }

        public TProperty? GetMax()
        {
            if (!_isCreated)
            {
                return default;
            }

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = $@"
                    SELECT MAX(json_extract(payload_json, @jsonPath))
                    FROM {_tableName}";
                command.Parameters.AddWithValue("@jsonPath", JsonPath);

                var result = command.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                {
                    return default;
                }

                return (TProperty)Convert.ChangeType(result, typeof(TProperty));
            }
            catch
            {
                return default;
            }
        }

        public IndexStatistics GetStatistics()
        {
            if (!_isCreated)
            {
                return new IndexStatistics
                {
                    EntryCount = 0,
                    UniqueValueCount = 0,
                    MemoryUsageBytes = 0
                };
            }

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var countCommand = connection.CreateCommand();
                countCommand.CommandText = $"SELECT COUNT(*) FROM {_tableName}";
                var entryCount = Convert.ToInt64(countCommand.ExecuteScalar());

                using var uniqueCommand = connection.CreateCommand();
                uniqueCommand.CommandText = $@"
                    SELECT COUNT(DISTINCT json_extract(payload_json, @jsonPath))
                    FROM {_tableName}";
                uniqueCommand.Parameters.AddWithValue("@jsonPath", JsonPath);
                var uniqueCount = Convert.ToInt64(uniqueCommand.ExecuteScalar());

                return new IndexStatistics
                {
                    EntryCount = entryCount,
                    UniqueValueCount = uniqueCount,
                    MemoryUsageBytes = 0, // Native indexes stored in DB, not memory
                    LastUpdated = DateTime.UtcNow
                };
            }
            catch
            {
                return new IndexStatistics
                {
                    EntryCount = 0,
                    UniqueValueCount = 0,
                    MemoryUsageBytes = 0
                };
            }
        }
    }
}
