namespace AcornDB.Security;

/// <summary>
/// Computes and verifies cryptographic signatures for policy content.
/// Used by IPolicyLog to ensure chain integrity.
/// </summary>
public interface IPolicySigner
{
    /// <summary>Compute cryptographic signature for the given data.</summary>
    /// <param name="data">Raw bytes to sign.</param>
    /// <returns>Signature bytes.</returns>
    byte[] Sign(byte[] data);

    /// <summary>Verify that signature matches data.</summary>
    /// <param name="data">Original data.</param>
    /// <param name="signature">Signature to verify.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool Verify(byte[] data, byte[] signature);

    /// <summary>Algorithm identifier for metadata/logging.</summary>
    string Algorithm { get; }
}
