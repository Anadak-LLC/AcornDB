using System;
using System.Text;
using AcornDB.Policy;

namespace AcornDB.Storage.Roots
{
    /// <summary>
    /// Root processor that enforces policy rules during read/write operations.
    /// Validates access control, TTL, and other policies at the storage boundary.
    /// Works at the byte level but deserializes temporarily to validate policies.
    /// Recommended sequence: 1-49 (runs before other transformations)
    /// </summary>
    public class PolicyEnforcementRoot : IRoot
    {
        private readonly IPolicyEngine _policyEngine;
        private readonly ISerializer _serializer;
        private readonly PolicyEnforcementMetrics _metrics;
        private readonly PolicyEnforcementOptions _options;

        public string Name => "PolicyEnforcement";
        public int Sequence { get; }

        /// <summary>
        /// Policy enforcement metrics for monitoring
        /// </summary>
        public PolicyEnforcementMetrics Metrics => _metrics;

        public PolicyEnforcementRoot(
            IPolicyEngine policyEngine,
            ISerializer? serializer = null,
            int sequence = 10,
            PolicyEnforcementOptions? options = null)
        {
            _policyEngine = policyEngine ?? throw new ArgumentNullException(nameof(policyEngine));
            _serializer = serializer ?? new NewtonsoftJsonSerializer();
            Sequence = sequence;
            _options = options ?? new PolicyEnforcementOptions();
            _metrics = new PolicyEnforcementMetrics();
        }

        public string GetSignature()
        {
            return "policy-enforcement";
        }

        public byte[] OnStash(byte[] data, RootProcessingContext context)
        {
            // Policy enforcement on write
            if (!_options.EnforceOnWrite)
                return data;

            try
            {
                // Temporarily deserialize to validate policies
                var json = Encoding.UTF8.GetString(data);
                var nut = _serializer.Deserialize<dynamic>(json);

                if (nut != null)
                {
                    // Validate policies
                    var validationResult = _policyEngine.Validate(nut);

                    if (!validationResult.IsValid)
                    {
                        _metrics.RecordDenial("Write", validationResult.FailureReason ?? "Unknown");

                        if (_options.ThrowOnPolicyViolation)
                        {
                            throw new PolicyViolationException(
                                $"Policy violation on write: {validationResult.FailureReason}");
                        }

                        // If not throwing, log and continue
                        Console.WriteLine($"⚠️ Policy violation on write (allowed by config): {validationResult.FailureReason}");
                    }
                    else
                    {
                        _metrics.RecordSuccess("Write");
                    }
                }

                // Add policy signature to context
                context.TransformationSignatures.Add(GetSignature());

                return data;
            }
            catch (PolicyViolationException)
            {
                throw; // Re-throw policy violations
            }
            catch (Exception ex)
            {
                _metrics.RecordError();
                Console.WriteLine($"⚠️ Policy enforcement failed on write for document '{context.DocumentId}': {ex.Message}");

                if (_options.ThrowOnPolicyViolation)
                    throw;

                return data;
            }
        }

        public byte[] OnCrack(byte[] data, RootProcessingContext context)
        {
            // Policy enforcement on read
            if (!_options.EnforceOnRead)
                return data;

            try
            {
                // Temporarily deserialize to validate policies
                var json = Encoding.UTF8.GetString(data);
                var nut = _serializer.Deserialize<dynamic>(json);

                if (nut != null)
                {
                    // Validate policies (including TTL and access control)
                    var validationResult = _policyEngine.Validate(nut);

                    if (!validationResult.IsValid)
                    {
                        _metrics.RecordDenial("Read", validationResult.FailureReason ?? "Unknown");

                        if (_options.ThrowOnPolicyViolation)
                        {
                            throw new PolicyViolationException(
                                $"Policy violation on read: {validationResult.FailureReason}");
                        }

                        // Check for TTL expiration - return null/empty to signal deletion
                        if (_options.ReturnNullOnTTLExpired &&
                            validationResult.FailureReason?.Contains("expired", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            Console.WriteLine($"⚠️ Document '{context.DocumentId}' expired, returning null");
                            return Array.Empty<byte>(); // Signal to trunk that data is expired
                        }

                        // If not throwing, log and continue
                        Console.WriteLine($"⚠️ Policy violation on read (allowed by config): {validationResult.FailureReason}");
                    }
                    else
                    {
                        _metrics.RecordSuccess("Read");
                    }
                }

                return data;
            }
            catch (PolicyViolationException)
            {
                throw; // Re-throw policy violations
            }
            catch (Exception ex)
            {
                _metrics.RecordError();
                Console.WriteLine($"⚠️ Policy enforcement failed on read for document '{context.DocumentId}': {ex.Message}");

                if (_options.ThrowOnPolicyViolation)
                    throw;

                return data;
            }
        }
    }

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

    /// <summary>
    /// Metrics for policy enforcement operations
    /// </summary>
    public class PolicyEnforcementMetrics
    {
        public long TotalWriteChecks { get; private set; }
        public long TotalReadChecks { get; private set; }
        public long TotalDenials { get; private set; }
        public long TotalErrors { get; private set; }

        private readonly object _lock = new object();

        internal void RecordSuccess(string operation)
        {
            lock (_lock)
            {
                if (operation == "Write")
                    TotalWriteChecks++;
                else if (operation == "Read")
                    TotalReadChecks++;
            }
        }

        internal void RecordDenial(string operation, string reason)
        {
            lock (_lock)
            {
                TotalDenials++;
                if (operation == "Write")
                    TotalWriteChecks++;
                else if (operation == "Read")
                    TotalReadChecks++;
            }
        }

        internal void RecordError()
        {
            lock (_lock)
            {
                TotalErrors++;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                TotalWriteChecks = 0;
                TotalReadChecks = 0;
                TotalDenials = 0;
                TotalErrors = 0;
            }
        }

        public override string ToString()
        {
            return $"Writes: {TotalWriteChecks}, Reads: {TotalReadChecks}, Denials: {TotalDenials}, Errors: {TotalErrors}";
        }
    }
}
