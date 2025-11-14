using AcornDB;

namespace AcornDB.Persistence.DataLake
{
    /// <summary>
    /// Interface for partition strategies in data lakes
    /// </summary>
    public interface IPartitionStrategy
    {
        /// <summary>
        /// Get partition path for a nut (e.g., "year=2025/month=10/day=14")
        /// </summary>
        string GetPartitionPath<T>(Nut<T> nut);
    }
}
