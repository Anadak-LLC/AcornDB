using System;

namespace AcornDB.Security
{
    /// <summary>
    /// Computes and verifies cryptographic signatures for policy content.
    /// Used by IPolicyLog to ensure chain integrity and tamper detection.
    /// </summary>
    /// <remarks>
    /// Implementations must be deterministic: same input always produces same signature.
    /// For hash-based signers (SHA-256), this is keyless integrity verification.
    /// For asymmetric signers (Ed25519), keys are managed externally and injected.
    /// </remarks>
    public interface IPolicySigner
    {
        /// <summary>
        /// Compute cryptographic signature for the given data.
        /// </summary>
        /// <param name="data">Raw bytes to sign. Must not be null.</param>
        /// <returns>Signature bytes (length depends on algorithm).</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        byte[] Sign(byte[] data);

        /// <summary>
        /// Verify that signature matches data using constant-time comparison.
        /// </summary>
        /// <param name="data">Original data. Must not be null.</param>
        /// <param name="signature">Signature to verify. Must not be null.</param>
        /// <returns>True if signature is valid, false otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data or signature is null.</exception>
        bool Verify(byte[] data, byte[] signature);

        /// <summary>
        /// Algorithm identifier for metadata and logging purposes.
        /// </summary>
        /// <example>"SHA256", "Ed25519", "ECDSA-P256"</example>
        string Algorithm { get; }
    }
}
