using System;
using AcornDB.Compression;
using AcornDB.Policy;
using AcornDB.Security;
using AcornDB.Storage.Roots;
using AcornDB.Storage.Serialization;

namespace AcornDB.Storage
{
    /// <summary>
    /// Fluent extension methods for adding roots to trunks.
    /// Provides a clean, chainable API for configuring trunk processing pipelines.
    /// </summary>
    public static class TrunkExtensions
    {
        /// <summary>
        /// Add compression to the trunk using the specified compression provider.
        /// Compresses data before storage, decompresses on retrieval.
        /// </summary>
        /// <param name="trunk">The trunk to add compression to</param>
        /// <param name="compression">Compression provider (e.g., GzipCompressionProvider)</param>
        /// <param name="sequence">Processing sequence (default: 100)</param>
        /// <returns>The trunk for method chaining</returns>
        public static ITrunk<T> WithCompression<T>(
            this ITrunk<T> trunk,
            ICompressionProvider compression,
            int sequence = 100)
        {
            if (trunk == null) throw new ArgumentNullException(nameof(trunk));
            if (compression == null) throw new ArgumentNullException(nameof(compression));

            trunk.AddRoot(new CompressionRoot(compression, sequence));
            return trunk;
        }

        /// <summary>
        /// Add encryption to the trunk using the specified encryption provider.
        /// Encrypts data before storage, decrypts on retrieval.
        /// </summary>
        /// <param name="trunk">The trunk to add encryption to</param>
        /// <param name="encryption">Encryption provider (e.g., AesEncryptionProvider)</param>
        /// <param name="sequence">Processing sequence (default: 200)</param>
        /// <param name="algorithmName">Algorithm name for signature (default: aes256)</param>
        /// <returns>The trunk for method chaining</returns>
        public static ITrunk<T> WithEncryption<T>(
            this ITrunk<T> trunk,
            IEncryptionProvider encryption,
            int sequence = 200,
            string? algorithmName = null)
        {
            if (trunk == null) throw new ArgumentNullException(nameof(trunk));
            if (encryption == null) throw new ArgumentNullException(nameof(encryption));

            trunk.AddRoot(new EncryptionRoot(encryption, sequence, algorithmName));
            return trunk;
        }

        /// <summary>
        /// Add policy enforcement to the trunk using the specified policy engine.
        /// Validates access control, TTL, and other policies during read/write.
        /// </summary>
        /// <param name="trunk">The trunk to add policy enforcement to</param>
        /// <param name="policyEngine">Policy engine to use for validation</param>
        /// <param name="sequence">Processing sequence (default: 10, runs early)</param>
        /// <param name="options">Policy enforcement options</param>
        /// <param name="serializer">Optional serializer for policy validation</param>
        /// <returns>The trunk for method chaining</returns>
        public static ITrunk<T> WithPolicyEnforcement<T>(
            this ITrunk<T> trunk,
            IPolicyEngine policyEngine,
            int sequence = 10,
            PolicyEnforcementOptions? options = null,
            ISerializer? serializer = null)
        {
            if (trunk == null) throw new ArgumentNullException(nameof(trunk));
            if (policyEngine == null) throw new ArgumentNullException(nameof(policyEngine));

            trunk.AddRoot(new PolicyEnforcementRoot(policyEngine, serializer, sequence, options));
            return trunk;
        }

        /// <summary>
        /// Add a custom root processor to the trunk.
        /// </summary>
        /// <param name="trunk">The trunk to add the root to</param>
        /// <param name="root">The root processor to add</param>
        /// <returns>The trunk for method chaining</returns>
        public static ITrunk<T> WithRoot<T>(
            this ITrunk<T> trunk,
            IRoot root)
        {
            if (trunk == null) throw new ArgumentNullException(nameof(trunk));
            if (root == null) throw new ArgumentNullException(nameof(root));

            trunk.AddRoot(root);
            return trunk;
        }

        /// <summary>
        /// Remove a root processor from the trunk by name.
        /// </summary>
        /// <param name="trunk">The trunk to remove the root from</param>
        /// <param name="rootName">Name of the root to remove</param>
        /// <returns>The trunk for method chaining</returns>
        public static ITrunk<T> WithoutRoot<T>(
            this ITrunk<T> trunk,
            string rootName)
        {
            if (trunk == null) throw new ArgumentNullException(nameof(trunk));
            if (string.IsNullOrEmpty(rootName)) throw new ArgumentNullException(nameof(rootName));

            trunk.RemoveRoot(rootName);
            return trunk;
        }

        /// <summary>
        /// Configure a complete secure trunk with compression and encryption.
        /// Order: Compression (100) → Encryption (200)
        /// </summary>
        /// <param name="trunk">The trunk to configure</param>
        /// <param name="compression">Compression provider</param>
        /// <param name="encryption">Encryption provider</param>
        /// <returns>The trunk for method chaining</returns>
        public static ITrunk<T> WithSecureStorage<T>(
            this ITrunk<T> trunk,
            ICompressionProvider compression,
            IEncryptionProvider encryption)
        {
            return trunk
                .WithCompression(compression, sequence: 100)
                .WithEncryption(encryption, sequence: 200);
        }

        /// <summary>
        /// Configure a complete governed trunk with policy, compression, and encryption.
        /// Order: Policy (10) → Compression (100) → Encryption (200)
        /// </summary>
        /// <param name="trunk">The trunk to configure</param>
        /// <param name="policyEngine">Policy engine for validation</param>
        /// <param name="compression">Compression provider</param>
        /// <param name="encryption">Encryption provider</param>
        /// <returns>The trunk for method chaining</returns>
        public static ITrunk<T> WithGovernedStorage<T>(
            this ITrunk<T> trunk,
            IPolicyEngine policyEngine,
            ICompressionProvider compression,
            IEncryptionProvider encryption)
        {
            return trunk
                .WithPolicyEnforcement(policyEngine, sequence: 10)
                .WithCompression(compression, sequence: 100)
                .WithEncryption(encryption, sequence: 200);
        }
    }
}
