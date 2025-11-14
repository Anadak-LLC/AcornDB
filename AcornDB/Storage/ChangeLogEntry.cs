using System;

namespace AcornDB.Storage
{
    public class ChangeLogEntry<T>
    {
        public string Action { get; set; } = "";
        public string Id { get; set; } = "";
        public Nut<T>? Shell { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
