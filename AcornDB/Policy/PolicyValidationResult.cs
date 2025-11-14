using System.Collections.Generic;
using System.Linq;

namespace AcornDB.Policy
{
    /// <summary>
    /// Aggregate result of all policy validations
    /// </summary>
    public class PolicyValidationResult
    {
        /// <summary>
        /// Whether all policies passed
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Individual policy results
        /// </summary>
        public List<PolicyEvaluationResult> Results { get; set; } = new();

        /// <summary>
        /// First failure reason (if any)
        /// </summary>
        public string? FailureReason => Results.FirstOrDefault(r => !r.Passed)?.Reason;
    }
}
