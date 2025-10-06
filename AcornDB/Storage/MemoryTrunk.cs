namespace AcornDB.Storage
{
    /// <summary>
    /// In-memory trunk for testing. Non-durable, no history.
    /// </summary>
    public class MemoryTrunk<T> : ITrunk<T>
    {
        private readonly Dictionary<string, NutShell<T>> _storage = new();

        public void Save(string id, NutShell<T> shell)
        {
            _storage[id] = shell;
        }

        public NutShell<T>? Load(string id)
        {
            return _storage.TryGetValue(id, out var shell) ? shell : null;
        }

        public void Delete(string id)
        {
            _storage.Remove(id);
        }

        public IEnumerable<NutShell<T>> LoadAll()
        {
            return _storage.Values.ToList();
        }

        // Optional features - not supported by MemoryTrunk
        public IReadOnlyList<NutShell<T>> GetHistory(string id)
        {
            throw new NotSupportedException("MemoryTrunk does not support history.");
        }

        public IEnumerable<NutShell<T>> ExportChanges()
        {
            return LoadAll();
        }

        public void ImportChanges(IEnumerable<NutShell<T>> incoming)
        {
            foreach (var shell in incoming)
            {
                Save(shell.Id, shell);
            }
        }
    }
}