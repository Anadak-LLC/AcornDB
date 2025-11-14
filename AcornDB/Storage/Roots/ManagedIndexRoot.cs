using System;
using AcornDB.Logging;
using AcornDB.Indexing;

namespace AcornDB.Storage.Roots
{
    /// <summary>
    /// [DEPRECATED] Root processor that tracks index metadata and provides hooks for index-aware operations.
    /// This root operates at sequence 50 (pre-compression) to access uncompressed document data.
    ///
    /// ⚠️ IMPORTANT: This class is DEPRECATED and will be removed in v0.6.0.
    ///
    /// Reason for deprecation:
    /// - Does not actually manage indexes (indexes are managed at Tree level in Tree.IndexManagement.cs)
    /// - Misleading name suggests index management when it only tracks metrics
    /// - Root processors should transform bytes, not just observe operations
    /// - Index metrics belong at Tree level where actual index updates occur
    ///
    /// Migration:
    /// - If you need index metrics, use Tree.GetNutStats() which provides comprehensive statistics
    /// - Index updates are automatically tracked in Tree.IndexManagement.cs
    /// - Root processors should focus on byte-level transformations (compression, encryption, etc.)
    ///
    /// Recommended sequence: 50 (before compression)
    /// </summary>
    [Obsolete("ManagedIndexRoot is deprecated and will be removed in v0.6.0. Index metrics are tracked at Tree level. " +
              "Use Tree.GetNutStats() for statistics. Root processors should transform bytes, not just observe operations.", false)]
    public class ManagedIndexRoot : IRoot
    {
        private readonly ManagedIndexMetrics _metrics;

        public string Name => "ManagedIndex";
        public int Sequence { get; }

        /// <summary>
        /// Metrics for index tracking operations
        /// </summary>
        public ManagedIndexMetrics Metrics => _metrics;

        public ManagedIndexRoot(int sequence = 50)
        {
            Sequence = sequence;
            _metrics = new ManagedIndexMetrics();
        }

        public string GetSignature()
        {
            return "managed-index:v1";
        }

        public byte[] OnStash(byte[] data, RootProcessingContext context)
        {
            try
            {
                // Track that this document passed through indexing pipeline
                _metrics.RecordStash(context.DocumentId);

                // Add index signature to transformation chain
                context.TransformationSignatures.Add(GetSignature());

                // Store document ID in metadata for downstream processors
                context.Metadata["IndexedDocumentId"] = context.DocumentId ?? "unknown";
                context.Metadata["IndexedAt"] = DateTime.UtcNow;

                // Pass through unchanged - index updates happen at Tree level
                return data;
            }
            catch (Exception ex)
            {
                _metrics.RecordError();
                AcornLog.Info($"⚠️ Index root processing failed for document '{context.DocumentId}': {ex.Message}");
                // Don't throw - indexing failures shouldn't break writes
                return data;
            }
        }

        public byte[] OnCrack(byte[] data, RootProcessingContext context)
        {
            try
            {
                // Track document retrieval
                _metrics.RecordCrack(context.DocumentId);

                // Extract index metadata if present
                if (context.Metadata.TryGetValue("IndexedDocumentId", out var docId))
                {
                    context.Metadata["RecoveredDocumentId"] = docId;
                }

                // Pass through unchanged - this root is for tracking only
                return data;
            }
            catch (Exception ex)
            {
                _metrics.RecordError();
                AcornLog.Info($"⚠️ Index root retrieval failed for document '{context.DocumentId}': {ex.Message}");
                // Don't throw - indexing failures shouldn't break reads
                return data;
            }
        }
    }
}
