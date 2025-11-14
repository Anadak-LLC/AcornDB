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
}
