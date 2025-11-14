using System;
using AcornDB;
using AcornDB.Models;

namespace AcornDB.Persistence.RDBMS
{
    /// <summary>
    /// Extension methods for Acorn builder to support RDBMS storage
    /// </summary>
    public static class AcornRdbmsExtensions
    {
        /// <summary>
        /// Use SQLite database storage
        /// </summary>
        /// <param name="databasePath">Path to SQLite database file (e.g., "./data/acorndb.db")</param>
        /// <param name="tableName">Optional custom table name. Default: acorn_{TypeName}</param>
        public static Acorn<T> WithSqlite<T>(
            this Acorn<T> acorn,
            string databasePath,
            string? tableName = null)
            where T : class
        {
            var sqliteTrunk = new SqliteTrunk<T>(databasePath, tableName);
            return acorn.WithTrunk(sqliteTrunk);
        }

        /// <summary>
        /// Use SQL Server database storage
        /// </summary>
        /// <param name="connectionString">SQL Server connection string</param>
        /// <param name="tableName">Optional custom table name. Default: Acorn_{TypeName}</param>
        /// <param name="schema">Database schema. Default: dbo</param>
        public static Acorn<T> WithSqlServer<T>(
            this Acorn<T> acorn,
            string connectionString,
            string? tableName = null,
            string schema = "dbo")
            where T : class
        {
            var sqlServerTrunk = new SqlServerTrunk<T>(connectionString, tableName, schema);
            return acorn.WithTrunk(sqlServerTrunk);
        }

        /// <summary>
        /// Use PostgreSQL database storage
        /// </summary>
        /// <param name="connectionString">PostgreSQL connection string</param>
        /// <param name="tableName">Optional custom table name. Default: acorn_{type_name}</param>
        /// <param name="schema">Database schema. Default: public</param>
        public static Acorn<T> WithPostgreSQL<T>(
            this Acorn<T> acorn,
            string connectionString,
            string? tableName = null,
            string schema = "public")
            where T : class
        {
            var postgresTrunk = new PostgreSqlTrunk<T>(connectionString, tableName, schema);
            return acorn.WithTrunk(postgresTrunk);
        }

        /// <summary>
        /// Use MySQL database storage
        /// </summary>
        /// <param name="connectionString">MySQL connection string</param>
        /// <param name="tableName">Optional custom table name. Default: acorn_{TypeName}</param>
        /// <param name="database">Database name (optional if included in connection string)</param>
        public static Acorn<T> WithMySQL<T>(
            this Acorn<T> acorn,
            string connectionString,
            string? tableName = null,
            string? database = null)
            where T : class
        {
            var mysqlTrunk = new MySqlTrunk<T>(connectionString, tableName, database);
            return acorn.WithTrunk(mysqlTrunk);
        }
    }
}
