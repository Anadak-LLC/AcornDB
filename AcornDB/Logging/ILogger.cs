using System;

namespace AcornDB.Logging
{
    /// <summary>
    /// Logging abstraction for AcornDB operations
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Log informational message
        /// </summary>
        void Info(string message);

        /// <summary>
        /// Log warning message
        /// </summary>
        void Warning(string message);

        /// <summary>
        /// Log error message
        /// </summary>
        void Error(string message);

        /// <summary>
        /// Log error with exception
        /// </summary>
        void Error(string message, Exception ex);
    }
}
