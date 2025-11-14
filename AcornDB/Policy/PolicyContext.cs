using System.Collections.Generic;

namespace AcornDB.Policy
{
    /// <summary>
    /// Context information for policy evaluation
    /// </summary>
    public class PolicyContext
    {
        /// <summary>
        /// Current user/role requesting access
        /// </summary>
        public string? UserRole { get; set; }

        /// <summary>
        /// Operation being performed (Read, Write, Delete, etc.)
        /// </summary>
        public string? Operation { get; set; }

        /// <summary>
        /// Additional context metadata
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
