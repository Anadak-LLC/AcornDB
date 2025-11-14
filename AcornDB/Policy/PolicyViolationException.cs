using System;

namespace AcornDB.Policy
{
    /// <summary>
    /// CORE EXCEPTION: Exception thrown when a policy is violated.
    /// Part of AcornDB.Core - lightweight, dependency-free enforcement.
    /// </summary>
    public class PolicyViolationException : Exception
    {
        /// <summary>
        /// Creates a new PolicyViolationException with the specified message
        /// </summary>
        public PolicyViolationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates a new PolicyViolationException with the specified message and inner exception
        /// </summary>
        public PolicyViolationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
