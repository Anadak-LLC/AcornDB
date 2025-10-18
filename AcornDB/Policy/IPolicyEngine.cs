using System;
using System.Collections.Generic;

namespace AcornDB.Policy
{
    /// <summary>
    /// Core interface for local policy enforcement including access control, TTL, and data redaction.
    /// Implements MossGrove-aligned tag-based governance for local-first applications.
    /// </summary>
    public interface IPolicyEngine
    {
        /// <summary>
        /// Apply all configured policies to an entity (TTL, redaction, etc.)
        /// </summary>
        /// <typeparam name="T">Entity type</typeparam>
        /// <param name="entity">Entity to apply policies to</param>
        void ApplyPolicies<T>(T entity);

        /// <summary>
        /// Validate if a user/role has access to an entity based on tags and permissions.
        /// </summary>
        /// <typeparam name="T">Entity type</typeparam>
        /// <param name="entity">Entity to validate access for</param>
        /// <param name="userRole">User role or identifier</param>
        /// <returns>True if access is granted, false otherwise</returns>
        bool ValidateAccess<T>(T entity, string userRole);

        /// <summary>
        /// Enforce Time-To-Live (TTL) policies on a collection of entities.
        /// Entities past their TTL should be marked for deletion or purged.
        /// </summary>
        /// <typeparam name="T">Entity type</typeparam>
        /// <param name="entities">Entities to enforce TTL on</param>
        void EnforceTTL<T>(IEnumerable<T> entities);

        /// <summary>
        /// Register a custom policy rule
        /// </summary>
        /// <param name="policyRule">Policy rule to register</param>
        void RegisterPolicy(IPolicyRule policyRule);

        /// <summary>
        /// Remove a registered policy rule
        /// </summary>
        /// <param name="policyName">Name of the policy to remove</param>
        /// <returns>True if removed, false if not found</returns>
        bool UnregisterPolicy(string policyName);

        /// <summary>
        /// Get all registered policies
        /// </summary>
        IReadOnlyCollection<IPolicyRule> GetPolicies();

        /// <summary>
        /// Check if an entity meets all policy requirements
        /// </summary>
        /// <typeparam name="T">Entity type</typeparam>
        /// <param name="entity">Entity to validate</param>
        /// <returns>Policy validation result with details</returns>
        PolicyValidationResult Validate<T>(T entity);
    }

    /// <summary>
    /// Represents a custom policy rule that can be applied to entities
    /// </summary>
    public interface IPolicyRule
    {
        /// <summary>
        /// Unique name of the policy
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of what the policy enforces
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Priority for policy execution (higher = earlier execution)
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Evaluate the policy against an entity
        /// </summary>
        /// <typeparam name="T">Entity type</typeparam>
        /// <param name="entity">Entity to evaluate</param>
        /// <param name="context">Policy execution context</param>
        /// <returns>Result of policy evaluation</returns>
        PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context);
    }

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
