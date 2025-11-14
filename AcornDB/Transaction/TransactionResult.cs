namespace AcornDB.Transaction
{
    /// <summary>
    /// Transaction result information
    /// </summary>
    public class TransactionResult
    {
        public bool Success { get; set; }
        public int OperationCount { get; set; }
        public bool RolledBack { get; set; }
    }
}
