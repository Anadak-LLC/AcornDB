using System;

namespace AcornDB.Security
{
    /// <summary>
    /// Interface for pluggable encryption providers
    /// </summary>
    public interface IEncryptionProvider
    {
        /// <summary>
        /// Encrypt plaintext data
        /// </summary>
        string Encrypt(string plaintext);

        /// <summary>
        /// Decrypt ciphertext data
        /// </summary>
        string Decrypt(string ciphertext);

        /// <summary>
        /// Check if this provider is configured and ready
        /// </summary>
        bool IsEnabled { get; }
    }

    /// <summary>
    /// No-op encryption provider (passes through data unchanged)
    /// </summary>
    public class NoEncryptionProvider : IEncryptionProvider
    {
        public bool IsEnabled => false;

        public string Encrypt(string plaintext) => plaintext;

        public string Decrypt(string ciphertext) => ciphertext;
    }
}
