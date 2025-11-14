using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using AcornDB.Indexing;

namespace AcornDB.Query
{
    /// <summary>
    /// Default query planner implementation with cost-based optimization.
    /// Selects the best index based on selectivity, cardinality, and query structure.
    /// </summary>
    public class DefaultQueryPlanner<T> : IQueryPlanner<T> where T : class
    {
        private readonly Tree<T> _tree;
        private readonly List<IIndex> _indexes;

        public IReadOnlyList<IIndex> AvailableIndexes => _indexes.AsReadOnly();

        public DefaultQueryPlanner(Tree<T> tree)
        {
            _tree = tree ?? throw new ArgumentNullException(nameof(tree));
            _indexes = tree.GetAllIndexes().ToList();
        }

        public QueryPlan<T> CreatePlan(QueryContext<T> queryContext)
        {
            var plan = new QueryPlan<T>
            {
                Context = queryContext,
                Strategy = QueryStrategy.FullScan,
                EstimatedCost = double.MaxValue
            };

            // If index hint is provided, try to use it
            if (!string.IsNullOrEmpty(queryContext.IndexHint))
            {
                var hintedIndex = _tree.GetIndex(queryContext.IndexHint);
                if (hintedIndex != null)
                {
                    plan.SelectedIndex = hintedIndex;
                    plan.Strategy = QueryStrategy.IndexSeek;
                    plan.EstimatedCost = 1.0; // Assume hint is correct
                    plan.Explanation = $"Using hinted index: {queryContext.IndexHint}";
                    return plan;
                }
            }

            // Analyze all available indexes
            var candidates = new List<IndexCandidate>();

            foreach (var index in _indexes)
            {
                var candidate = AnalyzeIndex(index, queryContext);
                candidates.Add(candidate);

                // Update plan if this candidate is better
                if (candidate.EstimatedCost < plan.EstimatedCost)
                {
                    plan.SelectedIndex = index;
                    plan.Strategy = DetermineStrategy(candidate);
                    plan.EstimatedCost = candidate.EstimatedCost;
                    plan.Explanation = candidate.Reason;
                }
            }

            plan.Candidates = candidates;

            // Fall back to full scan if no good index found
            if (plan.SelectedIndex == null)
            {
                plan.Strategy = QueryStrategy.FullScan;
                plan.EstimatedRowsExamined = _tree.NutCount;
                plan.EstimatedRowsReturned = queryContext.Take ?? _tree.NutCount;
                plan.Explanation = "No suitable index found, performing full cache scan";
            }

            return plan;
        }

        public IEnumerable<Nut<T>> Execute(QueryPlan<T> plan)
        {
            IEnumerable<Nut<T>> results;

            switch (plan.Strategy)
            {
                case QueryStrategy.IndexSeek:
                case QueryStrategy.IndexRangeScan:
                case QueryStrategy.IndexScan:
                    results = ExecuteIndexQuery(plan);
                    break;

                case QueryStrategy.FullScan:
                default:
                    results = ExecuteFullScan(plan);
                    break;
            }

            // Apply ordering if needed and not provided by index
            if (plan.Context.OrderBySelector != null && plan.Strategy == QueryStrategy.FullScan)
            {
                results = plan.Context.OrderDescending
                    ? results.OrderByDescending(plan.Context.OrderBySelector)
                    : results.OrderBy(plan.Context.OrderBySelector);
            }

            // Apply skip/take
            if (plan.Context.Skip.HasValue)
            {
                results = results.Skip(plan.Context.Skip.Value);
            }

            if (plan.Context.Take.HasValue)
            {
                results = results.Take(plan.Context.Take.Value);
            }

            return results;
        }

