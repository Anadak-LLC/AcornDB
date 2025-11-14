using System;
using AcornDB.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AcornDB.Policy.BuiltInRules;

namespace AcornDB.Policy
{
    /// <summary>
    /// CORE POLICY ENGINE: Local policy enforcement engine implementing tag-based governance, TTL, and access control.
    /// Part of AcornDB.Core - lightweight, dependency-free, synchronous-safe enforcement.
    /// Thread-safe and designed for local-first, embedded applications.
    /// </summary>
    public class LocalPolicyEngine : IPolicyEngine
    {
        private readonly ConcurrentDictionary<string, IPolicyRule> _policies;
        private readonly ConcurrentDictionary<string, HashSet<string>> _tagPermissions; // tag -> allowed roles
        private readonly LocalPolicyEngineOptions _options;

        /// <summary>
        /// Event raised when a policy is evaluated. Extensions can subscribe to this for logging, auditing, etc.
        /// </summary>
        public event Action<PolicyEvaluationResult>? PolicyEvaluated;

        /// <summary>
        /// Creates a new LocalPolicyEngine with default options
        /// </summary>
        public LocalPolicyEngine() : this(new LocalPolicyEngineOptions())
        {
        }

        /// <summary>
        /// Creates a new LocalPolicyEngine with custom options
        /// </summary>
        public LocalPolicyEngine(LocalPolicyEngineOptions options)
        {
            _policies = new ConcurrentDictionary<string, IPolicyRule>();
            _tagPermissions = new ConcurrentDictionary<string, HashSet<string>>();
            _options = options ?? throw new ArgumentNullException(nameof(options));

            // Register default policies
            RegisterDefaultPolicies();
        }

        private void RegisterDefaultPolicies()
        {
            // TTL enforcement policy
            RegisterPolicy(new TtlPolicyRule());

            // Tag-based access control policy
            RegisterPolicy(new TagAccessPolicyRule(_tagPermissions));
        }

        public void ApplyPolicies<T>(T entity)
        {
            if (entity == null) return;

            var context = new PolicyContext();
            foreach (var policy in _policies.Values.OrderByDescending(p => p.Priority))
            {
                var result = policy.Evaluate(entity, context);

                // Raise event for extensions to handle (logging, audit, etc.)
                PolicyEvaluated?.Invoke(result);

                if (_options.EnforceAllPolicies && !result.Passed)
                {
                    throw new PolicyViolationException(
                        $"Policy '{policy.Name}' failed: {result.Reason}");
                }

                // Execute actions from policy result
                ExecutePolicyActions(entity, result.Actions);
            }
        }

        public bool ValidateAccess<T>(T entity, string userRole)
        {
            if (entity == null) return true;
            if (string.IsNullOrEmpty(userRole)) return false;

            // Check if entity implements IPolicyTaggable
            if (entity is IPolicyTaggable taggable)
            {
                // If entity has tags, check permissions
                if (taggable.Tags.Any())
                {
                    // Check if user role has access to any of the entity's tags
                    foreach (var tag in taggable.Tags)
                    {
                        if (_tagPermissions.TryGetValue(tag, out var allowedRoles))
                        {
                            if (allowedRoles.Contains(userRole) || allowedRoles.Contains("*"))
                            {
                                return true;
                            }
                        }
                    }

                    // If tags exist but no permissions matched, deny access
                    return false;
                }
            }

            // If no tags or not taggable, allow by default (can be configured)
            return _options.DefaultAccessWhenNoTags;
        }

        public void EnforceTTL<T>(IEnumerable<T> entities)
        {
            if (entities == null) return;

            var context = new PolicyContext { Operation = "TTL_Check" };
            var ttlPolicy = _policies.Values.OfType<TtlPolicyRule>().FirstOrDefault();

            if (ttlPolicy == null) return;

            foreach (var entity in entities)
            {
                var result = ttlPolicy.Evaluate(entity, context);

                // Raise event for extensions to handle (logging, audit, etc.)
                PolicyEvaluated?.Invoke(result);

                if (!result.Passed)
                {
                    ExecutePolicyActions(entity, result.Actions);
                }
            }
        }

        public void RegisterPolicy(IPolicyRule policyRule)
        {
            if (policyRule == null)
                throw new ArgumentNullException(nameof(policyRule));

            _policies[policyRule.Name] = policyRule;
        }

        public bool UnregisterPolicy(string policyName)
        {
            return _policies.TryRemove(policyName, out _);
        }

        public IReadOnlyCollection<IPolicyRule> GetPolicies()
        {
            return _policies.Values.ToList().AsReadOnly();
        }

        public PolicyValidationResult Validate<T>(T entity)
        {
            var result = new PolicyValidationResult { IsValid = true };

            if (entity == null)
            {
                result.IsValid = false;
                result.Results.Add(PolicyEvaluationResult.Failure("Entity is null"));
                return result;
            }

            var context = new PolicyContext();
            foreach (var policy in _policies.Values.OrderByDescending(p => p.Priority))
            {
                var evalResult = policy.Evaluate(entity, context);
                result.Results.Add(evalResult);

                if (!evalResult.Passed)
                {
                    result.IsValid = false;
                }
            }

            return result;
        }

        /// <summary>
        /// Grant a role access to entities with a specific tag
        /// </summary>
        public void GrantTagAccess(string tag, string role)
        {
            var roles = _tagPermissions.GetOrAdd(tag, _ => new HashSet<string>());
            lock (roles)
            {
                roles.Add(role);
            }
        }

        /// <summary>
        /// Revoke a role's access to entities with a specific tag
        /// </summary>
        public void RevokeTagAccess(string tag, string role)
        {
            if (_tagPermissions.TryGetValue(tag, out var roles))
            {
                lock (roles)
                {
                    roles.Remove(role);
                }
            }
        }

        /// <summary>
        /// Get all roles that have access to a specific tag
        /// </summary>
        public IReadOnlySet<string> GetRolesForTag(string tag)
        {
            if (_tagPermissions.TryGetValue(tag, out var roles))
            {
                lock (roles)
                {
                    return new HashSet<string>(roles);
                }
            }
            return new HashSet<string>();
        }

        private void ExecutePolicyActions<T>(T entity, List<string> actions)
        {
            foreach (var action in actions)
            {
                // Parse action format: "ActionType:Target"
                var parts = action.Split(':', 2);
                var actionType = parts[0];
                var target = parts.Length > 1 ? parts[1] : null;

                switch (actionType.ToUpperInvariant())
                {
                    case "REDACT":
                        RedactField(entity, target);
                        break;
                    case "DELETE":
                        // Deletion would be handled by the caller
                        break;
                    case "DENY":
                        throw new PolicyViolationException($"Access denied: {target}");
                    case "WARN":
                        AcornLog.Info($"� Policy warning: {target}");
                        break;
                }
            }
        }

        private void RedactField<T>(T entity, string? fieldName)
        {
            if (entity == null || string.IsNullOrEmpty(fieldName)) return;

            var type = typeof(T);
            var property = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);

            if (property != null && property.CanWrite)
            {
                var propertyType = property.PropertyType;

                // Set appropriate redacted value based on type
                if (propertyType == typeof(string))
                {
                    property.SetValue(entity, "[REDACTED]");
                }
                else if (propertyType.IsValueType)
                {
                    property.SetValue(entity, Activator.CreateInstance(propertyType));
                }
                else
                {
                    property.SetValue(entity, null);
                }
            }
        }
    }
}
