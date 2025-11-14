using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Full-text search index interface - tokenizes text for efficient searching.
    /// Supports word matching, prefix matching, and ranking.
    /// Examples: Bio, Description, Comments
    /// </summary>
    public interface ITextIndex<T> : IIndex where T : class
    {
        /// <summary>
        /// Expression that extracts the text to index from the document
        /// (e.g., product => product.Description)
        /// </summary>
        Expression<Func<T, string>> TextSelector { get; }

        /// <summary>
        /// Language for tokenization and stemming (e.g., "english", "spanish")
        /// </summary>
        string Language { get; }

        /// <summary>
        /// Search for documents containing the given search term(s).
        /// Supports multiple words, phrase matching, and boolean operators.
        /// </summary>
        /// <param name="searchQuery">Search query (e.g., "laptop computer", "\"gaming laptop\"", "laptop OR tablet")</param>
        /// <returns>Document IDs ranked by relevance</returns>
        IEnumerable<TextSearchResult> Search(string searchQuery);

        /// <summary>
        /// Prefix search: find documents containing words starting with the prefix.
        /// Useful for autocomplete/typeahead.
        /// </summary>
        /// <param name="prefix">Word prefix to search for</param>
        /// <returns>Document IDs containing words with this prefix</returns>
        IEnumerable<string> PrefixSearch(string prefix);

        /// <summary>
        /// Get all unique tokens in the index (for debugging/analysis)
        /// </summary>
        IEnumerable<string> GetAllTokens();
    }
}
