using System;
using Newtonsoft.Json;

namespace AcornDB
{
    /// <summary>
    /// Nut: A document wrapped with metadata (ID, timestamp, version, TTL)
    /// </summary>
    public partial class Nut<T>
    {
        public string Id { get; set; } = string.Empty;
        public T Payload { get; set; } = default!;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
        public int Version { get; set; } = 1;

        [JsonIgnore]
        // Alias properties for compatibility
        public T Value
        {
            get => Payload;
            set => Payload = value;
        }
    }

    // Backwards compatibility alias
    [Obsolete("Use Nut<T> instead")]
    public partial class NutShell<T> : Nut<T> { }
}
