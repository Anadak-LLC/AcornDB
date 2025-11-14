using System;

namespace AcornDB.Query
{
    /// <summary>
    /// Represents a single indexable condition extracted from a WHERE clause
    /// </summary>
    public class IndexableCondition
    {
        /// <summary>
        /// Property name being compared (e.g., "Email", "Age")
        /// </summary>
        public string PropertyName { get; set; } = string.Empty;

        /// <summary>
        /// Property type
        /// </summary>
        public Type PropertyType { get; set; } = typeof(object);

        /// <summary>
        /// Comparison operator (Equal, GreaterThan, LessThan, etc.)
        /// </summary>
        public ComparisonOperator Operator { get; set; }

        /// <summary>
        /// Value being compared against (if constant)
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// Whether the value is a constant (true) or variable (false)
        /// </summary>
        public bool IsConstantValue { get; set; }
    }
}
