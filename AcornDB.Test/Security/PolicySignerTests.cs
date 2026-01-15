using System;
using System.Text;
using AcornDB.Security;
using Xunit;

namespace AcornDB.Test.Security;

/// <summary>
/// Unit tests for <see cref="Sha256PolicySigner"/>.
/// </summary>
public class PolicySignerTests
{
    private readonly Sha256PolicySigner _signer = new();

    [Fact]
    public void Sign_ReturnsConsistentHash_ForSameInput()
    {
        var data = Encoding.UTF8.GetBytes("Policy content to sign");

        var signature1 = _signer.Sign(data);
        var signature2 = _signer.Sign(data);

        Assert.Equal(32, signature1.Length);
        Assert.Equal(signature1, signature2);
    }

    [Fact]
    public void Sign_ReturnsDifferentHash_ForDifferentInput()
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
    public void Algorithm_ReturnsSHA256()
    {
        Assert.Equal("SHA256", _signer.Algorithm);
    }
}
