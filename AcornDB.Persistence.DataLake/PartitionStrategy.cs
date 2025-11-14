using System;
using System.Collections.Generic;
using System.Linq;
using AcornDB;

namespace AcornDB.Persistence.DataLake
{
    /// <summary>
    /// Partition strategies for data lake organization
    /// </summary>
    public static class PartitionStrategy
    {
        /// <summary>
        /// Partition by date using Timestamp field
        /// </summary>
        /// <param name="format">Date format (e.g., "yyyy/MM/dd" or "yyyy-MM")</param>
        public static IPartitionStrategy ByDate(string format = "yyyy/MM/dd")
        {
            return new DatePartitionStrategy(format);
        }

        /// <summary>
        /// Partition by year/month/day (Hive-style: year=2025/month=10/day=14)
        /// </summary>
        public static IPartitionStrategy ByYearMonthDay()
        {
            return new HiveDatePartitionStrategy();
        }

        /// <summary>
        /// Partition by value from payload property
        /// </summary>
        /// <param name="propertySelector">Property selector function</param>
        public static IPartitionStrategy ByValue<T>(Func<T, string> propertySelector)
        {
            return new ValuePartitionStrategy<T>(propertySelector);
        }

        /// <summary>
        /// Composite partitioning (e.g., date + value)
        /// </summary>
        public static IPartitionStrategy Composite(params IPartitionStrategy[] strategies)
        {
            return new CompositePartitionStrategy(strategies);
        }
    }

    /// <summary>
    /// Date-based partitioning
    /// </summary>
    internal class DatePartitionStrategy : IPartitionStrategy
    {
        private readonly string _format;

        public DatePartitionStrategy(string format)
        {
            _format = format;
        }

        public string GetPartitionPath<T>(Nut<T> nut)
        {
            return nut.Timestamp.ToString(_format).Replace('-', '/');
        }
    }

    /// <summary>
    /// Hive-style date partitioning (year=2025/month=10/day=14)
    /// </summary>
    internal class HiveDatePartitionStrategy : IPartitionStrategy
    {
        public string GetPartitionPath<T>(Nut<T> nut)
        {
            return $"year={nut.Timestamp.Year}/month={nut.Timestamp.Month:D2}/day={nut.Timestamp.Day:D2}";
        }
    }

    /// <summary>
    /// Value-based partitioning from payload property
    /// </summary>
    internal class ValuePartitionStrategy<TPayload> : IPartitionStrategy
    {
        private readonly Func<TPayload, string> _propertySelector;

        public ValuePartitionStrategy(Func<TPayload, string> propertySelector)
        {
            _propertySelector = propertySelector;
        }

        public string GetPartitionPath<T>(Nut<T> nut)
        {
            if (nut.Payload is TPayload payload)
            {
                var value = _propertySelector(payload);
                return SanitizePartitionValue(value);
            }

            return "unknown";
        }

        private string SanitizePartitionValue(string value)
        {
            // Replace invalid path characters
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            return string.Join("_", value.Split(invalid));
        }
    }

    /// <summary>
    /// Composite partitioning (combines multiple strategies)
    /// </summary>
    internal class CompositePartitionStrategy : IPartitionStrategy
    {
        private readonly IPartitionStrategy[] _strategies;

        public CompositePartitionStrategy(IPartitionStrategy[] strategies)
        {
            _strategies = strategies;
        }

        public string GetPartitionPath<T>(Nut<T> nut)
        {
            var paths = _strategies.Select(s => s.GetPartitionPath(nut));
            return string.Join("/", paths);
        }
    }
}
