using System;

namespace AcornDB.Reactive
{
    /// <summary>
    /// Represents a change in a tree
    /// </summary>
    public class TreeChange<T> where T : class
    {
        public ChangeType ChangeType { get; set; }
        public string Id { get; set; } = "";
        public T? Item { get; set; }
        public Nut<T>? Nut { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? NodeId { get; set; }
    }

    /// <summary>
    /// Type of change operation
    /// </summary>
    public enum ChangeType
    {
        Stash,      // Create or update
        Toss,       // Delete
        Squabble    // Conflict resolution
    }
}
