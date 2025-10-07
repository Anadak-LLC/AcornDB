using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Reactive.Linq;

namespace AcornDB.Reactive
{
    /// <summary>
    /// Reactive extensions for AcornDB Trees
    /// Provides IObservable<T> streams for real-time change notifications
    /// </summary>
    public static class ReactiveTreeExtensions
    {
        /// <summary>
        /// Observe all changes to this tree (stash, toss, update)
        /// </summary>
        public static IObservable<TreeChange<T>> ObserveChanges<T>(this Tree<T> tree)
        {
            var changeStream = tree.GetChangeStream();
            return changeStream.AsObservable();
        }

        /// <summary>
        /// Observe only stash (create/update) operations
        /// </summary>
        public static IObservable<TreeChange<T>> ObserveStash<T>(this Tree<T> tree)
        {
            return tree.ObserveChanges()
                .Where(change => change.ChangeType == ChangeType.Stash);
        }

        /// <summary>
        /// Observe only toss (delete) operations
        /// </summary>
        public static IObservable<TreeChange<T>> ObserveToss<T>(this Tree<T> tree)
        {
            return tree.ObserveChanges()
                .Where(change => change.ChangeType == ChangeType.Toss);
        }

        /// <summary>
        /// Observe changes filtered by predicate
        /// </summary>
        public static IObservable<TreeChange<T>> ObserveWhere<T>(this Tree<T> tree, Func<T, bool> predicate)
        {
            return tree.ObserveChanges()
                .Where(change => change.Item != null && predicate(change.Item));
        }

        /// <summary>
        /// Observe the stream of payloads only (without metadata)
        /// </summary>
        public static IObservable<T> ObserveItems<T>(this Tree<T> tree)
        {
            return tree.ObserveStash()
                .Where(change => change.Item != null)
                .Select(change => change.Item!);
        }

        /// <summary>
        /// Observe changes from a specific node in the mesh
        /// </summary>
        public static IObservable<TreeChange<T>> ObserveFromNode<T>(this Tree<T> tree, string nodeId)
        {
            return tree.ObserveChanges()
                .Where(change => change.Nut?.OriginNodeId == nodeId);
        }

        /// <summary>
        /// Buffer changes over a time window
        /// </summary>
        public static IObservable<IList<TreeChange<T>>> ObserveBuffered<T>(
            this Tree<T> tree,
            TimeSpan window)
        {
            return tree.ObserveChanges().Buffer(window);
        }

        /// <summary>
        /// Throttle changes to avoid overwhelming subscribers
        /// </summary>
        public static IObservable<TreeChange<T>> ObserveThrottled<T>(
            this Tree<T> tree,
            TimeSpan throttle)
        {
            return tree.ObserveChanges().Throttle(throttle);
        }

        /// <summary>
        /// Observe latest value only (sample at intervals)
        /// </summary>
        public static IObservable<TreeChange<T>> ObserveSampled<T>(
            this Tree<T> tree,
            TimeSpan interval)
        {
            return tree.ObserveChanges().Sample(interval);
        }
    }

    /// <summary>
    /// Represents a change in a tree
    /// </summary>
    public class TreeChange<T>
    {
        public ChangeType ChangeType { get; set; }
        public string Id { get; set; } = "";
        public T? Item { get; set; }
        public Nut<T>? Nut { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? NodeId { get; set; }
    }

    /// <summary>
    /// Type of change operation
    /// </summary>
    public enum ChangeType
    {
        Stash,      // Create or update
        Toss,       // Delete
        Squabble    // Conflict resolution
    }

    /// <summary>
    /// Internal extension to get or create a change stream for a tree
    /// </summary>
    internal static class TreeChangeStreamHelper
    {
        private static readonly Dictionary<object, object> _changeStreams = new();
        private static readonly object _lock = new();

        internal static Subject<TreeChange<T>> GetChangeStream<T>(this Tree<T> tree)
        {
            lock (_lock)
            {
                if (!_changeStreams.ContainsKey(tree))
                {
                    var subject = new Subject<TreeChange<T>>();
                    _changeStreams[tree] = subject;

                    // Hook into tree events to populate the stream
                    HookTreeEvents(tree, subject);
                }

                return (_changeStreams[tree] as Subject<TreeChange<T>>)!;
            }
        }

        private static void HookTreeEvents<T>(Tree<T> tree, Subject<TreeChange<T>> subject)
        {
            // Hook into tree's internal events
            tree.OnStashEvent += (id, item, nut) =>
            {
                subject.OnNext(new TreeChange<T>
                {
                    ChangeType = ChangeType.Stash,
                    Id = id,
                    Item = item,
                    Nut = nut,
                    Timestamp = DateTime.UtcNow,
                    NodeId = nut.OriginNodeId
                });
            };

            tree.OnTossEvent += (id) =>
            {
                subject.OnNext(new TreeChange<T>
                {
                    ChangeType = ChangeType.Toss,
                    Id = id,
                    Item = default,
                    Nut = null,
                    Timestamp = DateTime.UtcNow
                });
            };

            tree.OnSquabbleEvent += (id, nut) =>
            {
                subject.OnNext(new TreeChange<T>
                {
                    ChangeType = ChangeType.Squabble,
                    Id = id,
                    Item = nut.Payload,
                    Nut = nut,
                    Timestamp = DateTime.UtcNow,
                    NodeId = nut.OriginNodeId
                });
            };
        }
    }
}
