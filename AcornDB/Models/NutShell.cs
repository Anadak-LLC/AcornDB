using System;

namespace AcornDB
{
    public partial class NutShell<T>
    {
        public string Id { get; set; } = string.Empty;
        public T Payload { get; set; } = default!;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
        public int Version { get; set; } = 1;

        // Alias properties for compatibility
        public T Value
        {
            get => Payload;
            set => Payload = value;
        }

        public T Nut
        {
            get => Payload;
            set => Payload = value;
        }
    }
}
