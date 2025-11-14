using System;
using System.Collections.Generic;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Time bucket containing document IDs
    /// </summary>
    public class TimeBucket
    {
        /// <summary>
        /// Start of the time bucket
        /// </summary>
        public DateTime BucketStart { get; set; }

        /// <summary>
        /// End of the time bucket (exclusive)
        /// </summary>
        public DateTime BucketEnd { get; set; }

        /// <summary>
        /// Document IDs in this bucket
        /// </summary>
        public List<string> DocumentIds { get; set; } = new List<string>();

        /// <summary>
        /// Number of documents in this bucket
        /// </summary>
        public int Count => DocumentIds.Count;
    }

    /// <summary>
    /// Time bucket with aggregated value
    /// </summary>
    public class TimeBucket<TAggregate> : TimeBucket
    {
        /// <summary>
        /// Aggregated value for this bucket (count, sum, average, etc.)
        /// </summary>
        public TAggregate? AggregateValue { get; set; }
    }
}
