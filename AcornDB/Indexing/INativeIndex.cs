using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Represents a database-native index that delegates to the underlying storage engine.
    /// Unlike managed indexes which maintain their own data structures in memory,
    /// native indexes use CREATE INDEX statements and query the database directly.
    /// </summary>
    public interface INativeIndex : IIndex
    {
        /// <summary>
        /// The DDL statement used to create this index in the database.
        /// Example: "CREATE INDEX idx_email ON acorn_user(json_extract(json_data, '$.Email'))"
        /// </summary>
        string CreateIndexDdl { get; }

        /// <summary>
        /// The DDL statement used to drop this index from the database.
        /// Example: "DROP INDEX IF EXISTS idx_email"
        /// </summary>
        string DropIndexDdl { get; }

        /// <summary>
        /// Whether this index has been created in the database.
        /// </summary>
        bool IsCreated { get; }

        /// <summary>
        /// Create the index in the database by executing the CREATE INDEX statement.
        /// This is idempotent - calling multiple times should be safe.
        /// </summary>
        void CreateInDatabase();

        /// <summary>
        /// Drop the index from the database by executing the DROP INDEX statement.
        /// This is idempotent - calling multiple times should be safe.
        /// </summary>
        void DropFromDatabase();

        /// <summary>
        /// Verify that the index exists and is valid in the database.
        /// Returns true if the index exists, false otherwise.
        /// </summary>
        bool VerifyInDatabase();
    }
}
