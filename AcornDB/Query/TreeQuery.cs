using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace AcornDB.Query
{
    /// <summary>
    /// LINQ-style query interface for AcornDB Trees
    /// Provides fluent API for filtering, ordering, and projecting tree data
    /// </summary>
    public class TreeQuery<T> where T : class
    {
        private readonly Tree<T> _tree;
        private IEnumerable<Nut<T>> _source;
        private Func<Nut<T>, bool>? _whereClause;
        private Expression<Func<T, bool>>? _whereExpression;
        private Func<Nut<T>, object>? _orderByClause;
        private LambdaExpression? _orderByExpression;
        private bool _orderDescending = false;
        private int? _takeCount;
        private int? _skipCount;
        private string? _indexHint;

        internal TreeQuery(Tree<T> tree)
        {
            _tree = tree;
            _source = tree.GetAllNuts();
        }

        /// <summary>
        /// Filter nuts by predicate on the payload
        /// </summary>
        public TreeQuery<T> Where(Expression<Func<T, bool>> predicate)
        {
            _whereExpression = predicate;
            var compiled = predicate.Compile();
            _whereClause = nut => compiled(nut.Payload);
            return this;
        }

        /// <summary>
        /// Filter nuts by predicate on the entire nut (payload + metadata)
        /// </summary>
        public TreeQuery<T> WhereNut(Func<Nut<T>, bool> predicate)
        {
            _whereClause = predicate;
            return this;
        }

        /// <summary>
        /// Order results by a key selector
        /// </summary>
        public TreeQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            _orderByExpression = keySelector;
            var compiled = keySelector.Compile();
            _orderByClause = nut => compiled(nut.Payload)!;
            _orderDescending = false;
            return this;
        }

        /// <summary>
        /// Order results by a key selector (descending)
        /// </summary>
        public TreeQuery<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            _orderByExpression = keySelector;
            var compiled = keySelector.Compile();
            _orderByClause = nut => compiled(nut.Payload)!;
            _orderDescending = true;
            return this;
        }

        /// <summary>
        /// Order by timestamp (newest first)
        /// </summary>
        public TreeQuery<T> Newest()
        {
            _orderByClause = nut => nut.Timestamp;
            _orderDescending = true;
            return this;
        }

        /// <summary>
        /// Order by timestamp (oldest first)
        /// </summary>
        public TreeQuery<T> Oldest()
        {
            _orderByClause = nut => nut.Timestamp;
            _orderDescending = false;
            return this;
        }

        /// <summary>
        /// Take only the first N results
        /// </summary>
        public TreeQuery<T> Take(int count)
        {
            _takeCount = count;
            return this;
        }

        /// <summary>
        /// Skip the first N results
        /// </summary>
        public TreeQuery<T> Skip(int count)
        {
            _skipCount = count;
            return this;
        }

        /// <summary>
        /// Filter by timestamp range
        /// </summary>
        public TreeQuery<T> Between(DateTime start, DateTime end)
        {
            _whereClause = nut => nut.Timestamp >= start && nut.Timestamp <= end;
            return this;
        }

        /// <summary>
        /// Filter by items created after a specific date
        /// </summary>
        public TreeQuery<T> After(DateTime date)
        {
            _whereClause = nut => nut.Timestamp > date;
            return this;
        }

        /// <summary>
        /// Filter by items created before a specific date
        /// </summary>
        public TreeQuery<T> Before(DateTime date)
        {
            _whereClause = nut => nut.Timestamp < date;
            return this;
        }

        /// <summary>
        /// Filter by origin node
        /// </summary>
        public TreeQuery<T> FromNode(string nodeId)
        {
            _whereClause = nut => nut.OriginNodeId == nodeId;
            return this;
        }

        /// <summary>
        /// Execute query and return results
        /// </summary>
        public List<T> ToList()
        {
            return ExecuteQuery().Select(nut => nut.Payload).ToList();
        }

        /// <summary>
        /// Execute query and return nuts (with metadata)
        /// </summary>
        public List<Nut<T>> ToNutList()
        {
            return ExecuteQuery().ToList();
        }

        /// <summary>
        /// Execute query and return first result or null
        /// </summary>
        public T? FirstOrDefault()
        {
            return ExecuteQuery().Select(nut => nut.Payload).FirstOrDefault();
        }

        /// <summary>
        /// Execute query and return first nut or null
        /// </summary>
        public Nut<T>? FirstNutOrDefault()
        {
            return ExecuteQuery().FirstOrDefault();
        }

        /// <summary>
        /// Execute query and return single result (throws if multiple)
        /// </summary>
        public T? SingleOrDefault()
        {
            return ExecuteQuery().Select(nut => nut.Payload).SingleOrDefault();
        }

        /// <summary>
        /// Count results without materializing them
        /// </summary>
        public int Count()
        {
            return ExecuteQuery().Count();
        }

        /// <summary>
        /// Check if any results match the query
        /// </summary>
        public bool Any()
        {
            return ExecuteQuery().Any();
        }

        /// <summary>
        /// Execute query and return as enumerable (deferred execution)
        /// </summary>
        public IEnumerable<T> AsEnumerable()
        {
            return ExecuteQuery().Select(nut => nut.Payload);
        }

        /// <summary>
        /// Provide a hint to use a specific index
        /// </summary>
        public TreeQuery<T> UseIndex(string indexName)
        {
            _indexHint = indexName;
            return this;
        }

        /// <summary>
        /// Get the query execution plan without executing the query.
        /// Useful for understanding query performance and index usage.
        /// </summary>
        public QueryPlan<T> Explain()
        {
            var planner = new DefaultQueryPlanner<T>(_tree);
            var context = CreateQueryContext();
            return planner.CreatePlan(context);
        }

        /// <summary>
        /// Get the query execution plan as a formatted string.
        /// </summary>
        public string ExplainString()
        {
            var plan = Explain();
            return FormatQueryPlan(plan);
        }

        private QueryContext<T> CreateQueryContext()
        {
            return new QueryContext<T>
            {
                WherePredicate = _whereClause,
                WhereExpression = _whereExpression,
                OrderBySelector = _orderByClause,
                OrderByExpression = _orderByExpression,
                OrderDescending = _orderDescending,
                Take = _takeCount,
                Skip = _skipCount,
                IndexHint = _indexHint
            };
        }

        private string FormatQueryPlan(QueryPlan<T> plan)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Query Execution Plan ===");
            sb.AppendLine($"Strategy: {plan.Strategy}");
            sb.AppendLine($"Selected Index: {plan.SelectedIndex?.Name ?? "None (Full Scan)"}");
            sb.AppendLine($"Estimated Cost: {plan.EstimatedCost:F2}");
            sb.AppendLine($"Estimated Rows Examined: {plan.EstimatedRowsExamined}");
            sb.AppendLine($"Estimated Rows Returned: {plan.EstimatedRowsReturned}");
            sb.AppendLine($"Explanation: {plan.Explanation}");

            if (plan.Candidates.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Index Candidates Considered:");
                foreach (var candidate in plan.Candidates)
                {
                    var selected = candidate.Index.Name == plan.SelectedIndex?.Name ? " [SELECTED]" : "";
                    sb.AppendLine($"  - {candidate.Index.Name}{selected}");
                    sb.AppendLine($"    Type: {candidate.Index.IndexType}");
                    sb.AppendLine($"    Cost: {candidate.EstimatedCost:F2}");
                    sb.AppendLine($"    Reason: {candidate.Reason}");
                }
            }

            return sb.ToString();
        }

        private IEnumerable<Nut<T>> ExecuteQuery()
        {
            // Try to use query planner for index-aware execution
            // Only use planner if we have indexes to potentially use
            var availableIndexes = _tree.GetAllIndexes().ToList();
            if (availableIndexes.Any())
            {
                try
                {
                    var planner = new DefaultQueryPlanner<T>(_tree);
                    var context = CreateQueryContext();
                    var plan = planner.CreatePlan(context);

                    // Use planner execution if it selected an index
                    if (plan.SelectedIndex != null && plan.Strategy != QueryStrategy.FullScan)
                    {
                        return planner.Execute(plan);
                    }
                }
                catch
                {
                    // Fall back to manual execution if planner fails
                }
            }

            // Fall back to manual LINQ-based execution
            IEnumerable<Nut<T>> query = _source;

            // Apply where clause
            if (_whereClause != null)
            {
                query = query.Where(_whereClause);
            }

            // Apply ordering
            if (_orderByClause != null)
            {
                query = _orderDescending
                    ? query.OrderByDescending(_orderByClause)
                    : query.OrderBy(_orderByClause);
            }

            // Apply skip
            if (_skipCount.HasValue)
            {
                query = query.Skip(_skipCount.Value);
            }

            // Apply take
            if (_takeCount.HasValue)
            {
                query = query.Take(_takeCount.Value);
            }

            return query;
        }
    }

    /// <summary>
    /// Extension methods to enable query syntax on Trees
    /// </summary>
    public static class TreeQueryExtensions
    {
        /// <summary>
        /// Start a LINQ-style query on this tree
        /// </summary>
        public static TreeQuery<T> Query<T>(this Tree<T> tree) where T : class
        {
            return new TreeQuery<T>(tree);
        }

        /// <summary>
        /// Get all nuts from the tree (internal helper)
        /// </summary>
        internal static IEnumerable<Nut<T>> GetAllNuts<T>(this Tree<T> tree) where T : class
        {
            // Use the public GetAllNuts() method we just added to Tree
            return tree.GetAllNuts();
        }
    }
}
