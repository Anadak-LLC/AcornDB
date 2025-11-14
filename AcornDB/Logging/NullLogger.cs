using System;

namespace AcornDB.Logging
{
    /// <summary>
    /// Null logger that discards all log messages (silent mode)
    /// </summary>
    public class NullLogger : ILogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
    }
}
