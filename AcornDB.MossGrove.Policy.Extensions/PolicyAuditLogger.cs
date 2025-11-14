using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using AcornDB.Policy;

namespace AcornDB.MossGrove.Policy.Extensions
{
    /// <summary>
    /// EXTENSION: Audit logger that subscribes to policy evaluation events.
    /// Part of AcornDB.MossGrove.Policy.Extensions - advanced logging for compliance and debugging.
    /// Provides structured logging of policy decisions to files, streams, or custom handlers.
    /// </summary>
    public class PolicyAuditLogger : IDisposable
    {
        private readonly PolicyAuditLoggerOptions _options;
        private readonly object _lock = new object();
        private StreamWriter? _fileWriter;
        private bool _disposed = false;

        /// <summary>
        /// Event raised when an audit entry is logged (for chaining to other systems)
        /// </summary>
        public event Action<PolicyAuditEntry>? AuditEntryLogged;

        /// <summary>
        /// Creates a new PolicyAuditLogger with specified options
        /// </summary>
        public PolicyAuditLogger(PolicyAuditLoggerOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            if (_options.LogToFile && !string.IsNullOrEmpty(_options.LogFilePath))
            {
                InitializeFileLogging();
            }
        }

        /// <summary>
        /// Creates a new PolicyAuditLogger with default options (in-memory only)
        /// </summary>
        public PolicyAuditLogger() : this(new PolicyAuditLoggerOptions())
        {
        }

        /// <summary>
        /// Attach this logger to a LocalPolicyEngine's PolicyEvaluated event
        /// </summary>
        public void AttachToEngine(LocalPolicyEngine engine)
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));

            engine.PolicyEvaluated += OnPolicyEvaluated;
        }

        /// <summary>
        /// Detach this logger from a LocalPolicyEngine's PolicyEvaluated event
        /// </summary>
        public void DetachFromEngine(LocalPolicyEngine engine)
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));

            engine.PolicyEvaluated -= OnPolicyEvaluated;
        }

        private void OnPolicyEvaluated(PolicyEvaluationResult result)
        {
            if (result == null) return;

            // Filter based on options
            if (!_options.LogSuccesses && result.Passed)
                return;

            if (!_options.LogFailures && !result.Passed)
                return;

            var auditEntry = new PolicyAuditEntry
            {
                Timestamp = DateTime.UtcNow,
                Passed = result.Passed,
                Reason = result.Reason,
                Actions = result.Actions
            };

            LogAuditEntry(auditEntry);
        }

        private void LogAuditEntry(PolicyAuditEntry entry)
        {
            lock (_lock)
            {
                if (_disposed) return;

                // Log to file if enabled
                if (_options.LogToFile && _fileWriter != null)
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false });
                        _fileWriter.WriteLine(json);
                        _fileWriter.Flush();
                    }
                    catch (Exception ex)
                    {
                        // Silently fail or handle logging errors
                        if (_options.ThrowOnError)
                            throw new PolicyAuditException("Failed to write audit log", ex);
                    }
                }

                // Log to console if enabled
                if (_options.LogToConsole)
                {
                    var status = entry.Passed ? "PASS" : "FAIL";
                    Console.WriteLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] Policy {status}: {entry.Reason}");
                }

                // Raise event for custom handlers
                AuditEntryLogged?.Invoke(entry);
            }
        }

        private void InitializeFileLogging()
        {
            try
            {
                var directory = Path.GetDirectoryName(_options.LogFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _fileWriter = new StreamWriter(_options.LogFilePath, append: _options.AppendToFile, Encoding.UTF8)
                {
                    AutoFlush = true
                };
            }
            catch (Exception ex)
            {
                if (_options.ThrowOnError)
                    throw new PolicyAuditException("Failed to initialize file logging", ex);
            }
        }

        /// <summary>
        /// Dispose of resources (closes file handles)
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            lock (_lock)
            {
                _fileWriter?.Dispose();
                _fileWriter = null;
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// EXTENSION: Configuration options for PolicyAuditLogger
    /// </summary>
    public class PolicyAuditLoggerOptions
    {
        /// <summary>
        /// Log successful policy evaluations (default: true)
        /// </summary>
        public bool LogSuccesses { get; set; } = true;

        /// <summary>
        /// Log failed policy evaluations (default: true)
        /// </summary>
        public bool LogFailures { get; set; } = true;

        /// <summary>
        /// Write audit logs to file (default: false)
        /// </summary>
        public bool LogToFile { get; set; } = false;

        /// <summary>
        /// Write audit logs to console (default: false)
        /// </summary>
        public bool LogToConsole { get; set; } = false;

        /// <summary>
        /// File path for audit logs (required if LogToFile is true)
        /// </summary>
        public string LogFilePath { get; set; } = "policy_audit.log";

        /// <summary>
        /// Append to existing log file instead of overwriting (default: true)
        /// </summary>
        public bool AppendToFile { get; set; } = true;

        /// <summary>
        /// Throw exceptions on logging errors instead of silently failing (default: false)
        /// </summary>
        public bool ThrowOnError { get; set; } = false;
    }

    /// <summary>
    /// EXTENSION: Represents an audit entry for a policy evaluation
    /// </summary>
    public class PolicyAuditEntry
    {
        public DateTime Timestamp { get; set; }
        public bool Passed { get; set; }
        public string? Reason { get; set; }
        public List<string> Actions { get; set; } = new();
    }

    /// <summary>
    /// EXTENSION: Exception thrown when audit logging fails
    /// </summary>
    public class PolicyAuditException : Exception
    {
        public PolicyAuditException(string message) : base(message) { }
        public PolicyAuditException(string message, Exception innerException) : base(message, innerException) { }
    }
}
