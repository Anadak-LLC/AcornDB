using System;

namespace AcornDB.Policy.Governance;

/// <summary>
/// Exception thrown when the policy chain integrity verification fails.
/// Indicates potential tampering or corruption in the governance ledger.
/// </summary>
public class ChainIntegrityException : Exception
{
    /// <summary>
    /// Index where the chain integrity was broken (if known).
    /// </summary>
    public int? BrokenAtIndex { get; }

    /// <summary>
    /// Creates a new ChainIntegrityException with the specified message.
    /// </summary>
    /// <param name="message">Description of the integrity failure.</param>
    public ChainIntegrityException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new ChainIntegrityException with index information.
    /// </summary>
    /// <param name="message">Description of the integrity failure.</param>
    /// <param name="brokenAtIndex">Index where chain broke.</param>
    public ChainIntegrityException(string message, int brokenAtIndex) : base(message)
    {
        BrokenAtIndex = brokenAtIndex;
    }

    /// <summary>
    /// Creates a new ChainIntegrityException with an inner exception.
    /// </summary>
    /// <param name="message">Description of the integrity failure.</param>
    /// <param name="innerException">The underlying exception.</param>
    public ChainIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
