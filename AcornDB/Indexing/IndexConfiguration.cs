using System;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Configuration options for index creation.
    /// Uses fluent API for easy configuration:
    /// new IndexConfiguration().Unique().CaseInsensitive().Name("IX_Custom")
    /// </summary>
    public class IndexConfiguration
    {
        /// <summary>
        /// Custom name for the index. If null, a name will be auto-generated.
        /// </summary>
        public string? Name { get; private set; }

        /// <summary>
        /// Whether the index enforces uniqueness constraint
        /// </summary>
        public bool IsUnique { get; private set; }

        /// <summary>
        /// Whether string comparisons should be case-insensitive (only applies to string indexes)
        /// </summary>
        public bool CaseInsensitive { get; private set; }

        /// <summary>
        /// Whether to build the index online (allows writes during build)
        /// </summary>
        public bool BuildOnline { get; private set; }

        /// <summary>
        /// Language for text indexes (e.g., "english", "spanish")
        /// </summary>
        public string Language { get; private set; } = "english";

        /// <summary>
        /// Bucket size for time-series indexes
        /// </summary>
        public TimeSpan? TimeBucketSize { get; private set; }

        /// <summary>
        /// Set a custom name for the index
        /// </summary>
        public IndexConfiguration WithName(string name)
        {
            Name = name;
            return this;
        }

        /// <summary>
        /// Mark the index as unique (enforce uniqueness constraint)
        /// </summary>
        public IndexConfiguration Unique()
        {
            IsUnique = true;
            return this;
        }

        /// <summary>
        /// Enable case-insensitive string comparisons
        /// </summary>
        public IndexConfiguration WithCaseInsensitiveComparison()
        {
            CaseInsensitive = true;
            return this;
        }

        /// <summary>
        /// Build the index online (allows concurrent writes)
        /// </summary>
        public IndexConfiguration Online()
        {
            BuildOnline = true;
            return this;
        }

        /// <summary>
        /// Set the language for text indexes (for tokenization and stemming)
        /// </summary>
        public IndexConfiguration WithLanguage(string language)
        {
            Language = language;
            return this;
        }

        /// <summary>
        /// Set the bucket size for time-series indexes
        /// </summary>
        public IndexConfiguration BucketHours(int hours)
        {
            TimeBucketSize = TimeSpan.FromHours(hours);
            return this;
        }

        /// <summary>
        /// Set the bucket size for time-series indexes
        /// </summary>
        public IndexConfiguration BucketMinutes(int minutes)
        {
            TimeBucketSize = TimeSpan.FromMinutes(minutes);
            return this;
        }

        /// <summary>
        /// Set the bucket size for time-series indexes
        /// </summary>
        public IndexConfiguration BucketDays(int days)
        {
            TimeBucketSize = TimeSpan.FromDays(days);
            return this;
        }

        /// <summary>
        /// Set a custom bucket size for time-series indexes
        /// </summary>
        public IndexConfiguration BucketSize(TimeSpan bucketSize)
        {
            TimeBucketSize = bucketSize;
            return this;
        }
    }
}
