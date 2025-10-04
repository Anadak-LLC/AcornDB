using System;
using System.Collections.Generic;

namespace AcornDB.Sync
{
    public class ChangeSet<T>
    {
        public List<ChangeEntry<T>> Changes { get; set; } = new();
    }

    public class ChangeEntry<T>
    {
        public int Id { get; set; }
        public string Operation { get; set; } = "insert"; // insert, update, delete
        public Models.NutShell<T>? Payload { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }
}
