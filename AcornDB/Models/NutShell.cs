using System;

namespace AcornDB.Models
{
    public class NutShell<T>
    {
        public T Nut { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UpdatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public int Version { get; set; } = 1;
    }
}
