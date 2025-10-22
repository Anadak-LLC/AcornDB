using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using AcornDB.Policy;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// High-performance trunk with append-only logging, versioning, and time-travel.
    /// Uses write batching, concurrent dictionaries, and memory pooling for optimal performance.
    /// Supports extensible IRoot processors for compression, encryption, policy enforcement, etc.
    ///
    /// Storage Pipeline:
    /// Write: Nut<T> → Store in memory → Serialize log entry → Root Chain (ascending) → byte[] → Write to log
    /// Read: In-memory retrieval (roots not involved, only for log replay on startup)
    /// </summary>
    public class DocumentStoreTrunk<T> : ITrunk<T>, IDisposable
    {
        private readonly string _folderPath;
        private readonly string _logPath;
        private readonly ConcurrentDictionary<string, Nut<T>> _current = new();
        private readonly ConcurrentDictionary<string, List<Nut<T>>> _history = new();
        private readonly List<byte[]> _logBuffer = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Timer _flushTimer;
        private FileStream? _logStream;
        private bool _disposed = false;
        private readonly List<IRoot> _roots = new();
        private readonly object _rootsLock = new();
        private readonly ISerializer _serializer;
        private bool _logLoaded = false;

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

        public DocumentStoreTrunk(string? customPath = null, ISerializer? serializer = null)
        {
            var typeName = typeof(T).Name;
            _folderPath = customPath ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "docstore", typeName);
            _logPath = Path.Combine(_folderPath, "changes.log");
            Directory.CreateDirectory(_folderPath);
            _serializer = serializer ?? new NewtonsoftJsonSerializer();

            // Note: Do NOT load log in constructor if roots might be needed
            // LoadFromLog will be called automatically on first access or explicitly after adding roots

            // Open log file for appending with buffering
            _logStream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read,
                8192, FileOptions.Asynchronous);

            // Auto-flush timer for write batching
            _flushTimer = new Timer(_ =>
            {
                try { FlushAsync().Wait(); }
                catch { /* Swallow timer exceptions */ }
            }, null, FLUSH_INTERVAL_MS, FLUSH_INTERVAL_MS);
        }

        /// <summary>
        /// Get all registered root processors
        /// </summary>
        public IReadOnlyList<IRoot> Roots
        {
            get
            {
                lock (_rootsLock)
                {
                    return _roots.ToList();
                }
            }
        }

        /// <summary>
        /// Add a root processor to the processing chain
        /// </summary>
        public void AddRoot(IRoot root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            lock (_rootsLock)
            {
                _roots.Add(root);
                // Sort by sequence to ensure correct execution order
                _roots.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

                // If log hasn't been loaded yet and we just added the first root, load it now
                if (!_logLoaded)
                {
                    LoadFromLog();
                    _logLoaded = true;
                }
            }
        }

        /// <summary>
        /// Remove a root processor from the processing chain
        /// </summary>
        public bool RemoveRoot(string name)
        {
            lock (_rootsLock)
            {
                var root = _roots.FirstOrDefault(r => r.Name == name);
                if (root != null)
                {
                    _roots.Remove(root);
                    return true;
                }
                return false;
            }
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
            // Ensure log is loaded (only matters if no roots were added)
            if (!_logLoaded)
            {
                lock (_rootsLock)
                {
                    if (!_logLoaded)
                    {
                        LoadFromLog();
                        _logLoaded = true;
                    }
                }
            }

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
            // Ensure log is loaded (only matters if no roots were added)
            if (!_logLoaded)
            {
                lock (_rootsLock)
                {
                    if (!_logLoaded)
                    {
                        LoadFromLog();
                        _logLoaded = true;
                    }
                }
            }

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
            // Serialize log entry to JSON (compact format for newline-delimited log)
            // Note: We do NOT apply roots to the log entries themselves - the log is an internal format
            // Roots are only applied by individual trunk implementations (BTreeTrunk, FileTrunk) for their storage
            var json = JsonConvert.SerializeObject(entry, Formatting.None);
            var jsonByteCount = Encoding.UTF8.GetByteCount(json);

            // Rent array from pool for better performance
            var buffer = ArrayPool<byte>.Shared.Rent(jsonByteCount + 1);
            byte[] bytes;
            try
            {
                Encoding.UTF8.GetBytes(json, 0, json.Length, buffer, 0);
                buffer[jsonByteCount] = (byte)'\n';

                // Copy to exact-sized array (will be stored in buffer)
                bytes = new byte[jsonByteCount + 1];
                Array.Copy(buffer, bytes, jsonByteCount + 1);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            // Add to buffer
            lock (_logBuffer)
            {
                _logBuffer.Add(bytes);

                // Flush if buffer is full
                if (_logBuffer.Count >= BUFFER_THRESHOLD)
                {
                    FlushAsync().Wait();
                }
            }
        }

        private byte[] AppendNewline(byte[] data)
        {
            // Optimize: Use ArrayPool to avoid allocations and use Span for better performance
            var result = ArrayPool<byte>.Shared.Rent(data.Length + 1);
            try
            {
                data.AsSpan().CopyTo(result);
                result[data.Length] = (byte)'\n';

                // Return only the exact size needed
                var final = new byte[data.Length + 1];
                Array.Copy(result, final, data.Length + 1);
                return final;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(result);
            }
        }

        private async Task FlushAsync()
        {
            List<byte[]> toWrite;

            lock (_logBuffer)
            {
                if (_logBuffer.Count == 0) return;
                toWrite = new List<byte[]>(_logBuffer);
                _logBuffer.Clear();
            }

            await _writeLock.WaitAsync();
            try
            {
                // Write all buffered entries
                foreach (var bytes in toWrite)
                {
                    await _logStream!.WriteAsync(bytes, 0, bytes.Length);
                }

                // Flush to disk
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

            // Read entire log file and split into lines
            // Note: Log entries are NOT processed through roots - they are stored in plain JSON format
            var allBytes = File.ReadAllBytes(_logPath);
            var allText = Encoding.UTF8.GetString(allBytes);
            var lines = allText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
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
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to deserialize log entry: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _flushTimer?.Dispose();

            // Flush any pending writes
            try { FlushAsync().Wait(); } catch { }

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
