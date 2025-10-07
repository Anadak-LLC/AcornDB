using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace AcornDB.Query
{
    /// <summary>
    /// LINQ-style query interface for AcornDB Trees
    /// Provides fluent API for filtering, ordering, and projecting tree data
    /// </summary>
    public class TreeQuery<T>
    {
        private readonly Tree<T> _tree;
        private IEnumerable<Nut<T>> _source;
        private Func<Nut<T>, bool>? _whereClause;
        private Func<Nut<T>, object>? _orderByClause;
        private bool _orderDescending = false;
        private int? _takeCount;
        private int? _skipCount;

        internal TreeQuery(Tree<T> tree)
        {
            _tree = tree;
            _source = tree.GetAllNuts();
        }

        /// <summary>
        /// Filter nuts by predicate on the payload
        /// </summary>
        public TreeQuery<T> Where(Func<T, bool> predicate)
        {
            _whereClause = nut => predicate(nut.Payload);
            return this;
        }

        /// <summary>
        /// Filter nuts by predicate on the entire nut (payload + metadata)
        /// </summary>
        public TreeQuery<T> WhereNut(Func<Nut<T>, bool> predicate)
        {
            _whereClause = predicate;
            return this;
        }

        /// <summary>
        /// Order results by a key selector
        /// </summary>
        public TreeQuery<T> OrderBy<TKey>(Func<T, TKey> keySelector)
        {
            _orderByClause = nut => keySelector(nut.Payload)!;
            _orderDescending = false;
            return this;
        }

        /// <summary>
        /// Order results by a key selector (descending)
        /// </summary>
        public TreeQuery<T> OrderByDescending<TKey>(Func<T, TKey> keySelector)
        {
            _orderByClause = nut => keySelector(nut.Payload)!;
            _orderDescending = true;
            return this;
        }

        /// <summary>
        /// Order by timestamp (newest first)
        /// </summary>
        public TreeQuery<T> Newest()
        {
            _orderByClause = nut => nut.Timestamp;
            _orderDescending = true;
            return this;
        }

        /// <summary>
        /// Order by timestamp (oldest first)
        /// </summary>
        public TreeQuery<T> Oldest()
        {
            _orderByClause = nut => nut.Timestamp;
            _orderDescending = false;
            return this;
        }

        /// <summary>
        /// Take only the first N results
        /// </summary>
        public TreeQuery<T> Take(int count)
        {
            _takeCount = count;
            return this;
        }

        /// <summary>
        /// Skip the first N results
        /// </summary>
        public TreeQuery<T> Skip(int count)
        {
            _skipCount = count;
            return this;
        }

        /// <summary>
        /// Filter by timestamp range
        /// </summary>
        public TreeQuery<T> Between(DateTime start, DateTime end)
        {
            _whereClause = nut => nut.Timestamp >= start && nut.Timestamp <= end;
            return this;
        }

        /// <summary>
        /// Filter by items created after a specific date
        /// </summary>
        public TreeQuery<T> After(DateTime date)
        {
            _whereClause = nut => nut.Timestamp > date;
            return this;
        }

        /// <summary>
        /// Filter by items created before a specific date
        /// </summary>
        public TreeQuery<T> Before(DateTime date)
        {
            _whereClause = nut => nut.Timestamp < date;
            return this;
        }

        /// <summary>
        /// Filter by origin node
        /// </summary>
        public TreeQuery<T> FromNode(string nodeId)
        {
            _whereClause = nut => nut.OriginNodeId == nodeId;
            return this;
        }

        /// <summary>
        /// Execute query and return results
        /// </summary>
        public List<T> ToList()
        {
            return ExecuteQuery().Select(nut => nut.Payload).ToList();
        }

        /// <summary>
        /// Execute query and return nuts (with metadata)
        /// </summary>
        public List<Nut<T>> ToNutList()
        {
            return ExecuteQuery().ToList();
        }

        /// <summary>
        /// Execute query and return first result or null
        /// </summary>
        public T? FirstOrDefault()
        {
            return ExecuteQuery().Select(nut => nut.Payload).FirstOrDefault();
        }

        /// <summary>
        /// Execute query and return first nut or null
        /// </summary>
        public Nut<T>? FirstNutOrDefault()
        {
            return ExecuteQuery().FirstOrDefault();
        }

        /// <summary>
        /// Execute query and return single result (throws if multiple)
        /// </summary>
        public T? SingleOrDefault()
        {
            return ExecuteQuery().Select(nut => nut.Payload).SingleOrDefault();
        }

        /// <summary>
        /// Count results without materializing them
        /// </summary>
        public int Count()
        {
            return ExecuteQuery().Count();
        }

        /// <summary>
        /// Check if any results match the query
        /// </summary>
        public bool Any()
        {
            return ExecuteQuery().Any();
        }

        /// <summary>
        /// Execute query and return as enumerable (deferred execution)
        /// </summary>
        public IEnumerable<T> AsEnumerable()
        {
            return ExecuteQuery().Select(nut => nut.Payload);
        }

        private IEnumerable<Nut<T>> ExecuteQuery()
        {
            IEnumerable<Nut<T>> query = _source;

            // Apply where clause
            if (_whereClause != null)
            {
                query = query.Where(_whereClause);
            }

            // Apply ordering
            if (_orderByClause != null)
            {
                query = _orderDescending
                    ? query.OrderByDescending(_orderByClause)
                    : query.OrderBy(_orderByClause);
            }

            // Apply skip
            if (_skipCount.HasValue)
            {
                query = query.Skip(_skipCount.Value);
            }

            // Apply take
            if (_takeCount.HasValue)
            {
                query = query.Take(_takeCount.Value);
            }

            return query;
        }
    }

    /// <summary>
    /// Extension methods to enable query syntax on Trees
    /// </summary>
    public static class TreeQueryExtensions
    {
        /// <summary>
        /// Start a LINQ-style query on this tree
        /// </summary>
        public static TreeQuery<T> Query<T>(this Tree<T> tree)
        {
            return new TreeQuery<T>(tree);
        }

        /// <summary>
        /// Get all nuts from the tree (internal helper)
        /// </summary>
        internal static IEnumerable<Nut<T>> GetAllNuts<T>(this Tree<T> tree)
        {
            // Use the public GetAllNuts() method we just added to Tree
            return tree.GetAllNuts();
        }
    }
}
