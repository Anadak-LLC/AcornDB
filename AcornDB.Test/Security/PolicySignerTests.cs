using System;
using System.Text;
using AcornDB.Security;
using Xunit;

namespace AcornDB.Test.Security
{
    /// <summary>
    /// Unit tests for <see cref="Sha256PolicySigner"/>.
    /// Validates cryptographic correctness, consistency, and security properties.
    /// </summary>
    public class PolicySignerTests
    {
        private readonly Sha256PolicySigner _signer = new();

        [Fact]
        public void Sign_ReturnsConsistentHash_ForSameInput()
        {
            // Arrange
            var data = Encoding.UTF8.GetBytes("Policy content to sign");

            // Act
            var signature1 = _signer.Sign(data);
            var signature2 = _signer.Sign(data);

            // Assert
            Assert.Equal(Sha256PolicySigner.SignatureLength, signature1.Length);
            Assert.Equal(signature1, signature2);
        }

        [Fact]
        public void Sign_ReturnsDifferentHash_ForDifferentInput()
        {
            // Arrange
            var data1 = Encoding.UTF8.GetBytes("Policy A");
            var data2 = Encoding.UTF8.GetBytes("Policy B");

            // Act
            var signature1 = _signer.Sign(data1);
            var signature2 = _signer.Sign(data2);

            // Assert
            Assert.NotEqual(signature1, signature2);
        }

        [Fact]
        public void Verify_ReturnsTrue_ForValidSignature()
        {
            // Arrange
            var data = Encoding.UTF8.GetBytes("Valid policy content");
            var signature = _signer.Sign(data);

            // Act
            var isValid = _signer.Verify(data, signature);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        public void Verify_ReturnsFalse_ForTamperedData()
        {
            // Arrange
            var originalData = Encoding.UTF8.GetBytes("Original policy");
            var signature = _signer.Sign(originalData);
            var tamperedData = Encoding.UTF8.GetBytes("Tampered policy");

            // Act
            var isValid = _signer.Verify(tamperedData, signature);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void Verify_ReturnsFalse_ForTamperedSignature()
        {
            // Arrange
            var data = Encoding.UTF8.GetBytes("Policy content");
            var signature = _signer.Sign(data);
            var tamperedSignature = (byte[])signature.Clone();
            tamperedSignature[0] ^= 0xFF; // Flip bits in first byte

            // Act
            var isValid = _signer.Verify(data, tamperedSignature);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void Algorithm_ReturnsSHA256()
        {
            // Act & Assert
            Assert.Equal("SHA256", _signer.Algorithm);
        }

        [Fact]
        public void Sign_ThrowsArgumentNullException_ForNullData()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => _signer.Sign(null!));
            Assert.Equal("data", ex.ParamName);
        }

        [Fact]
        public void Verify_ThrowsArgumentNullException_ForNullData()
        {
            // Arrange
            var signature = new byte[32];

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => _signer.Verify(null!, signature));
            Assert.Equal("data", ex.ParamName);
        }

        [Fact]
        public void Verify_ThrowsArgumentNullException_ForNullSignature()
        {
            // Arrange
            var data = Encoding.UTF8.GetBytes("Some data");

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => _signer.Verify(data, null!));
            Assert.Equal("signature", ex.ParamName);
        }

        [Fact]
        public void Verify_ReturnsFalse_ForWrongSignatureLength()
        {
            // Arrange
            var data = Encoding.UTF8.GetBytes("Some data");
            var shortSignature = new byte[16]; // Wrong length (should be 32)

            // Act
            var isValid = _signer.Verify(data, shortSignature);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void Sign_ReturnsExactly32Bytes()
        {
            // Arrange
            var smallData = new byte[1];
            var largeData = new byte[10000];

            // Act
            var smallSignature = _signer.Sign(smallData);
            var largeSignature = _signer.Sign(largeData);

            // Assert
            Assert.Equal(32, smallSignature.Length);
            Assert.Equal(32, largeSignature.Length);
        }

        [Fact]
        public void Sign_HandlesEmptyArray()
        {
            // Arrange
            var emptyData = Array.Empty<byte>();

            // Act
            var signature = _signer.Sign(emptyData);

            // Assert
            Assert.Equal(32, signature.Length);
            Assert.True(_signer.Verify(emptyData, signature));
        }
    }
}
