using System;
using AcornDB.Logging;
using System.Text;
using AcornDB.Policy;
using AcornDB.Policy.Governance;
using AcornDB.Storage.Serialization;

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
        private readonly IPolicyLog? _policyLog;
        private ChainValidationResult? _cachedChainState;

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
            : this(policyEngine, policyLog: null, serializer, sequence, options)
        {
        }

        /// <summary>
        /// Creates a PolicyEnforcementRoot with optional policy log for chain verification.
        /// </summary>
        /// <param name="policyEngine">Policy engine for evaluation.</param>
        /// <param name="policyLog">Optional hash-chained policy log for governance verification.</param>
        /// <param name="serializer">Serializer for deserializing data during validation.</param>
        /// <param name="sequence">Root sequence in the processing chain.</param>
        /// <param name="options">Policy enforcement options.</param>
        public PolicyEnforcementRoot(
            IPolicyEngine policyEngine,
            IPolicyLog? policyLog,
            ISerializer? serializer = null,
            int sequence = 10,
            PolicyEnforcementOptions? options = null)
        {
            _policyEngine = policyEngine ?? throw new ArgumentNullException(nameof(policyEngine));
            _policyLog = policyLog;
            _serializer = serializer ?? new NewtonsoftJsonSerializer();
            Sequence = sequence;
            _options = options ?? new PolicyEnforcementOptions();
            _metrics = new PolicyEnforcementMetrics();
        }

        /// <summary>
        /// Gets the policy log if one was configured.
        /// </summary>
        public IPolicyLog? PolicyLog => _policyLog;

        public string GetSignature()
        {
            return "policy-enforcement";
        }

        public byte[] OnStash(byte[] data, RootProcessingContext context)
        {
            // Policy enforcement on write
            if (!_options.EnforceOnWrite)
                return data;

            // Verify chain integrity if policy log is configured
            VerifyChainIntegrity(context);

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
                        AcornLog.Info($"⚠️ Policy violation on write (allowed by config): {validationResult.FailureReason}");
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
                AcornLog.Info($"⚠️ Policy enforcement failed on write for document '{context.DocumentId}': {ex.Message}");

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

            // Verify chain integrity if policy log is configured
            VerifyChainIntegrity(context);

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
                            AcornLog.Info($"⚠️ Document '{context.DocumentId}' expired, returning null");
                            return Array.Empty<byte>(); // Signal to trunk that data is expired
                        }

                        // If not throwing, log and continue
                        AcornLog.Info($"⚠️ Policy violation on read (allowed by config): {validationResult.FailureReason}");
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
                AcornLog.Info($"⚠️ Policy enforcement failed on read for document '{context.DocumentId}': {ex.Message}");

                if (_options.ThrowOnPolicyViolation)
                    throw;

                return data;
            }
        }

        /// <summary>
        /// Verifies policy chain integrity if a policy log is configured.
        /// Caches the result to avoid re-validating on every operation.
        /// </summary>
        /// <param name="context">Processing context to store chain state.</param>
        /// <exception cref="ChainIntegrityException">Thrown if chain is invalid.</exception>
        private void VerifyChainIntegrity(RootProcessingContext context)
        {
            if (_policyLog == null)
                return;

            // Use cached result if available
            if (_cachedChainState != null)
            {
                context.ChainState = _cachedChainState;
                return;
            }

            // Verify chain and cache result
            var chainResult = _policyLog.VerifyChain();
            _cachedChainState = chainResult;
            context.ChainState = chainResult;

            if (!chainResult.IsValid)
            {
                _metrics.RecordError();
                throw new ChainIntegrityException(
                    chainResult.Details ?? "Policy chain integrity verification failed",
                    chainResult.BrokenAtIndex ?? -1);
            }
        }

        /// <summary>
        /// Invalidates the cached chain state, forcing re-verification on next operation.
        /// Call this after appending to the policy log.
        /// </summary>
        public void InvalidateChainCache()
        {
            _cachedChainState = null;
        }
    }
}
