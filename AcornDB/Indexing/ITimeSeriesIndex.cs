using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Time-series index interface - buckets timestamps for efficient range queries and aggregations.
    /// Useful for metrics, events, logs, and time-based analytics.
    /// Examples: CreatedAt, LastModified, EventTimestamp
    /// </summary>
    public interface ITimeSeriesIndex<T> : IIndex where T : class
    {
        /// <summary>
        /// Expression that extracts the timestamp from the document
        /// (e.g., event => event.Timestamp)
        /// </summary>
        Expression<Func<T, DateTime>> TimestampSelector { get; }

        /// <summary>
        /// Bucket size for time-series aggregation
        /// </summary>
        TimeSpan BucketSize { get; }

        /// <summary>
        /// Get all documents in a specific time range
        /// </summary>
        /// <param name="start">Start time (inclusive)</param>
        /// <param name="end">End time (inclusive)</param>
        /// <returns>Document IDs in chronological order</returns>
        IEnumerable<string> Range(DateTime start, DateTime end);

        /// <summary>
        /// Get document IDs grouped by time bucket
        /// </summary>
        /// <param name="start">Start time</param>
        /// <param name="end">End time</param>
        /// <returns>Buckets with document IDs</returns>
        IEnumerable<TimeBucket> GetBuckets(DateTime start, DateTime end);

        /// <summary>
        /// Get aggregated metrics for each time bucket
        /// </summary>
        /// <param name="start">Start time</param>
        /// <param name="end">End time</param>
        /// <param name="aggregateFunc">Function to aggregate documents in each bucket</param>
        /// <returns>Time buckets with aggregated values</returns>
        IEnumerable<TimeBucket<TAggregate>> Aggregate<TAggregate>(
            DateTime start,
            DateTime end,
            Func<IEnumerable<T>, TAggregate> aggregateFunc);

        /// <summary>
        /// Get documents after a specific timestamp (for incremental queries)
        /// </summary>
        /// <param name="after">Timestamp to search after</param>
        /// <returns>Document IDs in chronological order</returns>
        IEnumerable<string> After(DateTime after);

        /// <summary>
        /// Get documents before a specific timestamp
        /// </summary>
        /// <param name="before">Timestamp to search before</param>
        /// <returns>Document IDs in chronological order</returns>
        IEnumerable<string> Before(DateTime before);
    }
}