        private IndexCandidate AnalyzeIndex(IIndex index, QueryContext<T> queryContext)
        {
            var candidate = new IndexCandidate
            {
                Index = index,
                EstimatedCost = double.MaxValue,
                Reason = "Not applicable"
            };

            var stats = index.GetStatistics();

            // Use expression analyzer to check if WHERE clause matches this index
            string? wherePropertyName = null;
            if (queryContext.WhereExpression != null)
            {
                var analyzer = new ExpressionAnalyzer<T>();
                var analysis = analyzer.Analyze(queryContext.WhereExpression);

                if (analysis.IsIndexable && analysis.Conditions.Any())
                {
                    // For now, take the first condition
                    wherePropertyName = analysis.Conditions[0].PropertyName;
                }
            }

            // Use expression analyzer to check if ORDER BY matches this index
            string? orderByPropertyName = null;
            if (queryContext.OrderByExpression != null)
            {
                var analyzer = new ExpressionAnalyzer<T>();

                // Try to extract property name from the ORDER BY expression
                if (queryContext.OrderByExpression is Expression<Func<T, object>> objExpr)
                {
                    var orderInfo = analyzer.AnalyzeOrderBy(objExpr);
                    orderByPropertyName = orderInfo?.PropertyName;
                }
                else
                {
                    // Handle generic OrderBy expressions using reflection
                    var method = typeof(ExpressionAnalyzer<T>).GetMethod("AnalyzeOrderBy");
                    if (method != null)
                    {
                        var genericMethod = method.MakeGenericMethod(queryContext.OrderByExpression.ReturnType);
                        var result = genericMethod.Invoke(analyzer, new object[] { queryContext.OrderByExpression });
                        if (result is PropertyAccessInfo propInfo)
                        {
                            orderByPropertyName = propInfo.PropertyName;
                        }
                    }
                }
            }

            // Check if this index matches the WHERE property
            bool matchesWhere = false;
            bool matchesOrderBy = false;

            // Extract property name from scalar index using reflection (since we can't cast to specific generic type)
            if (index.IndexType == IndexType.Scalar)
            {
                var indexType = index.GetType();
                var interfaces = indexType.GetInterfaces();
                var scalarInterface = interfaces.FirstOrDefault(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IScalarIndex<,>));

                if (scalarInterface != null)
                {
                    var propertySelectorProperty = scalarInterface.GetProperty("PropertySelector");
                    if (propertySelectorProperty != null)
                    {
                        var propertySelector = propertySelectorProperty.GetValue(index) as LambdaExpression;
                        if (propertySelector != null)
                        {
                            var indexPropertyName = ExtractPropertyName(propertySelector);
                            if (indexPropertyName != null)
                            {
                                matchesWhere = wherePropertyName == indexPropertyName;
                                matchesOrderBy = orderByPropertyName == indexPropertyName;
                            }
                        }
                    }
                }
            }

            // Identity index is great for ID lookups
            if (index.IndexType == IndexType.Identity)
            {
                bool matchesIdProperty = wherePropertyName == "Id" || wherePropertyName == "ID";
                candidate.CanSatisfyWhere = matchesIdProperty;

                if (matchesIdProperty)
                {
                    candidate.EstimatedCost = 1.0; // O(1) lookup
                    candidate.Reason = "Identity index: fast O(1) lookup by ID";
                }
                else
                {
                    // Identity index doesn't help with this query
                    candidate.EstimatedCost = stats.EntryCount; // Full scan cost
                    candidate.Reason = "Identity index: does not match query property";
                }
            }
            // Scalar indexes can help with equality and range queries
            else if (index.IndexType == IndexType.Scalar && matchesWhere)
            {
                // This index matches the WHERE clause property
                var selectivity = stats.Selectivity;
                var estimatedRows = (long)(stats.EntryCount * (1.0 - selectivity));

                candidate.EstimatedCost = Math.Log(stats.EntryCount + 1, 2); // O(log n) lookup
                candidate.CanSatisfyWhere = true;
                candidate.Reason = $"Scalar index matches WHERE clause, selectivity {selectivity:P2}, estimated {estimatedRows} rows";
            }
            else if (index.IndexType == IndexType.Scalar)
            {
                // Index doesn't match WHERE but might help with other things
                candidate.EstimatedCost = stats.EntryCount; // Full scan cost
                candidate.Reason = $"Scalar index does not match query predicates";
            }

            // Check if index can help with ORDER BY
            if (matchesOrderBy)
            {
                candidate.CanSatisfyOrderBy = true;
                // Reduce cost significantly if ordering can be satisfied
                candidate.EstimatedCost *= 0.3;
                candidate.Reason += " + provides sorted results";
            }

            // Prefer native indexes - they're maintained by the database engine
            // Native indexes typically have better performance than managed indexes
            if (IsNativeIndex(index))
            {
                candidate.EstimatedCost *= 0.5; // Reduce cost by 50% to prefer native
                candidate.Reason += " (native DB index)";
            }

            return candidate;
        }

