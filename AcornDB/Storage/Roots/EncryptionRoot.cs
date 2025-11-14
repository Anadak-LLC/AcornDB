using System;
using AcornDB.Logging;
using System.Text;
using AcornDB.Security;

namespace AcornDB.Storage.Roots
{
    /// <summary>
    /// Root processor that encrypts byte streams before storage and decrypts on retrieval.
    /// Provides data-at-rest encryption for sensitive information.
    /// Works at the byte level - completely agnostic to data type.
    /// Recommended sequence: 200-299
    /// </summary>
    public class EncryptionRoot : IRoot
    {
        private readonly IEncryptionProvider _encryption;
        private readonly EncryptionMetrics _metrics;
        private readonly string _algorithmName;

        public string Name => "Encryption";
        public int Sequence { get; }

        /// <summary>
        /// Encryption metrics for monitoring
        /// </summary>
        public EncryptionMetrics Metrics => _metrics;

        public EncryptionRoot(
            IEncryptionProvider encryption,
            int sequence = 200,
            string? algorithmName = null)
        {
            _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
            Sequence = sequence;
            _algorithmName = algorithmName ?? "aes256"; // Default algorithm name
            _metrics = new EncryptionMetrics();
        }

        public string GetSignature()
        {
            return _algorithmName;
        }

        public byte[] OnStash(byte[] data, RootProcessingContext context)
        {
            try
            {
                // Convert bytes to base64 string for encryption provider
                // (Most encryption providers work with strings)
                var base64Data = Convert.ToBase64String(data);

                // Encrypt
                var encrypted = _encryption.Encrypt(base64Data);

                // Convert back to bytes
                var encryptedBytes = Encoding.UTF8.GetBytes(encrypted);

                // Update metrics
                _metrics.RecordEncryption();

                // Add signature to transformation chain
                context.TransformationSignatures.Add(GetSignature());

                return encryptedBytes;
            }
            catch (Exception ex)
            {
                _metrics.RecordError();
                AcornLog.Info($"⚠️ Encryption failed for document '{context.DocumentId}': {ex.Message}");
                throw new InvalidOperationException($"Failed to encrypt data", ex);
            }
        }

        public byte[] OnCrack(byte[] data, RootProcessingContext context)
        {
            try
            {
                // Convert bytes to string
                var encryptedString = Encoding.UTF8.GetString(data);

                // Decrypt
                var decrypted = _encryption.Decrypt(encryptedString);

                // Convert from base64 back to original bytes
                var decryptedBytes = Convert.FromBase64String(decrypted);

                // Update metrics
                _metrics.RecordDecryption();

                return decryptedBytes;
            }
            catch (Exception ex)
            {
                _metrics.RecordError();
                AcornLog.Info($"⚠️ Decryption failed for document '{context.DocumentId}': {ex.Message}");
                throw new InvalidOperationException($"Failed to decrypt data", ex);
            }
        }
    }
}
