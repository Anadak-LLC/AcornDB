using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using AcornDB.Indexing;

namespace AcornDB.Query
{
    /// <summary>
    /// Query planner that analyzes queries and determines the best execution strategy.
    /// Responsible for index selection, cost estimation, and query optimization.
    /// </summary>
    public interface IQueryPlanner<T>
    {
        /// <summary>
        /// Analyze a query and create an execution plan
        /// </summary>
        /// <param name="queryContext">Context containing query predicates, ordering, etc.</param>
        /// <returns>Execution plan with selected indexes and estimated cost</returns>
        QueryPlan<T> CreatePlan(QueryContext<T> queryContext);

        /// <summary>
        /// Execute a query plan and return results
        /// </summary>
        /// <param name="plan">Pre-analyzed execution plan</param>
        /// <returns>Query results</returns>
        IEnumerable<Nut<T>> Execute(QueryPlan<T> plan);

        /// <summary>
        /// Get available indexes for planning
        /// </summary>
        IReadOnlyList<IIndex> AvailableIndexes { get; }
    }

    /// <summary>
    /// Context object containing information about a query
    /// </summary>
    public class QueryContext<T>
    {
        /// <summary>
        /// WHERE predicate (if any)
        /// </summary>
        public Func<Nut<T>, bool>? WherePredicate { get; set; }

        /// <summary>
        /// WHERE expression (for analysis)
        /// </summary>
        public Expression<Func<T, bool>>? WhereExpression { get; set; }

        /// <summary>
        /// ORDER BY key selector
        /// </summary>
        public Func<Nut<T>, object>? OrderBySelector { get; set; }

        /// <summary>
        /// ORDER BY expression (for analysis)
        /// </summary>
        public LambdaExpression? OrderByExpression { get; set; }

        /// <summary>
        /// Descending order flag
        /// </summary>
        public bool OrderDescending { get; set; }

        /// <summary>
        /// Take count (LIMIT)
        /// </summary>
        public int? Take { get; set; }

        /// <summary>
        /// Skip count (OFFSET)
        /// </summary>
        public int? Skip { get; set; }

        /// <summary>
        /// Hint: specific index to use (overrides planner)
        /// </summary>
        public string? IndexHint { get; set; }

        /// <summary>
        /// Capture timestamp for query tracking
        /// </summary>
        public DateTime QueryTimestamp { get; set; } = DateTime.UtcNow;
    }

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

    /// <summary>
    /// Strategy for executing a query
    /// </summary>
    public enum QueryStrategy
    {
        /// <summary>
        /// Full table/cache scan (no index)
        /// </summary>
        FullScan,

        /// <summary>
        /// Index lookup (exact match)
        /// </summary>
        IndexSeek,

        /// <summary>
        /// Index range scan
        /// </summary>
        IndexRangeScan,

        /// <summary>
        /// Index scan (use index for ordering only)
        /// </summary>
        IndexScan,

        /// <summary>
        /// Multiple index merge
        /// </summary>
        IndexMerge
    }

    /// <summary>
    /// Candidate index considered during planning
    /// </summary>
    public class IndexCandidate
    {
        /// <summary>
        /// The index being considered
        /// </summary>
        public IIndex Index { get; set; } = null!;

        /// <summary>
        /// Estimated cost if this index is used
        /// </summary>
        public double EstimatedCost { get; set; }

        /// <summary>
        /// Why this index was/wasn't selected
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Can this index satisfy the WHERE clause?
        /// </summary>
        public bool CanSatisfyWhere { get; set; }

        /// <summary>
        /// Can this index satisfy the ORDER BY clause?
        /// </summary>
        public bool CanSatisfyOrderBy { get; set; }
    }
}
