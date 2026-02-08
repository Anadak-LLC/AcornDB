namespace AcornDB.Policy.Governance;

/// <summary>
/// Result of chain integrity verification.
/// </summary>
public sealed record ChainValidationResult
{
    /// <summary>Whether the chain is valid.</summary>
    public bool IsValid { get; init; }

    /// <summary>Index where chain broke (if invalid).</summary>
    public int? BrokenAtIndex { get; init; }

    /// <summary>Details about the validation failure.</summary>
    public string? Details { get; init; }

    /// <summary>Create a valid result.</summary>
    public static ChainValidationResult Valid()
        => new() { IsValid = true };

    /// <summary>Create an invalid result.</summary>
    public static ChainValidationResult Invalid(int index, string details)
        => new() { IsValid = false, BrokenAtIndex = index, Details = details };
}
