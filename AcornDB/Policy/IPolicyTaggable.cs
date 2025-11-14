using System.Collections.Generic;

namespace AcornDB.Policy
{
    /// <summary>
    /// Interface for entities that support tag-based policy enforcement.
    /// Tags are used for access control, caching, TTL enforcement, and data classification.
    /// </summary>
    public interface IPolicyTaggable
    {
        /// <summary>
        /// Gets the collection of tags associated with this entity.
        /// Tags drive policy enforcement, access control, and cache invalidation.
        /// </summary>
        IEnumerable<string> Tags { get; }

        /// <summary>
        /// Checks if the entity has a specific tag.
        /// </summary>
        /// <param name="tag">The tag to check for</param>
        /// <returns>True if the entity has the tag, false otherwise</returns>
        bool HasTag(string tag);
    }
}
