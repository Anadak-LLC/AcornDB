using System;

namespace AcornDB.Query
{
    /// <summary>
    /// Information about a property access in an expression
    /// </summary>
    public class PropertyAccessInfo
    {
        public string PropertyName { get; set; } = string.Empty;
        public Type PropertyType { get; set; } = typeof(object);
    }
}
