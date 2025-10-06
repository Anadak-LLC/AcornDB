using System.Text;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// Full-featured trunk with append-only logging, versioning, and time-travel.
    /// </summary>
    public class DocumentStoreTrunk<T> : ITrunk<T>
    {
        private readonly string _folderPath;
        private readonly string _logPath;
        private readonly Dictionary<string, Nut<T>> _current = new();
        private readonly Dictionary<string, List<Nut<T>>> _history = new();

        public DocumentStoreTrunk(string? customPath = null)
        {
            var typeName = typeof(T).Name;
            _folderPath = customPath ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "docstore", typeName);
            _logPath = Path.Combine(_folderPath, "changes.log");
            Directory.CreateDirectory(_folderPath);
            LoadFromLog();
        }

        public void Save(string id, Nut<T> shell)
        {
            // Store previous version in history
            if (_current.TryGetValue(id, out var previous))
            {
                if (!_history.ContainsKey(id))
                    _history[id] = new List<Nut<T>>();
                _history[id].Add(previous);
            }

            // Update current state
            _current[id] = shell;

            // Append to log
            var logEntry = new ChangeLogEntry<T>
            {
                Action = "Save",
                Id = id,
                Shell = shell,
                Timestamp = DateTime.UtcNow
            };
            AppendToLog(logEntry);
        }

        public Nut<T>? Load(string id)
        {
            return _current.TryGetValue(id, out var shell) ? shell : null;
        }

        public void Delete(string id)
        {
            if (_current.TryGetValue(id, out var shell))
            {
                // Store in history before deleting
                if (!_history.ContainsKey(id))
                    _history[id] = new List<Nut<T>>();
                _history[id].Add(shell);

                _current.Remove(id);

                // Log deletion
                var logEntry = new ChangeLogEntry<T>
                {
                    Action = "Delete",
                    Id = id,
                    Shell = null,
                    Timestamp = DateTime.UtcNow
                };
                AppendToLog(logEntry);
            }
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            return _current.Values.ToList();
        }

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            return _history.TryGetValue(id, out var versions)
                ? versions.AsReadOnly()
                : new List<Nut<T>>().AsReadOnly();
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return _current.Values.ToList();
        }

        public void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            foreach (var shell in incoming)
            {
                Save(shell.Id, shell);
            }
        }

        private void AppendToLog(ChangeLogEntry<T> entry)
        {
            var json = JsonConvert.SerializeObject(entry);
            File.AppendAllText(_logPath, json + Environment.NewLine, Encoding.UTF8);
        }

        private void LoadFromLog()
        {
            if (!File.Exists(_logPath))
                return;

            foreach (var line in File.ReadAllLines(_logPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var entry = JsonConvert.DeserializeObject<ChangeLogEntry<T>>(line);
                if (entry == null)
                    continue;

                if (entry.Action == "Save" && entry.Shell != null)
                {
                    // Store previous in history
                    if (_current.TryGetValue(entry.Id, out var previous))
                    {
                        if (!_history.ContainsKey(entry.Id))
                            _history[entry.Id] = new List<Nut<T>>();
                        _history[entry.Id].Add(previous);
                    }
                    _current[entry.Id] = entry.Shell;
                }
                else if (entry.Action == "Delete")
                {
                    if (_current.TryGetValue(entry.Id, out var shell))
                    {
                        if (!_history.ContainsKey(entry.Id))
                            _history[entry.Id] = new List<Nut<T>>();
                        _history[entry.Id].Add(shell);
                    }
                    _current.Remove(entry.Id);
                }
            }
        }
    }

    public class ChangeLogEntry<T>
    {
        public string Action { get; set; } = "";
        public string Id { get; set; } = "";
        public Nut<T>? Shell { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
