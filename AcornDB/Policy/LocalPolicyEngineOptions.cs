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
    }
}
