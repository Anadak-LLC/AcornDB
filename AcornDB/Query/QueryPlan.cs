using System.Collections.Generic;
using AcornDB.Indexing;

namespace AcornDB.Query
{
    /// <summary>
    /// Execution plan created by the query planner
    /// </summary>
    public class QueryPlan<T>
    {
        /// <summary>
        /// Selected index to use (null = full scan)
        /// </summary>
        public IIndex? SelectedIndex { get; set; }

        /// <summary>
        /// Strategy for executing the query
        /// </summary>
        public QueryStrategy Strategy { get; set; }

        /// <summary>
        /// Estimated cost of this plan (lower is better)
        /// </summary>
        public double EstimatedCost { get; set; }

        /// <summary>
        /// Estimated number of rows to examine
        /// </summary>
        public long EstimatedRowsExamined { get; set; }

        /// <summary>
        /// Estimated number of rows to return
        /// </summary>
        public long EstimatedRowsReturned { get; set; }

        /// <summary>
        /// Original query context
        /// </summary>
        public QueryContext<T> Context { get; set; } = new QueryContext<T>();

        /// <summary>
        /// Explanation of why this plan was chosen
        /// </summary>
        public string Explanation { get; set; } = string.Empty;

        /// <summary>
        /// All candidate indexes that were considered
        /// </summary>
        public List<IndexCandidate> Candidates { get; set; } = new List<IndexCandidate>();
    }
}
