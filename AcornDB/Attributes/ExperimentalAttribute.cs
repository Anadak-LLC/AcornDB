using System;

namespace AcornDB
{
    /// <summary>
    /// Marks APIs as experimental and subject to change in future releases.
    /// Experimental features may have incomplete implementations or be removed entirely.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public sealed class ExperimentalAttribute : Attribute
    {
        /// <summary>
        /// Optional message explaining the experimental status
        /// </summary>
        public string? Message { get; }

        /// <summary>
        /// Optional version when feature is planned to be stabilized
        /// </summary>
        public string? PlannedVersion { get; }

        /// <summary>
        /// Creates an experimental attribute
        /// </summary>
        public ExperimentalAttribute()
        {
        }

        /// <summary>
        /// Creates an experimental attribute with a message
        /// </summary>
        /// <param name="message">Message explaining the experimental status</param>
        public ExperimentalAttribute(string message)
        {
            Message = message;
        }

        /// <summary>
        /// Creates an experimental attribute with a message and planned version
        /// </summary>
        /// <param name="message">Message explaining the experimental status</param>
        /// <param name="plannedVersion">Version when feature is planned to be stabilized</param>
        public ExperimentalAttribute(string message, string plannedVersion)
        {
            Message = message;
            PlannedVersion = plannedVersion;
        }
    }
}
