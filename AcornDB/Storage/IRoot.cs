using AcornDB.Policy;

namespace AcornDB.Storage;

/// <summary>
/// Root processor interface for byte-level transformations in the storage pipeline.
/// Roots form a processing chain that transforms serialized data before write (OnStash)
/// and after read (OnCrack).
///
/// Processing Flow:
/// Write (Stash): Nut → Serialize to bytes → Root chain (ascending) → Storage
/// Read (Crack): Storage → Root chain (descending/reverse) → Deserialize to Nut
///
/// Processing Order:
/// - Write (Stash): Roots execute in ascending Sequence order on byte array
/// - Read (Crack): Roots execute in descending Sequence order on byte array
///
/// Example Use Cases:
/// - Compression (sequence 100): Compress/decompress byte streams
/// - Encryption (sequence 200): Encrypt/decrypt byte streams
/// - Checksumming (sequence 300): Add/validate checksums
/// - Signing (sequence 350): Add/verify digital signatures
/// </summary>
public interface IRoot
{
    /// <summary>
    /// Unique name identifying this root processor
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Execution order for this root.
    /// Lower sequences execute first on write (OnStash).
    /// Higher sequences execute first on read (OnCrack).
    ///
    /// Recommended ranges:
    /// - 50-99: Pre-processing (validation, normalization)
    /// - 100-199: Compression
    /// - 200-299: Encryption
    /// - 300-399: Checksumming/Hashing
    /// - 400-499: Digital signatures
    /// </summary>
    int Sequence { get; }

    /// <summary>
    /// Get a signature/descriptor for this processor to include in metadata.
    /// Used by trunk to optionally track which transformations were applied.
    /// Example: "gzip:optimal", "aes256:cbc", "sha256"
    /// </summary>
    string GetSignature();

    /// <summary>
    /// Transform bytes before storage (compress, encrypt, etc.).
    /// Called during Save/Stash operations.
    /// Executes in ascending sequence order.
    /// </summary>
    /// <param name="data">Serialized data to transform</param>
    /// <param name="context">Processing context for policy enforcement and metadata</param>
    /// <returns>Transformed bytes to pass to next root or storage</returns>
    byte[] OnStash(byte[] data, RootProcessingContext context);

    /// <summary>
    /// Restore bytes after retrieval (decompress, decrypt, etc.).
    /// Called during Load/Crack operations.
    /// Executes in descending sequence order (reverse of OnStash).
    /// </summary>
    /// <param name="data">Data retrieved from storage or previous root</param>
    /// <param name="context">Processing context for policy enforcement and metadata</param>
    /// <returns>Restored bytes to pass to next root or deserializer</returns>
    byte[] OnCrack(byte[] data, RootProcessingContext context);
}
