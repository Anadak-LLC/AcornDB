namespace AcornDB.Storage.Roots
{
    /// <summary>
    /// Metrics for policy enforcement operations
    /// </summary>
    public class PolicyEnforcementMetrics
    {
        public long TotalWriteChecks { get; private set; }
        public long TotalReadChecks { get; private set; }
        public long TotalDenials { get; private set; }
        public long TotalErrors { get; private set; }

        private readonly object _lock = new object();

        internal void RecordSuccess(string operation)
        {
            lock (_lock)
            {
                if (operation == "Write")
                    TotalWriteChecks++;
                else if (operation == "Read")
                    TotalReadChecks++;
            }
        }

        internal void RecordDenial(string operation, string reason)
        {
            lock (_lock)
            {
                TotalDenials++;
                if (operation == "Write")
                    TotalWriteChecks++;
                else if (operation == "Read")
                    TotalReadChecks++;
            }
        }

        internal void RecordError()
        {
            lock (_lock)
            {
                TotalErrors++;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                TotalWriteChecks = 0;
                TotalReadChecks = 0;
                TotalDenials = 0;
                TotalErrors = 0;
            }
        }

        public override string ToString()
        {
            return $"Writes: {TotalWriteChecks}, Reads: {TotalReadChecks}, Denials: {TotalDenials}, Errors: {TotalErrors}";
        }
    }
}
