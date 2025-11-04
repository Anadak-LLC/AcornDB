using System;
using AcornDB.Indexing;

namespace AcornDB.Storage.Roots
{
    /// <summary>
    /// Root processor that tracks index metadata and provides hooks for index-aware operations.
    /// This root operates at sequence 50 (pre-compression) to access uncompressed document data.
    ///
    /// Primary responsibilities:
    /// - Track which documents have been indexed
    /// - Provide metadata for index verification/rebuilding
    /// - Enable future index-aware load optimizations
    ///
    /// Recommended sequence: 50 (before compression)
    /// </summary>
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
                Console.WriteLine($"⚠️ Index root processing failed for document '{context.DocumentId}': {ex.Message}");
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
                Console.WriteLine($"⚠️ Index root retrieval failed for document '{context.DocumentId}': {ex.Message}");
                // Don't throw - indexing failures shouldn't break reads
                return data;
            }
        }
    }

    /// <summary>
    /// Metrics for managed index operations (thread-safe)
    /// </summary>
    public class ManagedIndexMetrics
    {
        private long _totalStashes;
        private long _totalCracks;
        private long _totalErrors;
        private DateTime _lastStash;
        private DateTime _lastCrack;

        public long TotalStashes => Interlocked.Read(ref _totalStashes);
        public long TotalCracks => Interlocked.Read(ref _totalCracks);
        public long TotalErrors => Interlocked.Read(ref _totalErrors);
        public DateTime LastStash => _lastStash;
        public DateTime LastCrack => _lastCrack;

        internal void RecordStash(string? documentId)
        {
            Interlocked.Increment(ref _totalStashes);
            _lastStash = DateTime.UtcNow;
        }

        internal void RecordCrack(string? documentId)
        {
            Interlocked.Increment(ref _totalCracks);
            _lastCrack = DateTime.UtcNow;
        }

        internal void RecordError()
        {
            Interlocked.Increment(ref _totalErrors);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _totalStashes, 0);
            Interlocked.Exchange(ref _totalCracks, 0);
            Interlocked.Exchange(ref _totalErrors, 0);
            _lastStash = DateTime.MinValue;
            _lastCrack = DateTime.MinValue;
        }

        public override string ToString()
        {
            return $"Stashes: {TotalStashes}, Cracks: {TotalCracks}, Errors: {TotalErrors}, " +
                   $"Last Stash: {LastStash:yyyy-MM-dd HH:mm:ss}, Last Crack: {LastCrack:yyyy-MM-dd HH:mm:ss}";
        }
    }
}
