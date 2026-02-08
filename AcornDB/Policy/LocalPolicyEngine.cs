using System;
using AcornDB.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AcornDB.Policy.BuiltInRules;
using AcornDB.Policy.Governance;

namespace AcornDB.Policy
{
    /// <summary>
    /// CORE POLICY ENGINE: Local policy enforcement engine implementing tag-based governance, TTL, and access control.
    /// Part of AcornDB.Core - lightweight, dependency-free, synchronous-safe enforcement.
    /// Thread-safe and designed for local-first, embedded applications.
    /// </summary>
    /// <remarks>
    /// <para><b>Evaluation Caching (GAP-003):</b> Policy evaluation results are cached to avoid
    /// re-evaluating identical contexts. Cache key is SHA256(EntityHash + PolicyVersion).
    /// Cache is invalidated when policies are registered/unregistered.</para>
    /// </remarks>
    public class LocalPolicyEngine : IPolicyEngine
    {
        private readonly ConcurrentDictionary<string, IPolicyRule> _policies;
        private readonly ConcurrentDictionary<string, HashSet<string>> _tagPermissions; // tag -> allowed roles
        private readonly ConcurrentDictionary<string, CachedEvaluationResult> _evaluationCache;
        private readonly LocalPolicyEngineOptions _options;
        private readonly IPolicyLog? _policyLog;
        private int _policyVersion; // Incremented on policy change to invalidate cache (via Interlocked)

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
            _evaluationCache = new ConcurrentDictionary<string, CachedEvaluationResult>();
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _policyLog = null;
            _policyVersion = 0;

            // Register default policies
            RegisterDefaultPolicies();
        }

        /// <summary>
        /// Creates a new LocalPolicyEngine with custom options and optional policy log.
        /// When a policy log is provided, policies are loaded from the governance ledger
        /// after verifying chain integrity.
        /// </summary>
        /// <param name="options">Engine configuration options.</param>
        /// <param name="policyLog">Optional hash-chained policy log for governance.</param>
        /// <exception cref="ChainIntegrityException">Thrown if policy log chain is invalid.</exception>
        /// <remarks>
        /// Consider using <see cref="Governance.GovernedPolicyEngine"/> decorator instead
        /// for better separation of concerns between core policy logic and governance.
        /// </remarks>
        [Obsolete("Use GovernedPolicyEngine decorator for governance capabilities. This constructor will be removed in v1.0.")]
        public LocalPolicyEngine(LocalPolicyEngineOptions options, IPolicyLog? policyLog)
        {
            _policies = new ConcurrentDictionary<string, IPolicyRule>();
            _tagPermissions = new ConcurrentDictionary<string, HashSet<string>>();
            _evaluationCache = new ConcurrentDictionary<string, CachedEvaluationResult>();
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _policyLog = policyLog;
            _policyVersion = 0;

            // Register default policies
            RegisterDefaultPolicies();

            // Load policies from governance ledger if provided
            if (_policyLog != null)
            {
                LoadPoliciesFromLog();
            }
        }

        /// <summary>
        /// Gets the policy log if one was configured.
        /// </summary>
        /// <remarks>
        /// Use <see cref="Governance.GovernedPolicyEngine.PolicyLog"/> instead.
        /// </remarks>
        [Obsolete("Use GovernedPolicyEngine.PolicyLog instead. This property will be removed in v1.0.")]
        public IPolicyLog? PolicyLog => _policyLog;

        private void LoadPoliciesFromLog()
        {
            if (_policyLog == null) return;

            var result = _policyLog.VerifyChain();
            if (!result.IsValid)
            {
                throw new ChainIntegrityException(
                    result.Details ?? "Policy log chain integrity verification failed",
                    result.BrokenAtIndex ?? -1);
            }

            foreach (var seal in _policyLog.GetAllSeals())
            {
                RegisterPolicy(seal.Policy);
            }

            var prefix = _options.UseEmojiInLogs ? "🔐 " : "[POLICY] ";
            AcornLog.Info($"{prefix}Loaded {_policyLog.Count} policies from governance ledger");
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
            InvalidateCache();
        }

        public bool UnregisterPolicy(string policyName)
        {
            var removed = _policies.TryRemove(policyName, out _);
            if (removed) InvalidateCache();
            return removed;
        }

        public IReadOnlyCollection<IPolicyRule> GetPolicies()
        {
            return _policies.Values.ToList().AsReadOnly();
        }

        public PolicyValidationResult Validate<T>(T entity)
        {
            if (entity == null)
            {
                var nullResult = new PolicyValidationResult { IsValid = false };
                nullResult.Results.Add(PolicyEvaluationResult.Failure("Entity is null"));
                return nullResult;
            }

            // Try to get from cache (GAP-003)
            var cacheKey = ComputeCacheKey(entity);
            if (_options.EnableEvaluationCache && 
                _evaluationCache.TryGetValue(cacheKey, out var cached) &&
                cached.PolicyVersion == _policyVersion &&
                DateTime.UtcNow < cached.ExpiresAt)
            {
                return cached.Result;
            }

            // Evaluate policies
            var result = new PolicyValidationResult { IsValid = true };
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

            // Cache the result (GAP-003)
            if (_options.EnableEvaluationCache)
            {
                var cacheEntry = new CachedEvaluationResult
                {
                    Result = result,
                    PolicyVersion = _policyVersion,
                    ExpiresAt = DateTime.UtcNow.Add(_options.EvaluationCacheTtl)
                };
                _evaluationCache[cacheKey] = cacheEntry;
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
                        AcornLog.Warning($"[PolicyEngine] {target}");
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

        /// <summary>
        /// Invalidates the evaluation cache. Called when policies change.
        /// </summary>
        private void InvalidateCache()
        {
            Interlocked.Increment(ref _policyVersion);
            _evaluationCache.Clear();
        }

        /// <summary>
        /// Computes a cache key for an entity based on its identity and tags.
        /// </summary>
        private string ComputeCacheKey<T>(T entity)
        {
            var sb = new StringBuilder();
            sb.Append(typeof(T).FullName);
            sb.Append('|');
            sb.Append(entity?.GetHashCode() ?? 0);
            sb.Append('|');
            sb.Append(_policyVersion);

            // Include tags if entity is taggable
            if (entity is IPolicyTaggable taggable && taggable.Tags.Any())
            {
                sb.Append('|');
                sb.Append(string.Join(",", taggable.Tags.OrderBy(t => t)));
            }

            // Compute SHA256 hash for compact key
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Gets the current cache statistics for monitoring.
        /// </summary>
        public (int CacheSize, int PolicyVersion) GetCacheStats()
        {
            return (_evaluationCache.Count, _policyVersion);
        }

        /// <summary>
        /// Clears the evaluation cache manually.
        /// </summary>
        public void ClearCache()
        {
            _evaluationCache.Clear();
        }
    }

    /// <summary>
    /// Cached policy evaluation result with expiration.
    /// </summary>
    internal sealed class CachedEvaluationResult
    {
        public required PolicyValidationResult Result { get; init; }
        public required int PolicyVersion { get; init; }
        public required DateTime ExpiresAt { get; init; }
    }
}
