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
}
