using System;
using AcornDB.Logging;
using System.Collections.Generic;
using System.Linq;

namespace AcornDB.Transaction
{
    /// <summary>
    /// Provides atomic multi-nut transactions for Trees
    /// All operations succeed or all fail together
    /// </summary>
    public class TreeTransaction<T> where T : class
    {
        private readonly Tree<T> _tree;
        private readonly List<TransactionOperation<T>> _operations = new();
        private readonly Dictionary<string, Nut<T>> _snapshot = new();
        private bool _committed = false;
        private bool _rolledBack = false;

        internal TreeTransaction(Tree<T> tree)
        {
            _tree = tree;
        }

        /// <summary>
        /// Add a stash operation to the transaction
        /// </summary>
        public TreeTransaction<T> Stash(string id, T item)
        {
            EnsureNotFinalized();

            // Take snapshot of existing value if it exists
            var existing = _tree.Crack(id);
            if (existing != null)
            {
                _snapshot[id] = new Nut<T>
                {
                    Id = id,
                    Payload = existing,
                    Timestamp = DateTime.UtcNow
                };
            }

            _operations.Add(new TransactionOperation<T>
            {
                Type = OperationType.Stash,
                Id = id,
                Item = item
            });

            return this;
        }

        /// <summary>
        /// Add a toss (delete) operation to the transaction
        /// </summary>
        public TreeTransaction<T> Toss(string id)
        {
            EnsureNotFinalized();

            // Take snapshot of existing value
            var existing = _tree.Crack(id);
            if (existing != null)
            {
                _snapshot[id] = new Nut<T>
                {
                    Id = id,
                    Payload = existing,
                    Timestamp = DateTime.UtcNow
                };
            }

            _operations.Add(new TransactionOperation<T>
            {
                Type = OperationType.Toss,
                Id = id
            });

            return this;
        }

        /// <summary>
        /// Commit the transaction - all operations execute atomically
        /// Returns true if successful, false if rolled back
        /// </summary>
        public bool Commit()
        {
            EnsureNotFinalized();

            try
            {
                // Execute all operations
                foreach (var op in _operations)
                {
                    switch (op.Type)
                    {
                        case OperationType.Stash:
                            _tree.Stash(op.Id, op.Item!);
                            break;
                        case OperationType.Toss:
                            _tree.Toss(op.Id);
                            break;
                    }
                }

                _committed = true;
                return true;
            }
            catch (Exception ex)
            {
                // Rollback on any error
                Rollback();
                AcornLog.Info($"⚠️ Transaction failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Rollback the transaction - restore all previous values
        /// </summary>
        public void Rollback()
        {
            if (_rolledBack) return;
            EnsureNotCommitted();

            // Restore snapshots in reverse order
            foreach (var kvp in _snapshot.Reverse())
            {
                _tree.Stash(kvp.Key, kvp.Value.Payload);
            }

            _rolledBack = true;
        }

        /// <summary>
        /// Execute the transaction and return result
        /// Syntactic sugar for Commit()
        /// </summary>
        public TransactionResult Execute()
        {
            var success = Commit();
            return new TransactionResult
            {
                Success = success,
                OperationCount = _operations.Count,
                RolledBack = _rolledBack
            };
        }

        private void EnsureNotFinalized()
        {
            if (_committed)
                throw new InvalidOperationException("Transaction already committed");
            if (_rolledBack)
                throw new InvalidOperationException("Transaction already rolled back");
        }

        private void EnsureNotCommitted()
        {
            if (_committed)
                throw new InvalidOperationException("Cannot rollback committed transaction");
        }
    }

    /// <summary>
    /// Transaction operation types
    /// </summary>
    internal enum OperationType
    {
        Stash,
        Toss
    }

    /// <summary>
    /// Internal transaction operation record
    /// </summary>
    internal class TransactionOperation<T>
    {
        public OperationType Type { get; set; }
        public string Id { get; set; } = "";
        public T? Item { get; set; }
    }

    /// <summary>
    /// Extension methods to enable transactions on Trees
    /// </summary>
    public static class TreeTransactionExtensions
    {
        /// <summary>
        /// Begin a new transaction on this tree
        /// </summary>
        public static TreeTransaction<T> BeginTransaction<T>(this Tree<T> tree) where T : class
        {
            return new TreeTransaction<T>(tree);
        }
    }
}
