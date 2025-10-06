namespace AcornDB.Storage
{
    /// <summary>
    /// In-memory trunk for testing. Non-durable, no history.
    /// </summary>
    public class MemoryTrunk<T> : ITrunk<T>
    {
        private readonly Dictionary<string, Nut<T>> _storage = new();

        public void Save(string id, Nut<T> nut)
        {
            _storage[id] = nut;
        }

        public Nut<T>? Load(string id)
        {
            return _storage.TryGetValue(id, out var nut) ? nut : null;
        }

        public void Delete(string id)
        {
            _storage.Remove(id);
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            return _storage.Values.ToList();
        }

        // Optional features - not supported by MemoryTrunk
        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("MemoryTrunk does not support history.");
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return LoadAll();
        }

        public void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            foreach (var nut in incoming)
            {
                Save(nut.Id, nut);
            }
        }
    }
}