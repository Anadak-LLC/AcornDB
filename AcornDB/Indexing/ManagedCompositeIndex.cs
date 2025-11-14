using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace AcornDB.Indexing
{
    /// <summary>
    /// In-memory composite index that indexes multiple properties together.
    /// Uses a sorted dictionary for efficient prefix and range queries.
    ///
    /// Example: Index on (Department, Age) enables queries like:
    ///   - WHERE Department = "Engineering"
    ///   - WHERE Department = "Engineering" AND Age = 30
    ///   - WHERE Department = "Engineering" AND Age BETWEEN 25 AND 35
    /// </summary>
    public class ManagedCompositeIndex<T> : ICompositeIndex<T> where T : class
    {
        private readonly SortedDictionary<CompositeKey, HashSet<string>> _index;
        private readonly List<Func<T, object>> _propertyExtractors;
        private readonly List<string> _propertyNames;
        private IndexState _state;

        public string Name { get; }
        public IndexType IndexType => IndexType.Composite;
        public bool IsUnique { get; }
        public IndexState State => _state;
        public LambdaExpression KeySelector { get; }
        public IReadOnlyList<string> PropertyNames => _propertyNames.AsReadOnly();

        /// <summary>
        /// Create a composite index from individual property selectors
        /// </summary>
        public ManagedCompositeIndex(
            string name,
            IEnumerable<LambdaExpression> propertySelectors,
            IndexConfiguration? config = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            IsUnique = config?.IsUnique ?? false;
            _state = IndexState.Building;

            _propertyExtractors = new List<Func<T, object>>();
            _propertyNames = new List<string>();

            foreach (var selector in propertySelectors)
            {
                // Extract property name
                var propertyName = ExtractPropertyName(selector);
                if (string.IsNullOrEmpty(propertyName))
                {
                    throw new ArgumentException("Could not extract property name from selector", nameof(propertySelectors));
                }
                _propertyNames.Add(propertyName);

                // Compile extractor that returns object
                var parameter = selector.Parameters[0];
                var body = Expression.Convert(selector.Body, typeof(object));
                var objectSelector = Expression.Lambda<Func<T, object>>(body, parameter);
                _propertyExtractors.Add(objectSelector.Compile());
            }

            // Store a combined key selector for reference
            // This is a simplified representation - actual extraction uses _propertyExtractors
            KeySelector = propertySelectors.First();

            _index = new SortedDictionary<CompositeKey, HashSet<string>>();
        }

        private string? ExtractPropertyName(LambdaExpression expression)
        {
            if (expression.Body is MemberExpression member)
            {
                return member.Member.Name;
            }
            if (expression.Body is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
            {
                return unaryMember.Member.Name;
            }
            return null;
        }

        public void Build(IEnumerable<object> documents)
        {
            _state = IndexState.Building;
            _index.Clear();

            foreach (var doc in documents)
            {
                if (doc is Nut<T> nut)
                {
                    Add(nut.Id, nut.Payload);
                }
            }

            _state = IndexState.Ready;
        }

        public void Add(string id, object document)
        {
            if (document is not T typedDoc)
                return;

            var keyValues = _propertyExtractors.Select(extractor => extractor(typedDoc)).ToArray();
            var key = new CompositeKey(keyValues);

            if (!_index.TryGetValue(key, out var ids))
            {
                ids = new HashSet<string>();
                _index[key] = ids;
            }

            if (IsUnique && ids.Count > 0)
            {
                throw new InvalidOperationException($"Duplicate key in unique composite index '{Name}'");
            }

            ids.Add(id);
        }

        public void Remove(string id)
        {
            // Need to scan to find which key(s) contain this ID
            var keysToRemove = new List<CompositeKey>();

            foreach (var kvp in _index)
            {
                if (kvp.Value.Remove(id))
                {
                    if (kvp.Value.Count == 0)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
            }

            foreach (var key in keysToRemove)
            {
                _index.Remove(key);
            }
        }

        public void Clear()
        {
            _index.Clear();
            _state = IndexState.Building;
        }

        public IEnumerable<string> Lookup(params object[] keyValues)
        {
            if (keyValues.Length != _propertyExtractors.Count)
            {
                throw new ArgumentException(
                    $"Expected {_propertyExtractors.Count} key values, got {keyValues.Length}",
                    nameof(keyValues));
            }

            var key = new CompositeKey(keyValues);
            if (_index.TryGetValue(key, out var ids))
            {
                return ids.ToList();
            }

            return Enumerable.Empty<string>();
        }

        public IEnumerable<string> PrefixLookup(params object[] prefixValues)
        {
            if (prefixValues.Length == 0 || prefixValues.Length > _propertyExtractors.Count)
            {
                throw new ArgumentException("Invalid prefix length", nameof(prefixValues));
            }

            // If we have all values, just do a regular lookup
            if (prefixValues.Length == _propertyExtractors.Count)
            {
                return Lookup(prefixValues);
            }

            // Scan for matching prefixes
            var results = new HashSet<string>();

            foreach (var kvp in _index)
            {
                if (kvp.Key.MatchesPrefix(prefixValues))
                {
                    results.UnionWith(kvp.Value);
                }
            }

            return results;
        }

        public IEnumerable<string> RangeOnLastProperty(object[] exactPrefixValues, object min, object max)
        {
            if (exactPrefixValues.Length >= _propertyExtractors.Count)
            {
                throw new ArgumentException("Prefix values should leave room for range property", nameof(exactPrefixValues));
            }

            var results = new HashSet<string>();

            foreach (var kvp in _index)
            {
                // Check if prefix matches exactly
                if (!kvp.Key.MatchesPrefix(exactPrefixValues))
                    continue;

                // Get the value at the range position
                var rangeValue = kvp.Key.Values[exactPrefixValues.Length];

                // Check if it's in range
                if (IsInRange(rangeValue, min, max))
                {
                    results.UnionWith(kvp.Value);
                }
            }

            return results;
        }

        private bool IsInRange(object value, object min, object max)
        {
            if (value is IComparable comparable)
            {
                var compareMin = comparable.CompareTo(min);
                var compareMax = comparable.CompareTo(max);
                return compareMin >= 0 && compareMax <= 0;
            }

            return false;
        }

        public IEnumerable<string> GetAllSorted(bool ascending = true)
        {
            var keys = ascending ? _index.Keys : _index.Keys.Reverse();

            foreach (var key in keys)
            {
                foreach (var id in _index[key])
                {
                    yield return id;
                }
            }
        }

        public IndexStatistics GetStatistics()
        {
            var totalEntries = _index.Values.Sum(ids => ids.Count);
            var uniqueKeys = _index.Count;

            return new IndexStatistics
            {
                EntryCount = totalEntries,
                UniqueValueCount = uniqueKeys,
                MemoryUsageBytes = EstimateMemoryUsage(),
                LastUpdated = DateTime.UtcNow
            };
        }

        private long EstimateMemoryUsage()
        {
            // Rough estimation:
            // - Each CompositeKey: 8 bytes per value + overhead
            // - Each HashSet: ~100 bytes + entries
            // - Each string ID: ~40 bytes
            var keyCount = _index.Count;
            var entryCount = _index.Values.Sum(ids => ids.Count);

            return (keyCount * (_propertyExtractors.Count * 8 + 100)) + (entryCount * 40);
        }

        /// <summary>
        /// Represents a composite key with multiple values
        /// </summary>
        private class CompositeKey : IComparable<CompositeKey>
        {
            public object[] Values { get; }

            public CompositeKey(object[] values)
            {
                Values = values ?? throw new ArgumentNullException(nameof(values));
            }

            public bool MatchesPrefix(object[] prefixValues)
            {
                if (prefixValues.Length > Values.Length)
                    return false;

                for (int i = 0; i < prefixValues.Length; i++)
                {
                    if (!Equals(Values[i], prefixValues[i]))
                        return false;
                }

                return true;
            }

            public int CompareTo(CompositeKey? other)
            {
                if (other == null)
                    return 1;

                var minLength = Math.Min(Values.Length, other.Values.Length);

                for (int i = 0; i < minLength; i++)
                {
                    var val1 = Values[i];
                    var val2 = other.Values[i];

                    int comparison;
                    if (val1 is IComparable comparable)
                    {
                        comparison = comparable.CompareTo(val2);
                    }
                    else if (val1 != null && val2 != null)
                    {
                        comparison = string.Compare(val1.ToString(), val2.ToString(), StringComparison.Ordinal);
                    }
                    else if (val1 == null && val2 == null)
                    {
                        comparison = 0;
                    }
                    else
                    {
                        comparison = val1 == null ? -1 : 1;
                    }

                    if (comparison != 0)
                        return comparison;
                }

                return Values.Length.CompareTo(other.Values.Length);
            }

            public override bool Equals(object? obj)
            {
                if (obj is not CompositeKey other)
                    return false;

                if (Values.Length != other.Values.Length)
                    return false;

                for (int i = 0; i < Values.Length; i++)
                {
                    if (!Equals(Values[i], other.Values[i]))
                        return false;
                }

                return true;
            }

            public override int GetHashCode()
            {
                var hash = new HashCode();
                foreach (var value in Values)
                {
                    hash.Add(value);
                }
                return hash.ToHashCode();
            }
        }
    }
}
