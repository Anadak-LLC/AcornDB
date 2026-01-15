using System;
using System.Text;
using AcornDB.Security;
using Xunit;

namespace AcornDB.Test.Security;

/// <summary>
/// Unit tests for <see cref="Ed25519PolicySigner"/>.
/// </summary>
public class Ed25519PolicySignerTests
{
    // Test key pair (32-byte seed generates consistent key pair)
    private static readonly byte[] TestPrivateKey = new byte[32]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20
    };

    private readonly Ed25519PolicySigner _signer;
    private readonly byte[] _publicKey;

    public Ed25519PolicySignerTests()
    {
        _signer = new Ed25519PolicySigner(TestPrivateKey);
        _publicKey = _signer.GetPublicKey();
    }

    [Fact]
    public void Sign_ReturnsConsistentSignature_ForSameInput()
    {
        var data = Encoding.UTF8.GetBytes("Policy content to sign");

        var signature1 = _signer.Sign(data);
        var signature2 = _signer.Sign(data);

        Assert.Equal(64, signature1.Length); // Ed25519 signatures are 64 bytes
        Assert.Equal(signature1, signature2);
    }

    [Fact]
    public void Sign_ReturnsDifferentSignature_ForDifferentInput()
    {
        var data1 = Encoding.UTF8.GetBytes("Policy A");
        var data2 = Encoding.UTF8.GetBytes("Policy B");

        var signature1 = _signer.Sign(data1);
        var signature2 = _signer.Sign(data2);

        Assert.NotEqual(signature1, signature2);
    }

    [Fact]
    public void Verify_ReturnsTrue_ForValidSignature()
    {
        var data = Encoding.UTF8.GetBytes("Valid policy content");
        var signature = _signer.Sign(data);

        var isValid = _signer.Verify(data, signature);

        Assert.True(isValid);
    }

    [Fact]
    public void Verify_ReturnsFalse_ForTamperedData()
    {
        var originalData = Encoding.UTF8.GetBytes("Original policy");
        var signature = _signer.Sign(originalData);
        var tamperedData = Encoding.UTF8.GetBytes("Tampered policy");

        var isValid = _signer.Verify(tamperedData, signature);

        Assert.False(isValid);
    }

    [Fact]
    public void Verify_ReturnsFalse_ForTamperedSignature()
    {
        var data = Encoding.UTF8.GetBytes("Policy content");
        var signature = _signer.Sign(data);
        var tamperedSignature = (byte[])signature.Clone();
        tamperedSignature[0] ^= 0xFF;

        var isValid = _signer.Verify(data, tamperedSignature);

        Assert.False(isValid);
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongLengthSignature()
    {
        var data = Encoding.UTF8.GetBytes("Policy content");

        var isValid = _signer.Verify(data, new byte[32]); // Wrong length

        Assert.False(isValid);
    }

    [Fact]
    public void Algorithm_ReturnsEd25519()
    {
        Assert.Equal("Ed25519", _signer.Algorithm);
    }

    [Fact]
    public void Constructor_ThrowsOnNullPrivateKey()
    {
        Assert.Throws<ArgumentException>(() => new Ed25519PolicySigner(null!));
    }

    [Fact]
    public void Constructor_ThrowsOnWrongLengthPrivateKey()
    {
        Assert.Throws<ArgumentException>(() => new Ed25519PolicySigner(new byte[16]));
    }

    [Fact]
    public void VerifyOnly_CanVerifySignatures()
    {
        var data = Encoding.UTF8.GetBytes("Test data");
        var signature = _signer.Sign(data);

        var verifyOnlySigner = new Ed25519PolicySigner(_publicKey, verifyOnly: true);
        var isValid = verifyOnlySigner.Verify(data, signature);

        Assert.True(isValid);
    }

    [Fact]
    public void VerifyOnly_CannotSign()
    {
        var verifyOnlySigner = new Ed25519PolicySigner(_publicKey, verifyOnly: true);
        var data = Encoding.UTF8.GetBytes("Test data");

        Assert.Throws<InvalidOperationException>(() => verifyOnlySigner.Sign(data));
    }

    [Fact]
    public void VerifyOnly_ThrowsOnNullPublicKey()
    {
        Assert.Throws<ArgumentException>(() => new Ed25519PolicySigner(null!, verifyOnly: true));
    }

    [Fact]
    public void VerifyOnly_ThrowsOnWrongLengthPublicKey()
    {
        Assert.Throws<ArgumentException>(() => new Ed25519PolicySigner(new byte[16], verifyOnly: true));
    }

    [Fact]
    public void GetPublicKey_Returns32Bytes()
    {
        var publicKey = _signer.GetPublicKey();

        Assert.Equal(32, publicKey.Length);
    }

    [Fact]
    public void DifferentKeys_ProduceDifferentSignatures()
    {
        var otherPrivateKey = new byte[32];
        Array.Fill<byte>(otherPrivateKey, 0xAB);
        var otherSigner = new Ed25519PolicySigner(otherPrivateKey);

        var data = Encoding.UTF8.GetBytes("Same data");
        var sig1 = _signer.Sign(data);
        var sig2 = otherSigner.Sign(data);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void CrossKeyVerification_Fails()
    {
        var otherPrivateKey = new byte[32];
        Array.Fill<byte>(otherPrivateKey, 0xCD);
        var otherSigner = new Ed25519PolicySigner(otherPrivateKey);

        var data = Encoding.UTF8.GetBytes("Test data");
        var signature = _signer.Sign(data);

        // Signature from _signer should not verify with otherSigner's key
        Assert.False(otherSigner.Verify(data, signature));
    }

    [Fact]
    public void Sign_ThrowsOnNullData()
    {
        Assert.Throws<ArgumentException>(() => _signer.Sign(null!));
    }

    [Fact]
    public void Verify_ThrowsOnNullData()
    {
        Assert.Throws<ArgumentException>(() => _signer.Verify(null!, new byte[64]));
    }

    [Fact]
    public void Verify_ThrowsOnNullSignature()
    {
        var data = Encoding.UTF8.GetBytes("Test data");
        Assert.Throws<ArgumentException>(() => _signer.Verify(data, null!));
    }

    [Fact]
    public void WorksAsDropInReplacement_ForIPolicySigner()
    {
        IPolicySigner signer = new Ed25519PolicySigner(TestPrivateKey);
        var data = Encoding.UTF8.GetBytes("Policy content");

        var signature = signer.Sign(data);
        var isValid = signer.Verify(data, signature);

        Assert.True(isValid);
        Assert.Equal("Ed25519", signer.Algorithm);
    }
}
