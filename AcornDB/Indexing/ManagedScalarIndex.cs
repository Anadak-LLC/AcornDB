using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace AcornDB.Indexing
{
    /// <summary>
    /// In-memory managed implementation of a scalar index.
    /// Uses a sorted dictionary for O(log n) lookups and range queries.
    /// Thread-safe for concurrent reads and writes.
    /// </summary>
    public class ManagedScalarIndex<T, TProperty> : IScalarIndex<T, TProperty> where T : class
    {
        private readonly object _lock = new object();
        private readonly SortedDictionary<TProperty, HashSet<string>> _index;
        private readonly IComparer<TProperty> _comparer;
        private readonly Func<T, TProperty> _propertyExtractor;
        private readonly IndexConfiguration _config;

        private IndexState _state = IndexState.Building;
        private IndexStatistics _statistics = new IndexStatistics();

        public string Name { get; }
        public IndexType IndexType => IndexType.Scalar;
        public bool IsUnique { get; }
        public IndexState State => _state;
        public Expression<Func<T, TProperty>> PropertySelector { get; }

        public ManagedScalarIndex(
            Expression<Func<T, TProperty>> propertySelector,
            IndexConfiguration? config = null)
        {
            PropertySelector = propertySelector ?? throw new ArgumentNullException(nameof(propertySelector));
            _propertyExtractor = propertySelector.Compile();
            _config = config ?? new IndexConfiguration();

            Name = _config.Name ?? $"IX_{typeof(T).Name}_{GetPropertyName(propertySelector)}";
            IsUnique = _config.IsUnique;

            // Use custom comparer if provided, otherwise default
            if (_config.CaseInsensitive && typeof(TProperty) == typeof(string))
            {
                _comparer = (IComparer<TProperty>)StringComparer.OrdinalIgnoreCase;
            }
            else
            {
                _comparer = Comparer<TProperty>.Default;
            }

            _index = new SortedDictionary<TProperty, HashSet<string>>(_comparer);
        }

        public void Build(IEnumerable<object> documents)
        {
            lock (_lock)
            {
                _state = IndexState.Building;
                _index.Clear();

                foreach (var obj in documents)
                {
                    if (obj is Nut<T> nut)
                    {
                        AddInternal(nut.Id, nut.Payload);
                    }
                }

                _state = IndexState.Ready;
                UpdateStatistics();
            }
        }

        public void Add(string id, object document)
        {
            var doc = document as T;
            if (doc == null) return;

            lock (_lock)
            {
                AddInternal(id, doc);
                UpdateStatistics();
            }
        }

        private void AddInternal(string id, T document)
        {
            var value = _propertyExtractor(document);

            if (!_index.TryGetValue(value, out var ids))
            {
                ids = new HashSet<string>();
                _index[value] = ids;
            }
            else if (IsUnique && ids.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Unique index violation: value '{value}' already exists in index '{Name}'");
            }

            ids.Add(id);
        }

        public void Remove(string id)
        {
            lock (_lock)
            {
                // Find and remove the ID from all value buckets
                // This is O(n) for removal without knowing the value, but acceptable for managed indexes
                var emptyKeys = new List<TProperty>();

                foreach (var kvp in _index)
                {
                    if (kvp.Value.Remove(id))
                    {
                        if (kvp.Value.Count == 0)
                        {
                            emptyKeys.Add(kvp.Key);
                        }
                        break; // Found and removed, stop searching
                    }
                }

                // Clean up empty buckets
                foreach (var key in emptyKeys)
                {
                    _index.Remove(key);
                }

                UpdateStatistics();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _index.Clear();
                UpdateStatistics();
            }
        }

        public IEnumerable<string> Lookup(TProperty value)
        {
            lock (_lock)
            {
                if (_index.TryGetValue(value, out var ids))
                {
                    return ids.ToList(); // Return copy to avoid lock issues
                }
                return Enumerable.Empty<string>();
            }
        }

        public IEnumerable<string> Range(TProperty min, TProperty max)
        {
            lock (_lock)
            {
                var results = new List<string>();

                foreach (var kvp in _index)
                {
                    if (_comparer.Compare(kvp.Key, min) >= 0 && _comparer.Compare(kvp.Key, max) <= 0)
                    {
                        results.AddRange(kvp.Value);
                    }
                    else if (_comparer.Compare(kvp.Key, max) > 0)
                    {
                        break; // Sorted, so we can stop early
                    }
                }

                return results;
            }
        }

        public IEnumerable<string> GetAllSorted(bool ascending = true)
        {
            lock (_lock)
            {
                var results = new List<string>();

                if (ascending)
                {
                    foreach (var kvp in _index)
                    {
                        results.AddRange(kvp.Value);
                    }
                }
                else
                {
                    foreach (var kvp in _index.Reverse())
                    {
                        results.AddRange(kvp.Value);
                    }
                }

                return results;
            }
        }

        public TProperty? GetMin()
        {
            lock (_lock)
            {
                return _index.Any() ? _index.First().Key : default;
            }
        }

        public TProperty? GetMax()
        {
            lock (_lock)
            {
                return _index.Any() ? _index.Last().Key : default;
            }
        }

        public IndexStatistics GetStatistics()
        {
            lock (_lock)
            {
                return new IndexStatistics
                {
                    EntryCount = _statistics.EntryCount,
                    UniqueValueCount = _statistics.UniqueValueCount,
                    MemoryUsageBytes = _statistics.MemoryUsageBytes,
                    LastUpdated = _statistics.LastUpdated
                };
            }
        }

        private void UpdateStatistics()
        {
            _statistics.UniqueValueCount = _index.Count;
            _statistics.EntryCount = _index.Sum(kvp => kvp.Value.Count);
            _statistics.LastUpdated = DateTime.UtcNow;

            // Rough memory estimate: (key size + HashSet overhead) per unique value
            // This is a simplification - actual memory usage depends on key type
            _statistics.MemoryUsageBytes = _statistics.UniqueValueCount * 64 + _statistics.EntryCount * 16;
        }

        private string GetPropertyName(Expression<Func<T, TProperty>> expression)
        {
            if (expression.Body is MemberExpression memberExpr)
            {
                return memberExpr.Member.Name;
            }
            return "Unknown";
        }
    }
}