        private bool IsNativeIndex(IIndex index)
        {
            // Check if the index implements INativeIndex interface
            var indexType = index.GetType();
            var interfaces = indexType.GetInterfaces();

            return interfaces.Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition().Name.Contains("INativeIndex") ||
                i.Name == "INativeIndex");
        }

        private string? ExtractPropertyName(LambdaExpression expression)
        {
            if (expression.Body is MemberExpression member)
            {
                return member.Member.Name;
            }
            // Handle unary expressions (like conversions)
            if (expression.Body is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
            {
                return unaryMember.Member.Name;
            }
            return null;
        }

        private QueryStrategy DetermineStrategy(IndexCandidate candidate)
        {
            if (candidate.Index.IndexType == IndexType.Identity)
            {
                return QueryStrategy.IndexSeek;
            }

            if (candidate.CanSatisfyWhere && candidate.CanSatisfyOrderBy)
            {
                return QueryStrategy.IndexRangeScan;
            }

            if (candidate.CanSatisfyWhere)
            {
                return QueryStrategy.IndexSeek;
            }

            if (candidate.CanSatisfyOrderBy)
            {
                return QueryStrategy.IndexScan;
            }

            return QueryStrategy.FullScan;
        }

        private IEnumerable<Nut<T>> ExecuteIndexQuery(QueryPlan<T> plan)
        {
            if (plan.SelectedIndex == null)
            {
                return ExecuteFullScan(plan);
            }

            // Get document IDs from the index
            IEnumerable<string> indexResults;

            // Try to use index for different query strategies
            switch (plan.Strategy)
            {
                case QueryStrategy.IndexSeek:
                case QueryStrategy.IndexRangeScan:
                    // Try to extract index-compatible predicates
                    indexResults = TryUseIndexForFiltering(plan);
                    break;

                case QueryStrategy.IndexScan:
                    // Use index for ordering only
                    indexResults = TryUseIndexForOrdering(plan);
                    break;

                default:
                    return ExecuteFullScan(plan);
            }

            // If index methods didn't return any results (empty enumerable),
            // it means we couldn't use the index, so fall back to full scan
            var indexResultsList = indexResults.ToList();
            if (!indexResultsList.Any())
            {
                // Fall back to full scan since we couldn't use the index effectively
                return ExecuteFullScan(plan);
            }

            // Convert IDs to Nuts by looking them up in the tree
            var results = indexResultsList
                .Select(id => _tree.Crack(id))
                .Where(payload => payload != null)
                .Select(payload => new Nut<T>
                {
                    Id = ExtractIdFromPayload(payload!),
                    Payload = payload!,
                    Timestamp = DateTime.UtcNow // Note: We lose timestamp info here - could be improved
                });

            // Always apply WHERE predicate to ensure correctness
            // The index narrows down the search space, but the predicate ensures exact matching
            if (plan.Context.WherePredicate != null)
            {
                results = results.Where(plan.Context.WherePredicate);
            }

            return results;
        }

        private IEnumerable<string> TryUseIndexForFiltering(QueryPlan<T> plan)
        {
            if (plan.SelectedIndex == null || plan.Context.WhereExpression == null)
            {
                return Enumerable.Empty<string>();
            }

            // Analyze the WHERE expression to extract indexable conditions
            var analyzer = new ExpressionAnalyzer<T>();
            var analysis = analyzer.Analyze(plan.Context.WhereExpression);

            if (!analysis.IsIndexable || !analysis.Conditions.Any())
            {
                return Enumerable.Empty<string>();
            }

            // Get the first condition (for now, we only handle single conditions)
            var condition = analysis.Conditions[0];

            // Get the property name from the selected index
            string? indexPropertyName = null;
            if (plan.SelectedIndex.IndexType == IndexType.Scalar)
            {
                var indexType = plan.SelectedIndex.GetType();
                var interfaces = indexType.GetInterfaces();
                var scalarInterface = interfaces.FirstOrDefault(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IScalarIndex<,>));

                if (scalarInterface != null)
                {
                    var propertySelectorProperty = scalarInterface.GetProperty("PropertySelector");
                    if (propertySelectorProperty != null)
                    {
                        var propertySelector = propertySelectorProperty.GetValue(plan.SelectedIndex) as LambdaExpression;
                        if (propertySelector != null)
                        {
                            indexPropertyName = ExtractPropertyName(propertySelector);
                        }
                    }
                }
            }

            // Check if the condition matches the index property
            if (indexPropertyName != condition.PropertyName)
            {
                return Enumerable.Empty<string>();
            }

            // Now we need to call the appropriate index method based on the operator
            // This requires reflection since we don't know the property type at compile time
            try
            {
                var indexType = plan.SelectedIndex.GetType();
                var interfaces = indexType.GetInterfaces();
                var scalarInterface = interfaces.FirstOrDefault(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IScalarIndex<,>));

                if (scalarInterface == null)
                {
                    return Enumerable.Empty<string>();
                }

                // Handle equality operator
                if (condition.Operator == ComparisonOperator.Equal && condition.IsConstantValue)
                {
                    // Call index.Lookup(value)
                    var lookupMethod = scalarInterface.GetMethod("Lookup");
                    if (lookupMethod != null)
                    {
                        var result = lookupMethod.Invoke(plan.SelectedIndex, new[] { condition.Value });
                        if (result is IEnumerable<string> ids)
                        {
                            return ids;
                        }
                    }
                }
                // Handle range operators
                else if (condition.IsConstantValue)
                {
                    // For range queries, we need to call index.Range(min, max)
                    // But we only have one condition, so we'll use GetMin() or GetMax() to get bounds
                    var rangeMethod = scalarInterface.GetMethod("Range");
                    if (rangeMethod != null)
                    {
                        object? min = null;
                        object? max = null;

                        // Determine min/max based on operator
                        switch (condition.Operator)
                        {
                            case ComparisonOperator.GreaterThan:
                            case ComparisonOperator.GreaterThanOrEqual:
                                min = condition.Value;
                                // Get max from index
                                var getMaxMethod = scalarInterface.GetMethod("GetMax");
                                if (getMaxMethod != null)
                                {
                                    max = getMaxMethod.Invoke(plan.SelectedIndex, null);
                                }
                                break;

                            case ComparisonOperator.LessThan:
                            case ComparisonOperator.LessThanOrEqual:
                                // Get min from index
                                var getMinMethod = scalarInterface.GetMethod("GetMin");
                                if (getMinMethod != null)
                                {
                                    min = getMinMethod.Invoke(plan.SelectedIndex, null);
                                }
                                max = condition.Value;
                                break;
                        }

                        if (min != null && max != null)
                        {
                            var result = rangeMethod.Invoke(plan.SelectedIndex, new[] { min, max });
                            if (result is IEnumerable<string> ids)
                            {
                                return ids;
                            }
                        }
                    }
                }
            }
            catch
            {
                // If anything fails, fall back to empty (which triggers full scan)
                return Enumerable.Empty<string>();
            }

            return Enumerable.Empty<string>();
        }

        private IEnumerable<string> TryUseIndexForOrdering(QueryPlan<T> plan)
        {
            if (plan.SelectedIndex == null || plan.SelectedIndex.IndexType != IndexType.Scalar)
            {
                return Enumerable.Empty<string>();
            }

            // Use index to get all IDs in sorted order
            try
            {
                var indexType = plan.SelectedIndex.GetType();
                var interfaces = indexType.GetInterfaces();
                var scalarInterface = interfaces.FirstOrDefault(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IScalarIndex<,>));

                if (scalarInterface == null)
                {
                    return Enumerable.Empty<string>();
                }

                // Call GetAllSorted(ascending)
                var getAllSortedMethod = scalarInterface.GetMethod("GetAllSorted");
                if (getAllSortedMethod != null)
                {
                    // Pass ascending = !orderDescending
                    bool ascending = !plan.Context.OrderDescending;
                    var result = getAllSortedMethod.Invoke(plan.SelectedIndex, new object[] { ascending });
                    if (result is IEnumerable<string> ids)
                    {
                        return ids;
                    }
                }
            }
            catch
            {
                // Fall back to empty if anything fails
                return Enumerable.Empty<string>();
            }

            return Enumerable.Empty<string>();
        }

        private string ExtractIdFromPayload(T payload)
        {
            // Try to extract ID from payload using common property names
            var type = typeof(T);
            var idProperty = type.GetProperty("Id") ?? type.GetProperty("ID") ?? type.GetProperty("Key");
            if (idProperty != null)
            {
                return idProperty.GetValue(payload)?.ToString() ?? Guid.NewGuid().ToString();
            }
            return Guid.NewGuid().ToString();
        }

        private IEnumerable<Nut<T>> ExecuteFullScan(QueryPlan<T> plan)
        {
            IEnumerable<Nut<T>> results = _tree.GetAllNuts();

            // Apply WHERE filter
            if (plan.Context.WherePredicate != null)
            {
                results = results.Where(plan.Context.WherePredicate);
            }

            return results;
        }
    }
}
