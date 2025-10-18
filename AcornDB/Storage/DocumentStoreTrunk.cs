using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// High-performance trunk with append-only logging, versioning, and time-travel.
    /// Uses write batching, concurrent dictionaries, and memory pooling for optimal performance.
    /// </summary>
    public class DocumentStoreTrunk<T> : ITrunk<T>, IDisposable
    {
        private readonly string _folderPath;
        private readonly string _logPath;
        private readonly ConcurrentDictionary<string, Nut<T>> _current = new();
        private readonly ConcurrentDictionary<string, List<Nut<T>>> _history = new();
        private readonly List<string> _logBuffer = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Timer _flushTimer;
        private FileStream? _logStream;
        private StreamWriter? _logWriter;
        private bool _disposed = false;

        private const int BUFFER_THRESHOLD = 100; // Flush after 100 log entries
        private const int FLUSH_INTERVAL_MS = 200; // Flush every 200ms

        public ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = true,
            SupportsSync = true,
            IsDurable = true,
            SupportsAsync = false,
            TrunkType = "DocumentStoreTrunk"
        };

        public DocumentStoreTrunk(string? customPath = null)
        {
            var typeName = typeof(T).Name;
            _folderPath = customPath ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "docstore", typeName);
            _logPath = Path.Combine(_folderPath, "changes.log");
            Directory.CreateDirectory(_folderPath);

            // Load existing log
            LoadFromLog();

            // Open log file for appending with buffering
            _logStream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read,
                8192, FileOptions.Asynchronous);
            _logWriter = new StreamWriter(_logStream, Encoding.UTF8, 8192, leaveOpen: false);

            // Auto-flush timer for write batching
            _flushTimer = new Timer(_ =>
            {
                try { FlushAsync().Wait(); }
                catch { /* Swallow timer exceptions */ }
            }, null, FLUSH_INTERVAL_MS, FLUSH_INTERVAL_MS);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Save(string id, Nut<T> shell)
        {
            // Store previous version in history
            if (_current.TryGetValue(id, out var previous))
            {
                // GetOrAdd for lock-free operation
                var historyList = _history.GetOrAdd(id, _ => new List<Nut<T>>());
                lock (historyList) // Lock only the specific list, not the entire dictionary
                {
                    historyList.Add(previous);
                }
            }

            // Update current state (lock-free with ConcurrentDictionary)
            _current[id] = shell;

            // Create log entry
            var logEntry = new ChangeLogEntry<T>
            {
                Action = "Save",
                Id = id,
                Shell = shell,
                Timestamp = DateTime.UtcNow
            };

            // Add to buffer (batched writes)
            QueueLogEntry(logEntry);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Nut<T>? Load(string id)
        {
            // Lock-free read from ConcurrentDictionary
            return _current.TryGetValue(id, out var shell) ? shell : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(string id)
        {
            if (_current.TryRemove(id, out var shell))
            {
                // Store in history before deleting
                var historyList = _history.GetOrAdd(id, _ => new List<Nut<T>>());
                lock (historyList)
                {
                    historyList.Add(shell);
                }

                // Log deletion
                var logEntry = new ChangeLogEntry<T>
                {
                    Action = "Delete",
                    Id = id,
                    Shell = null,
                    Timestamp = DateTime.UtcNow
                };
                QueueLogEntry(logEntry);
            }
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            // Return values directly - ConcurrentDictionary.Values is thread-safe
            return _current.Values;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void QueueLogEntry(ChangeLogEntry<T> entry)
        {
            var json = JsonConvert.SerializeObject(entry);

            lock (_logBuffer)
            {
                _logBuffer.Add(json);

                // Flush if buffer is full
                if (_logBuffer.Count >= BUFFER_THRESHOLD)
                {
                    FlushAsync().Wait();
                }
            }
        }

        private async Task FlushAsync()
        {
            List<string> toWrite;

            lock (_logBuffer)
            {
                if (_logBuffer.Count == 0) return;
                toWrite = new List<string>(_logBuffer);
                _logBuffer.Clear();
            }

            await _writeLock.WaitAsync();
            try
            {
                // Write all buffered entries
                foreach (var json in toWrite)
                {
                    await _logWriter!.WriteLineAsync(json);
                }

                // Flush to disk
                await _logWriter!.FlushAsync();
                await _logStream!.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private void LoadFromLog()
        {
            if (!File.Exists(_logPath))
                return;

            // Use streaming to reduce memory allocations
            using (var reader = new StreamReader(_logPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 8192))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
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
                            var historyList = _history.GetOrAdd(entry.Id, _ => new List<Nut<T>>());
                            lock (historyList)
                            {
                                historyList.Add(previous);
                            }
                        }
                        _current[entry.Id] = entry.Shell;
                    }
                    else if (entry.Action == "Delete")
                    {
                        if (_current.TryRemove(entry.Id, out var shell))
                        {
                            var historyList = _history.GetOrAdd(entry.Id, _ => new List<Nut<T>>());
                            lock (historyList)
                            {
                                historyList.Add(shell);
                            }
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _flushTimer?.Dispose();

            // Flush any pending writes
            try { FlushAsync().Wait(); } catch { }

            _logWriter?.Dispose();
            _logStream?.Dispose();
            _writeLock?.Dispose();

            _disposed = true;
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
