using System;

namespace AcornDB.Logging
{
    /// <summary>
    /// Global logging configuration for AcornDB.
    /// By default, logs to console. Can be configured to use custom logger or disabled entirely.
    ///
    /// Usage:
    /// <code>
    /// // Disable all logging
    /// AcornLog.SetLogger(new NullLogger());
    ///
    /// // Use custom logger
    /// AcornLog.SetLogger(myCustomLogger);
    ///
    /// // Re-enable console logging
    /// AcornLog.SetLogger(new ConsoleLogger());
    /// </code>
    /// </summary>
    public static class AcornLog
    {
        private static ILogger _logger = new ConsoleLogger();

        /// <summary>
        /// Get the current logger instance
        /// </summary>
        public static ILogger Current => _logger;

        /// <summary>
        /// Set a custom logger implementation
        /// </summary>
        /// <param name="logger">Logger to use (pass NullLogger to disable logging)</param>
        public static void SetLogger(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Enable console logging (default)
        /// </summary>
        public static void EnableConsoleLogging()
        {
            _logger = new ConsoleLogger();
        }

        /// <summary>
        /// Disable all logging
        /// </summary>
        public static void DisableLogging()
        {
            _logger = new NullLogger();
        }

        /// <summary>
        /// Log informational message
        /// </summary>
        public static void Info(string message) => _logger.Info(message);

        /// <summary>
        /// Log warning message
        /// </summary>
        public static void Warning(string message) => _logger.Warning(message);

        /// <summary>
        /// Log error message
        /// </summary>
        public static void Error(string message) => _logger.Error(message);

        /// <summary>
        /// Log error with exception
        /// </summary>
        public static void Error(string message, Exception ex) => _logger.Error(message, ex);
    }
}
