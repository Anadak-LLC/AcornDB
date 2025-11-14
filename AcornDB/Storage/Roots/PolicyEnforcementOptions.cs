namespace AcornDB.Storage.Roots
{
    /// <summary>
    /// Configuration options for policy enforcement
    /// </summary>
    public class PolicyEnforcementOptions
    {
        /// <summary>
        /// Enforce policies on write operations (default: true)
        /// </summary>
        public bool EnforceOnWrite { get; set; } = true;

        /// <summary>
        /// Enforce policies on read operations (default: true)
        /// </summary>
        public bool EnforceOnRead { get; set; } = true;

        /// <summary>
        /// Throw exception on policy violation (default: true)
        /// If false, violations are logged but data passes through
        /// </summary>
        public bool ThrowOnPolicyViolation { get; set; } = true;

        /// <summary>
        /// Return null/empty bytes when TTL expired (default: true)
        /// Signals to trunk that data should be treated as deleted
        /// </summary>
        public bool ReturnNullOnTTLExpired { get; set; } = true;

        /// <summary>
        /// Permissive mode: log violations but don't block (default: false)
        /// </summary>
        public static PolicyEnforcementOptions Permissive => new PolicyEnforcementOptions
        {
            ThrowOnPolicyViolation = false
        };

        /// <summary>
        /// Strict mode: block all violations (default)
        /// </summary>
        public static PolicyEnforcementOptions Strict => new PolicyEnforcementOptions
        {
            ThrowOnPolicyViolation = true
        };
    }
}
