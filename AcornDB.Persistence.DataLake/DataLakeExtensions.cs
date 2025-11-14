using System;
using System.Linq;
using System.Threading.Tasks;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Persistence.Cloud;
using AcornDB.Sync;

namespace AcornDB.Persistence.DataLake
{
    /// <summary>
    /// Extension methods for data lake integration
    /// </summary>
    public static class DataLakeExtensions
    {
        /// <summary>
        /// Export trunk data to Parquet in data lake
        /// </summary>
        public static async Task ExportToParquet<T>(
            this ITrunk<T> sourceTrunk,
            string path,
            ParquetOptions? options = null) where T : class
        {
            var parquetTrunk = new ParquetTrunk<T>(path, options);
            var nuts = sourceTrunk.CrackAll();
            await parquetTrunk.ImportChangesAsync(nuts);
        }

        /// <summary>
        /// Export trunk data to cloud data lake (S3, Azure Data Lake)
        /// </summary>
        public static async Task ExportToParquet<T>(
            this ITrunk<T> sourceTrunk,
            string path,
            ICloudStorageProvider cloudStorage,
            ParquetOptions? options = null) where T : class
        {
            var parquetTrunk = new ParquetTrunk<T>(path, cloudStorage, options);
            var nuts = sourceTrunk.CrackAll();
            await parquetTrunk.ImportChangesAsync(nuts);
        }

        /// <summary>
        /// Import data from Parquet data lake into trunk
        /// </summary>
        public static async Task ImportFromParquet<T>(
            this ITrunk<T> targetTrunk,
            string path,
            ParquetOptions? options = null) where T : class
        {
            var parquetTrunk = new ParquetTrunk<T>(path, options);
            var nuts = await parquetTrunk.CrackAllAsync();
            targetTrunk.ImportChanges(nuts);
        }

        /// <summary>
        /// Import data from cloud data lake (S3, Azure Data Lake)
        /// </summary>
        public static async Task ImportFromParquet<T>(
            this ITrunk<T> targetTrunk,
            string path,
            ICloudStorageProvider cloudStorage,
            ParquetOptions? options = null) where T : class
        {
            var parquetTrunk = new ParquetTrunk<T>(path, cloudStorage, options);
            var nuts = await parquetTrunk.CrackAllAsync();
            targetTrunk.ImportChanges(nuts);
        }

        /// <summary>
        /// Create bidirectional sync with data lake
        /// </summary>
        public static Tangle<T> SyncWithDataLake<T>(
            this Tree<T> tree,
            string path,
            ParquetOptions? options = null) where T : class
        {
            var parquetTrunk = new ParquetTrunk<T>(path, options);
            var dataLakeTree = new Tree<T>(parquetTrunk);

            return tree.Entangle(dataLakeTree);
        }

        /// <summary>
        /// Create bidirectional sync with cloud data lake
        /// </summary>
        public static Tangle<T> SyncWithDataLake<T>(
            this Tree<T> tree,
            string path,
            ICloudStorageProvider cloudStorage,
            ParquetOptions? options = null) where T : class
        {
            var parquetTrunk = new ParquetTrunk<T>(path, cloudStorage, options);
            var dataLakeTree = new Tree<T>(parquetTrunk);

            return tree.Entangle(dataLakeTree);
        }

        /// <summary>
        /// Create tiered trunk with hot BTree and cold Parquet storage
        /// </summary>
        /// <param name="hotPath">Path for hot BTree storage</param>
        /// <param name="coldPath">Path for cold Parquet storage</param>
        /// <param name="tieringOptions">Tiering options (archive after X days)</param>
        /// <returns>Tiered trunk</returns>
        public static TieredTrunk<T> CreateTieredStorage<T>(
            string hotPath,
            string coldPath,
            TieringOptions<T>? tieringOptions = null) where T : class
        {
            var hotTrunk = new BTreeTrunk<T>(hotPath);
            var coldTrunk = new ParquetTrunk<T>(coldPath);
            return new TieredTrunk<T>(hotTrunk, coldTrunk, tieringOptions);
        }

        /// <summary>
        /// Create complete caching + tiering architecture:
        /// Near cache (memory) → Far cache (Redis) → Hot tier (BTree) → Cold tier (Parquet)
        /// </summary>
        /// <param name="farCache">Distributed cache (e.g., RedisTrunk)</param>
        /// <param name="hotPath">Path for hot BTree storage</param>
        /// <param name="coldPath">Path for cold Parquet storage</param>
        /// <param name="nearFarOptions">Near/far caching options</param>
        /// <param name="tieringOptions">Tiering options</param>
        /// <returns>Fully optimized trunk</returns>
        public static NearFarTrunk<T> CreateFullStack<T>(
            ITrunk<T> farCache,
            string hotPath,
            string coldPath,
            NearFarOptions? nearFarOptions = null,
            TieringOptions<T>? tieringOptions = null) where T : class
        {
            // Create tiered backing store (hot + cold)
            var tieredStore = CreateTieredStorage<T>(hotPath, coldPath, tieringOptions);

            // Wrap with near/far caching
            return tieredStore.WithNearFarCache(farCache, nearFarOptions);
        }
    }
}
