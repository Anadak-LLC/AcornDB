using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Storage;
using Xunit;

namespace AcornDB.Test
{
    /// <summary>
    /// Stress tests verifying that concurrent reads do not crash or return garbage
    /// when a writer triggers memory-mapped file remap (EnsureCapacity).
    /// Targets defect D-04 (accessor disposed under readers) and D-05 (race in offset reservation).
    /// </summary>
    public class BitcaskTrunkRemapSafetyTests : IDisposable
    {
        private readonly string _tempDir;

        public class TestDoc
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public int Value { get; set; }
        }

        public BitcaskTrunkRemapSafetyTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"acorndb_remap_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        /// <summary>
        /// Write enough data to force at least one remap (exceed 64MB initial capacity),
        /// while concurrent readers continuously read previously-written items.
        /// Before the fix, this would throw ObjectDisposedException.
        /// </summary>
        [Fact]
        public void ConcurrentReadsAndWrites_WithRemap_NoExceptions()
        {
            var dir = Path.Combine(_tempDir, "remap_stress");
            using var trunk = new BitcaskTrunk<TestDoc>(dir);
            var tree = new Tree<TestDoc>(trunk);
            tree.TtlEnforcementEnabled = false;
            tree.CacheEvictionEnabled = false;

            var writtenIds = new ConcurrentBag<string>();
            var errors = new ConcurrentBag<Exception>();
            var cts = new CancellationTokenSource();

            // Seed some initial data so readers have something to read
            for (int i = 0; i < 100; i++)
            {
                var id = $"seed-{i}";
                tree.Stash(new TestDoc { Id = id, Name = $"Seed doc {i}", Value = i });
                writtenIds.Add(id);
            }

            // Force flush
            Thread.Sleep(200);

            // Writer task: write large documents to force file growth / remap
            var writerTask = Task.Run(() =>
            {
                try
                {
                    // Large payload to force remap faster
                    var largePayload = new string('X', 8192);
                    for (int i = 0; i < 2000 && !cts.IsCancellationRequested; i++)
                    {
                        var id = $"heavy-{i}";
                        tree.Stash(new TestDoc { Id = id, Name = largePayload, Value = i });
                        writtenIds.Add(id);

                        if (i % 100 == 0) Thread.Sleep(10); // let readers interleave
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });

            // Reader tasks: continuously read random previously-written items
            var readerTasks = new Task[4];
            for (int r = 0; r < readerTasks.Length; r++)
            {
                readerTasks[r] = Task.Run(() =>
                {
                    var rng = new Random(Thread.CurrentThread.ManagedThreadId);
                    try
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            var ids = writtenIds.ToArray();
                            if (ids.Length == 0) continue;

                            var id = ids[rng.Next(ids.Length)];
                            var result = tree.Crack(id);
                            // result may be null if write hasn't flushed yet — that's fine
                            // but should never throw ObjectDisposedException
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                });
            }

            // Let it run
            writerTask.Wait();
            cts.Cancel();
            Task.WaitAll(readerTasks);

            Assert.Empty(errors);
        }

        /// <summary>
        /// Multiple concurrent writers should not corrupt the file position or produce
        /// overlapping writes, even when remap is triggered.
        /// </summary>
        [Fact]
        public void ConcurrentWrites_WithRemap_AllDataReadableAfter()
        {
            var dir = Path.Combine(_tempDir, "concurrent_writes");
            using var trunk = new BitcaskTrunk<TestDoc>(dir);
            var tree = new Tree<TestDoc>(trunk);
            tree.TtlEnforcementEnabled = false;
            tree.CacheEvictionEnabled = false;

            var errors = new ConcurrentBag<Exception>();
            int threadsCount = 4;
            int itemsPerThread = 500;
            var largePayload = new string('Y', 4096);

            var tasks = new Task[threadsCount];
            for (int t = 0; t < threadsCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < itemsPerThread; i++)
                        {
                            tree.Stash(new TestDoc
                            {
                                Id = $"t{threadId}-{i}",
                                Name = largePayload,
                                Value = threadId * 10000 + i
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                });
            }

            Task.WaitAll(tasks);
            Assert.Empty(errors);

            // Wait for batch flush to complete
            Thread.Sleep(500);

            // Verify all items are readable at the storage level (bypass Tree cache)
            for (int t = 0; t < threadsCount; t++)
            {
                for (int i = 0; i < itemsPerThread; i++)
                {
                    var id = $"t{t}-{i}";
                    var nut = trunk.Crack(id);
                    Assert.NotNull(nut);
                    Assert.Equal(id, nut!.Id);
                }
            }
        }

        /// <summary>
        /// Reads during heavy writes should never throw, even when file growth
        /// triggers accessor replacement.
        /// </summary>
        [Fact]
        public void MixedWorkload_ReadsDuringHeavyWrites_NoExceptions()
        {
            var dir = Path.Combine(_tempDir, "mixed_workload");
            using var trunk = new BitcaskTrunk<TestDoc>(dir);
            var tree = new Tree<TestDoc>(trunk);
            tree.TtlEnforcementEnabled = false;
            tree.CacheEvictionEnabled = false;

            var errors = new ConcurrentBag<Exception>();
            var cts = new CancellationTokenSource();
            int readCount = 0;

            // Seed data
            for (int i = 0; i < 50; i++)
            {
                tree.Stash(new TestDoc { Id = $"init-{i}", Name = "init", Value = i });
            }
            Thread.Sleep(200);

            // Heavy writer with large payloads
            var writer = Task.Run(() =>
            {
                try
                {
                    var payload = new string('Z', 16384); // 16KB payloads
                    for (int i = 0; i < 1000 && !cts.IsCancellationRequested; i++)
                    {
                        tree.Stash(new TestDoc { Id = $"big-{i}", Name = payload, Value = i });
                    }
                }
                catch (Exception ex) { errors.Add(ex); }
            });

            // Concurrent readers hammering the initial data
            var readers = Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
            {
                var rng = new Random(Environment.CurrentManagedThreadId);
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        var id = $"init-{rng.Next(50)}";
                        var result = tree.Crack(id);
                        if (result != null)
                        {
                            Interlocked.Increment(ref readCount);
                            Assert.Equal(id, result.Id);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { errors.Add(ex); }
            })).ToArray();

            writer.Wait();
            cts.Cancel();
            Task.WaitAll(readers);

            Assert.Empty(errors);
            Assert.True(readCount > 0, "Readers should have completed at least some reads");
        }
    }
}
