using System;
using System.Security.Cryptography;
using NSec.Cryptography;

namespace AcornDB.Security;

/// <summary>
/// Ed25519 based policy signer for cryptographically signed policy chains.
/// Provides asymmetric signature verification using elliptic curve cryptography.
/// </summary>
/// <remarks>
/// <para>
/// Ed25519 provides 128-bit security level with fast signature generation and verification.
/// Unlike SHA-256 hashing, Ed25519 provides non-repudiation - only the holder of the
/// private key can create valid signatures.
/// </para>
/// <para>
/// <b>Key Management:</b> Keys must be injected via constructor. This class does not
/// generate or store keys. Support key rotation by creating a new signer instance.
/// </para>
/// </remarks>
public sealed class Ed25519PolicySigner : IPolicySigner
{
    private readonly Key? _privateKey;
    private readonly PublicKey _publicKey;
    private static readonly SignatureAlgorithm Ed25519Algorithm = SignatureAlgorithm.Ed25519;

    /// <summary>
    /// Creates a signer with both private and public keys (for signing and verification).
    /// </summary>
    /// <param name="privateKeyBytes">32-byte Ed25519 private key seed.</param>
    /// <exception cref="ArgumentException">If key is null or invalid length.</exception>
    public Ed25519PolicySigner(byte[] privateKeyBytes)
    {
        if (privateKeyBytes is null)
            throw new ArgumentException("Private key cannot be null.", nameof(privateKeyBytes));

        if (privateKeyBytes.Length != 32)
            throw new ArgumentException("Ed25519 private key seed must be exactly 32 bytes.", nameof(privateKeyBytes));

        _privateKey = Key.Import(Ed25519Algorithm, privateKeyBytes, KeyBlobFormat.RawPrivateKey);
        _publicKey = _privateKey.PublicKey;
    }

    /// <summary>
    /// Creates a verifier with only the public key (for verification only).
    /// </summary>
    /// <param name="publicKeyBytes">32-byte Ed25519 public key.</param>
    /// <param name="verifyOnly">Must be true to indicate verify-only mode.</param>
    /// <exception cref="ArgumentException">If key is null or invalid length.</exception>
    public Ed25519PolicySigner(byte[] publicKeyBytes, bool verifyOnly)
    {
        if (!verifyOnly)
            throw new ArgumentException("Use the single-parameter constructor for signing mode.", nameof(verifyOnly));

        if (publicKeyBytes is null)
            throw new ArgumentException("Public key cannot be null.", nameof(publicKeyBytes));

        if (publicKeyBytes.Length != 32)
            throw new ArgumentException("Ed25519 public key must be exactly 32 bytes.", nameof(publicKeyBytes));

        _privateKey = null;
        _publicKey = PublicKey.Import(Ed25519Algorithm, publicKeyBytes, KeyBlobFormat.RawPublicKey);
    }

    /// <inheritdoc />
    public string Algorithm => "Ed25519";

    /// <inheritdoc />
    public byte[] Sign(byte[] data)
    {
        if (data is null)
            throw new ArgumentException("Data to sign cannot be null.", nameof(data));

        if (_privateKey is null)
            throw new InvalidOperationException("Cannot sign without a private key. This signer is in verify-only mode.");

        return Ed25519Algorithm.Sign(_privateKey, data);
    }

    /// <inheritdoc />
    public bool Verify(byte[] data, byte[] signature)
    {
        if (data is null)
            throw new ArgumentException("Data to verify cannot be null.", nameof(data));

        if (signature is null)
            throw new ArgumentException("Signature to verify cannot be null.", nameof(signature));

        if (signature.Length != 64)
            return false;

        return Ed25519Algorithm.Verify(_publicKey, data, signature);
    }

    /// <summary>
    /// Gets the public key bytes for sharing with verifiers.
    /// </summary>
    /// <returns>32-byte Ed25519 public key.</returns>
    public byte[] GetPublicKey()
    {
        return _publicKey.Export(KeyBlobFormat.RawPublicKey);
    }
}
