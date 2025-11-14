using System.Collections.Generic;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Result from a text search with relevance ranking
    /// </summary>
    public class TextSearchResult
    {
        /// <summary>
        /// Document ID
        /// </summary>
        public string DocumentId { get; set; } = string.Empty;

        /// <summary>
        /// Relevance score (higher = more relevant)
        /// Calculated using TF-IDF or similar algorithm
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Matching tokens/terms found in this document
        /// </summary>
        public List<string> MatchedTerms { get; set; } = new List<string>();
    }
}
