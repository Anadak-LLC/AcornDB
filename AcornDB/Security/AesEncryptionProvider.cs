using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AcornDB.Security
{
    /// <summary>
    /// AES-256 encryption provider with automatic key generation
    /// </summary>
    public class AesEncryptionProvider : IEncryptionProvider
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;

        public bool IsEnabled => true;

        /// <summary>
        /// Create with explicit key and IV (for production - store these securely!)
        /// </summary>
        public AesEncryptionProvider(byte[] key, byte[] iv)
        {
            if (key.Length != 32)
                throw new ArgumentException("Key must be 32 bytes (256 bits)", nameof(key));
            if (iv.Length != 16)
                throw new ArgumentException("IV must be 16 bytes (128 bits)", nameof(iv));

            _key = key;
            _iv = iv;
        }

        /// <summary>
        /// Create with password-based key derivation (PBKDF2)
        /// </summary>
        public static AesEncryptionProvider FromPassword(string password, string salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes(salt), 10000, HashAlgorithmName.SHA256);
            var key = pbkdf2.GetBytes(32); // 256-bit key
            var iv = pbkdf2.GetBytes(16);  // 128-bit IV
            return new AesEncryptionProvider(key, iv);
        }

        /// <summary>
        /// Generate random key and IV (for testing or new deployments)
        /// IMPORTANT: Store the returned key/IV securely - data cannot be decrypted without them
        /// </summary>
        public static (byte[] key, byte[] iv) GenerateKeyAndIV()
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.GenerateKey();
            aes.GenerateIV();
            return (aes.Key, aes.IV);
        }

        public string Encrypt(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return plaintext;

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var writer = new StreamWriter(cs))
            {
                writer.Write(plaintext);
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        public string Decrypt(string ciphertext)
        {
            if (string.IsNullOrEmpty(ciphertext))
                return ciphertext;

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(Convert.FromBase64String(ciphertext));
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var reader = new StreamReader(cs);

            return reader.ReadToEnd();
        }

        /// <summary>
        /// Export key as base64 string (for backup/storage)
        /// </summary>
        public string ExportKeyBase64() => Convert.ToBase64String(_key);

        /// <summary>
        /// Export IV as base64 string (for backup/storage)
        /// </summary>
        public string ExportIVBase64() => Convert.ToBase64String(_iv);
    }
}
