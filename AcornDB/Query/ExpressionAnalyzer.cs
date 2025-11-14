using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace AcornDB.Query
{
    /// <summary>
    /// Analyzes LINQ expression trees to extract information useful for query planning.
    /// Identifies property access, comparison operators, and values for index matching.
    /// </summary>
    public class ExpressionAnalyzer<T> where T : class
    {
        /// <summary>
        /// Analyze a WHERE predicate expression to extract indexable conditions
        /// </summary>
        public ExpressionAnalysisResult Analyze(Expression<Func<T, bool>> expression)
        {
            var result = new ExpressionAnalysisResult();

            if (expression == null)
                return result;

            var visitor = new IndexableExpressionVisitor<T>();
            visitor.Visit(expression);

            result.Conditions = visitor.Conditions;
            result.IsIndexable = visitor.Conditions.Any();

            return result;
        }

        /// <summary>
        /// Analyze an ORDER BY expression to extract the property being sorted
        /// </summary>
        public PropertyAccessInfo? AnalyzeOrderBy<TProperty>(Expression<Func<T, TProperty>> expression)
        {
            if (expression == null)
                return null;

            var visitor = new PropertyAccessVisitor<T>();
            visitor.Visit(expression);

            return visitor.PropertyAccess;
        }
    }

    /// <summary>
    /// Expression visitor that identifies indexable conditions in WHERE clauses
    /// </summary>
    internal class IndexableExpressionVisitor<T> : ExpressionVisitor
    {
        public List<IndexableCondition> Conditions { get; } = new List<IndexableCondition>();

        protected override Expression VisitBinary(BinaryExpression node)
        {
            // Check if this is a comparison between a property and a constant
            if (IsComparisonOperator(node.NodeType))
            {
                var condition = TryExtractCondition(node);
                if (condition != null)
                {
                    Conditions.Add(condition);
                }
            }

            return base.VisitBinary(node);
        }

        private bool IsComparisonOperator(ExpressionType nodeType)
        {
            return nodeType == ExpressionType.Equal ||
                   nodeType == ExpressionType.NotEqual ||
                   nodeType == ExpressionType.GreaterThan ||
                   nodeType == ExpressionType.GreaterThanOrEqual ||
                   nodeType == ExpressionType.LessThan ||
                   nodeType == ExpressionType.LessThanOrEqual;
        }

        private IndexableCondition? TryExtractCondition(BinaryExpression binary)
        {
            // Try to extract: property [operator] value or value [operator] property
            string? propertyName = null;
            Type? propertyType = null;
            object? value = null;
            bool isConstant = false;
            bool swapped = false;

            // Check left side for property access
            if (binary.Left is MemberExpression leftMember &&
                leftMember.Expression is ParameterExpression)
            {
                propertyName = leftMember.Member.Name;
                propertyType = leftMember.Type;

                // Check right side for constant
                if (TryGetConstantValue(binary.Right, out var rightValue))
                {
                    value = rightValue;
                    isConstant = true;
                }
            }
            // Check right side for property access (swapped comparison)
            else if (binary.Right is MemberExpression rightMember &&
                     rightMember.Expression is ParameterExpression)
            {
                propertyName = rightMember.Member.Name;
                propertyType = rightMember.Type;
                swapped = true;

                // Check left side for constant
                if (TryGetConstantValue(binary.Left, out var leftValue))
                {
                    value = leftValue;
                    isConstant = true;
                }
            }

            if (propertyName == null || propertyType == null)
                return null;

            var op = MapOperator(binary.NodeType, swapped);

            return new IndexableCondition
            {
                PropertyName = propertyName,
                PropertyType = propertyType,
                Operator = op,
                Value = value,
                IsConstantValue = isConstant
            };
        }

        private bool TryGetConstantValue(Expression expression, out object? value)
        {
            value = null;

            // Direct constant
            if (expression is ConstantExpression constant)
            {
                value = constant.Value;
                return true;
            }

            // Member access on a constant (e.g., captured variable)
            if (expression is MemberExpression memberExpr &&
                memberExpr.Expression is ConstantExpression closureConstant)
            {
                var member = memberExpr.Member;
                if (member is System.Reflection.FieldInfo field)
                {
                    value = field.GetValue(closureConstant.Value);
                    return true;
                }
                if (member is System.Reflection.PropertyInfo prop)
                {
                    value = prop.GetValue(closureConstant.Value);
                    return true;
                }
            }

            return false;
        }

        private ComparisonOperator MapOperator(ExpressionType nodeType, bool swapped)
        {
            // If comparison is swapped (value < property instead of property > value),
            // we need to flip the operator
            if (swapped)
            {
                return nodeType switch
                {
                    ExpressionType.Equal => ComparisonOperator.Equal,
                    ExpressionType.NotEqual => ComparisonOperator.NotEqual,
                    ExpressionType.GreaterThan => ComparisonOperator.LessThan,
                    ExpressionType.GreaterThanOrEqual => ComparisonOperator.LessThanOrEqual,
                    ExpressionType.LessThan => ComparisonOperator.GreaterThan,
                    ExpressionType.LessThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
                    _ => ComparisonOperator.Equal
                };
            }

            return nodeType switch
            {
                ExpressionType.Equal => ComparisonOperator.Equal,
                ExpressionType.NotEqual => ComparisonOperator.NotEqual,
                ExpressionType.GreaterThan => ComparisonOperator.GreaterThan,
                ExpressionType.GreaterThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
                ExpressionType.LessThan => ComparisonOperator.LessThan,
                ExpressionType.LessThanOrEqual => ComparisonOperator.LessThanOrEqual,
                _ => ComparisonOperator.Equal
            };
        }
    }

    /// <summary>
    /// Expression visitor that extracts property access information from ORDER BY clauses
    /// </summary>
    internal class PropertyAccessVisitor<T> : ExpressionVisitor
    {
        public PropertyAccessInfo? PropertyAccess { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            // Only capture the first property access at the parameter level
            if (node.Expression is ParameterExpression && PropertyAccess == null)
            {
                PropertyAccess = new PropertyAccessInfo
                {
                    PropertyName = node.Member.Name,
                    PropertyType = node.Type
                };
            }

            return base.VisitMember(node);
        }
    }
}
