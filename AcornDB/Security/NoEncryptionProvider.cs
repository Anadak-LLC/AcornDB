namespace AcornDB.Security
{
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
