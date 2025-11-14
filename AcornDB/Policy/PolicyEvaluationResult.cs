using System.Collections.Generic;

namespace AcornDB.Policy
{
    /// <summary>
    /// Result of a policy evaluation
    /// </summary>
    public class PolicyEvaluationResult
    {
        /// <summary>
        /// Whether the policy check passed
        /// </summary>
        public bool Passed { get; set; }

        /// <summary>
        /// Reason for the result (especially useful for failures)
        /// </summary>
        public string? Reason { get; set; }

        /// <summary>
        /// Actions to take (e.g., "Redact:SSN", "Deny:Access")
        /// </summary>
        public List<string> Actions { get; set; } = new();

        public static PolicyEvaluationResult Success(string? reason = null)
        {
            return new PolicyEvaluationResult { Passed = true, Reason = reason };
        }

        public static PolicyEvaluationResult Failure(string reason)
        {
            return new PolicyEvaluationResult { Passed = false, Reason = reason };
        }
    }
}
