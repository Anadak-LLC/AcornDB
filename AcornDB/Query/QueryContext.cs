using System;
using System.Linq.Expressions;

namespace AcornDB.Query
{
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
}
