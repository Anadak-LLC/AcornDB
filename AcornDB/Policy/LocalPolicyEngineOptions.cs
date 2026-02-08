using System;

namespace AcornDB.Policy
{
    /// <summary>
    /// CORE: Configuration options for LocalPolicyEngine.
    /// Part of AcornDB.Core - lightweight, dependency-free enforcement.
    /// </summary>
    public class LocalPolicyEngineOptions
    {
        /// <summary>
        /// If true, throws exception when any policy fails. If false, emits event and continues.
        /// Default: false
        /// </summary>
        public bool EnforceAllPolicies { get; set; } = false;

        /// <summary>
        /// Default access when entity has no tags
        /// Default: true (allow)
        /// </summary>
        public bool DefaultAccessWhenNoTags { get; set; } = true;

        /// <summary>
        /// Enable verbose policy logging (deprecated - use PolicyEvaluated event instead)
        /// Default: false
        /// </summary>
        public bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// If true, uses emoji prefixes in log messages (e.g., 🔐, ⚠️).
        /// Set to false if your log aggregation system has encoding issues with Unicode.
        /// Default: true
        /// </summary>
        public bool UseEmojiInLogs { get; set; } = true;

        /// <summary>
        /// If true, caches policy evaluation results to avoid re-evaluating identical contexts.
        /// Cache is invalidated when policies are registered/unregistered.
        /// Default: true
        /// </summary>
        /// <remarks>GAP-003: Policy evaluation caching for performance.</remarks>
        public bool EnableEvaluationCache { get; set; } = true;

        /// <summary>
        /// Time-to-live for cached evaluation results.
        /// After this duration, results are re-evaluated even if policies haven't changed.
        /// Default: 5 minutes
        /// </summary>
        public TimeSpan EvaluationCacheTtl { get; set; } = TimeSpan.FromMinutes(5);
    }
}
