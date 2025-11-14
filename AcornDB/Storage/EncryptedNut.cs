namespace AcornDB.Storage
{
    /// <summary>
    /// Wrapper for encrypted payload data
    /// </summary>
    public class EncryptedNut
    {
        public string EncryptedData { get; set; } = "";
        public string OriginalType { get; set; } = "";
    }
}
