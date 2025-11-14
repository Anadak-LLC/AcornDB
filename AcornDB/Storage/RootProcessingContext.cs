using System.Collections.Generic;
using AcornDB.Policy;

namespace AcornDB.Storage
{
    /// <summary>
    /// Context object that flows through the root processing chain.
    /// Contains policy information and tracks the transformation signature chain.
    /// </summary>
    public class RootProcessingContext
    {
        /// <summary>
        /// Policy context for dynamic enforcement during processing
        /// </summary>
        public PolicyContext PolicyContext { get; set; } = new PolicyContext();

        /// <summary>
        /// Ordered list of transformation signatures applied during OnStash.
        /// Built up as data flows through the root chain.
        /// Can be serialized by trunk to track transformations (e.g., ["gzip:optimal", "aes256:cbc"])
        /// </summary>
        public List<string> TransformationSignatures { get; } = new List<string>();

        /// <summary>
        /// Ephemeral metadata for inter-root communication during processing.
        /// Not persisted - only exists during the processing pipeline.
        /// </summary>
        public Dictionary<string, object> Metadata { get; } = new Dictionary<string, object>();

        /// <summary>
        /// Document/Nut ID being processed (for logging/debugging)
        /// </summary>
        public string? DocumentId { get; set; }
    }
}
