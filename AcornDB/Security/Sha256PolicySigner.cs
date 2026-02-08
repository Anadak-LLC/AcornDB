using System;
using System.Security.Cryptography;

namespace AcornDB.Security;

/// <summary>
/// SHA-256 based policy signer for tamper-evident hash chains.
/// Provides keyless integrity verification using FIPS 180-4 compliant hashing.
/// </summary>
public sealed class Sha256PolicySigner : IPolicySigner
{
    /// <inheritdoc />
    public string Algorithm => "SHA256";

    /// <inheritdoc />
    public byte[] Sign(byte[] data)
    {
        if (data is null || data.Length == 0)
            throw new ArgumentException("Data to sign cannot be null or empty.", nameof(data));

        return SHA256.HashData(data);
    }

    /// <inheritdoc />
    public bool Verify(byte[] data, byte[] signature)
    {
        if (data is null)
            throw new ArgumentException("Data to verify cannot be null.", nameof(data));

        if (signature is null)
            throw new ArgumentException("Signature to verify cannot be null.", nameof(signature));

        if (signature.Length != 32)
            return false;

        var computed = SHA256.HashData(data);
        return CryptographicOperations.FixedTimeEquals(computed, signature);
    }
}
