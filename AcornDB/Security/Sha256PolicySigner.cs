using System;
using System.Security.Cryptography;

namespace AcornDB.Security
{
    /// <summary>
    /// SHA-256 based policy signer for tamper-evident hash chains.
    /// Provides keyless integrity verification using FIPS 180-4 compliant hashing.
    /// </summary>
    /// <remarks>
    /// This is a deterministic hash-based signer (not a cryptographic signature).
    /// Same input always produces the same 32-byte (256-bit) output.
    /// Uses <see cref="CryptographicOperations.FixedTimeEquals"/> to prevent timing attacks.
    /// </remarks>
    public sealed class Sha256PolicySigner : IPolicySigner
    {
        /// <summary>
        /// SHA-256 output size in bytes (256 bits = 32 bytes).
        /// </summary>
        public const int SignatureLength = 32;

        /// <inheritdoc />
        public string Algorithm => "SHA256";

        /// <inheritdoc />
        /// <returns>32-byte SHA-256 hash of the input data.</returns>
        public byte[] Sign(byte[] data)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data), "Data to sign cannot be null.");
            }

            return SHA256.HashData(data);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Uses constant-time comparison to prevent timing attacks.
        /// Returns false if signature length is not 32 bytes.
        /// </remarks>
        public bool Verify(byte[] data, byte[] signature)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data), "Data to verify cannot be null.");
            }

            if (signature is null)
            {
                throw new ArgumentNullException(nameof(signature), "Signature to verify cannot be null.");
            }

            if (signature.Length != SignatureLength)
            {
                return false;
            }

            var computed = SHA256.HashData(data);
            return CryptographicOperations.FixedTimeEquals(computed, signature);
        }
    }
}
