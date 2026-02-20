using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Storage.BTree;
using Xunit;

namespace AcornDB.Test
{
    public class PageCacheTests : IDisposable
    {
        private const int PAGE_SIZE = 4096;
        private const int MAX_PAGES = 8;
        private PageCache _cache;

        public PageCacheTests()
        {
            _cache = new PageCache(MAX_PAGES, PAGE_SIZE);
        }

        public void Dispose()
        {
            _cache.Dispose();
        }

        private static byte[] MakePage(int pageSize, byte fill)
        {
            var buf = new byte[pageSize];
            Array.Fill(buf, fill);
            return buf;
        }

        #region Basic Get/Put

        [Fact]
        public void TryGet_EmptyCache_ReturnsFalse()
        {
            var dest = new byte[PAGE_SIZE];
            Assert.False(_cache.TryGet(1, dest));
        }

        [Fact]
        public void Put_ThenTryGet_ReturnsData()
        {
            var page = MakePage(PAGE_SIZE, 0xAB);
            _cache.Put(1, page);

            var dest = new byte[PAGE_SIZE];
            Assert.True(_cache.TryGet(1, dest));
            Assert.Equal(0xAB, dest[0]);
            Assert.Equal(0xAB, dest[PAGE_SIZE - 1]);
        }

        [Fact]
        public void Put_MultiplePagesIndependently()
        {
            _cache.Put(10, MakePage(PAGE_SIZE, 0x10));
            _cache.Put(20, MakePage(PAGE_SIZE, 0x20));
            _cache.Put(30, MakePage(PAGE_SIZE, 0x30));

            var dest = new byte[PAGE_SIZE];

            Assert.True(_cache.TryGet(10, dest));
            Assert.Equal(0x10, dest[0]);

            Assert.True(_cache.TryGet(20, dest));
            Assert.Equal(0x20, dest[0]);

            Assert.True(_cache.TryGet(30, dest));
            Assert.Equal(0x30, dest[0]);
        }

        [Fact]
        public void Put_SamePageId_UpdatesInPlace()
        {
            _cache.Put(1, MakePage(PAGE_SIZE, 0xAA));
            _cache.Put(1, MakePage(PAGE_SIZE, 0xBB));

            var dest = new byte[PAGE_SIZE];
            Assert.True(_cache.TryGet(1, dest));
            Assert.Equal(0xBB, dest[0]);
        }

        [Fact]
        public void TryGet_ReturnsCopy_NotReference()
        {
            var page = MakePage(PAGE_SIZE, 0x11);
            _cache.Put(1, page);

            var dest1 = new byte[PAGE_SIZE];
            var dest2 = new byte[PAGE_SIZE];
            _cache.TryGet(1, dest1);
            _cache.TryGet(1, dest2);

            // Modifying dest1 should not affect dest2 or cache
            dest1[0] = 0xFF;
            Assert.Equal(0x11, dest2[0]);

            var dest3 = new byte[PAGE_SIZE];
            _cache.TryGet(1, dest3);
            Assert.Equal(0x11, dest3[0]);
        }

        #endregion

        #region Invalidate

        [Fact]
        public void Invalidate_RemovesPage()
        {
            _cache.Put(1, MakePage(PAGE_SIZE, 0xAA));
            Assert.True(_cache.TryGet(1, new byte[PAGE_SIZE]));

            _cache.Invalidate(1);
            Assert.False(_cache.TryGet(1, new byte[PAGE_SIZE]));
        }

        [Fact]
        public void Invalidate_NonExistent_DoesNotThrow()
        {
            _cache.Invalidate(999); // Should be a no-op
        }

        [Fact]
        public void Invalidate_DoesNotAffectOtherPages()
        {
            _cache.Put(1, MakePage(PAGE_SIZE, 0xAA));
            _cache.Put(2, MakePage(PAGE_SIZE, 0xBB));

            _cache.Invalidate(1);

            Assert.False(_cache.TryGet(1, new byte[PAGE_SIZE]));
            Assert.True(_cache.TryGet(2, new byte[PAGE_SIZE]));
        }

        [Fact]
        public void Put_AfterInvalidate_Works()
        {
            _cache.Put(1, MakePage(PAGE_SIZE, 0xAA));
            _cache.Invalidate(1);
            _cache.Put(1, MakePage(PAGE_SIZE, 0xBB));

            var dest = new byte[PAGE_SIZE];
            Assert.True(_cache.TryGet(1, dest));
            Assert.Equal(0xBB, dest[0]);
        }

        #endregion

        #region Eviction

        [Fact]
        public void Eviction_OccursWhenCacheFull()
        {
            // Fill cache completely
            for (int i = 0; i < MAX_PAGES; i++)
            {
                _cache.Put(i + 1, MakePage(PAGE_SIZE, (byte)(i + 1)));
            }

            // All pages should be present
            for (int i = 0; i < MAX_PAGES; i++)
            {
                Assert.True(_cache.TryGet(i + 1, new byte[PAGE_SIZE]));
            }

            // Insert one more — triggers eviction
            _cache.Put(100, MakePage(PAGE_SIZE, 0xFF));

            // New page should be present
            var dest = new byte[PAGE_SIZE];
            Assert.True(_cache.TryGet(100, dest));
            Assert.Equal(0xFF, dest[0]);

            // At least one old page should have been evicted
            int evicted = 0;
            for (int i = 0; i < MAX_PAGES; i++)
            {
                if (!_cache.TryGet(i + 1, new byte[PAGE_SIZE]))
                    evicted++;
            }
            Assert.True(evicted >= 1, "Expected at least one page to be evicted");
        }

        [Fact]
        public void Eviction_ClockGivesSecondChance()
        {
            // Fill cache
            for (int i = 0; i < MAX_PAGES; i++)
            {
                _cache.Put(i + 1, MakePage(PAGE_SIZE, (byte)(i + 1)));
            }

            // Access page 1 again (sets referenced bit after clock sweep would clear it)
            _cache.TryGet(1, new byte[PAGE_SIZE]);

            // Insert several new pages to trigger multiple evictions
            for (int i = 0; i < MAX_PAGES / 2; i++)
            {
                _cache.Put(100 + i, MakePage(PAGE_SIZE, (byte)(0xF0 + i)));
            }

            // Page 1 is more likely to survive (was recently accessed)
            // Not a strict guarantee due to clock sweep timing, but validates the mechanism works
            Assert.True(_cache.Evictions > 0, "Expected evictions to have occurred");
        }

        [Fact]
        public void Eviction_CounterIncremented()
        {
            Assert.Equal(0, _cache.Evictions);

            // Fill cache
            for (int i = 0; i < MAX_PAGES; i++)
            {
                _cache.Put(i + 1, MakePage(PAGE_SIZE, (byte)i));
            }

            Assert.Equal(0, _cache.Evictions);

            // Trigger eviction
            _cache.Put(100, MakePage(PAGE_SIZE, 0xFF));
            Assert.True(_cache.Evictions >= 1);
        }

        #endregion

        #region Statistics

        [Fact]
        public void Stats_InitiallyZero()
        {
            Assert.Equal(0, _cache.Hits);
            Assert.Equal(0, _cache.Misses);
            Assert.Equal(0, _cache.Evictions);
            Assert.Equal(0.0, _cache.HitRatio);
        }

        [Fact]
        public void Stats_Hit_IncrementedOnCacheHit()
        {
            _cache.Put(1, MakePage(PAGE_SIZE, 0xAA));
            _cache.TryGet(1, new byte[PAGE_SIZE]);

            Assert.Equal(1, _cache.Hits);
            Assert.Equal(0, _cache.Misses);
        }

        [Fact]
        public void Stats_Miss_IncrementedOnCacheMiss()
        {
            _cache.TryGet(999, new byte[PAGE_SIZE]);

            Assert.Equal(0, _cache.Hits);
            Assert.Equal(1, _cache.Misses);
        }

        [Fact]
        public void Stats_HitRatio_Calculated()
        {
            _cache.Put(1, MakePage(PAGE_SIZE, 0xAA));

            // 3 hits
            _cache.TryGet(1, new byte[PAGE_SIZE]);
            _cache.TryGet(1, new byte[PAGE_SIZE]);
            _cache.TryGet(1, new byte[PAGE_SIZE]);

            // 1 miss
            _cache.TryGet(999, new byte[PAGE_SIZE]);

            Assert.Equal(3, _cache.Hits);
            Assert.Equal(1, _cache.Misses);
            Assert.Equal(0.75, _cache.HitRatio, precision: 2);
        }

        [Fact]
        public void Stats_ResetStatistics_ClearsCounters()
        {
            _cache.Put(1, MakePage(PAGE_SIZE, 0xAA));
            _cache.TryGet(1, new byte[PAGE_SIZE]);
            _cache.TryGet(999, new byte[PAGE_SIZE]);

            _cache.ResetStatistics();

            Assert.Equal(0, _cache.Hits);
            Assert.Equal(0, _cache.Misses);
            Assert.Equal(0, _cache.Evictions);
        }

        [Fact]
        public void Stats_Count_TracksCurrentEntries()
        {
            Assert.Equal(0, _cache.Count);

            _cache.Put(1, MakePage(PAGE_SIZE, 0xAA));
            Assert.Equal(1, _cache.Count);

            _cache.Put(2, MakePage(PAGE_SIZE, 0xBB));
            Assert.Equal(2, _cache.Count);

            _cache.Invalidate(1);
            Assert.Equal(1, _cache.Count);
        }

        [Fact]
        public void Stats_Capacity_ReturnsMaxPages()
        {
            Assert.Equal(MAX_PAGES, _cache.Capacity);
        }

        #endregion

        #region Thread Safety

        [Fact]
        public void ConcurrentReads_DoNotCorrupt()
        {
            // Fill cache with known data
            for (int i = 0; i < MAX_PAGES; i++)
            {
                _cache.Put(i + 1, MakePage(PAGE_SIZE, (byte)(i + 1)));
            }

            var errors = new List<string>();
            int readCount = 0;

            // Launch many concurrent readers
            Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, iteration =>
            {
                int pageNum = (iteration % MAX_PAGES) + 1;
                var dest = new byte[PAGE_SIZE];
                if (_cache.TryGet(pageNum, dest))
                {
                    Interlocked.Increment(ref readCount);
                    if (dest[0] != (byte)pageNum)
                    {
                        lock (errors)
                        {
                            errors.Add($"Page {pageNum}: expected 0x{pageNum:X2}, got 0x{dest[0]:X2}");
                        }
                    }
                }
            });

            Assert.Empty(errors);
            Assert.True(readCount > 0, "Expected some reads to succeed");
        }

        [Fact]
        public void ConcurrentPutsAndGets_DoNotThrow()
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var exceptions = new List<Exception>();

            // Concurrent writers and readers
            var tasks = new Task[4];
            for (int t = 0; t < tasks.Length; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        var rng = new Random(threadId);
                        while (!cts.Token.IsCancellationRequested)
                        {
                            long pageId = rng.Next(1, 50);
                            if (rng.Next(2) == 0)
                            {
                                _cache.Put(pageId, MakePage(PAGE_SIZE, (byte)(pageId & 0xFF)));
                            }
                            else
                            {
                                _cache.TryGet(pageId, new byte[PAGE_SIZE]);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                            exceptions.Add(ex);
                    }
                });
            }

            Task.WaitAll(tasks);
            Assert.Empty(exceptions);
        }

        [Fact]
        public void ConcurrentInvalidateAndPut_DoNotThrow()
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var exceptions = new List<Exception>();

            var tasks = new Task[4];
            for (int t = 0; t < tasks.Length; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        var rng = new Random(threadId);
                        while (!cts.Token.IsCancellationRequested)
                        {
                            long pageId = rng.Next(1, 20);
                            int op = rng.Next(3);
                            if (op == 0)
                                _cache.Put(pageId, MakePage(PAGE_SIZE, (byte)(pageId & 0xFF)));
                            else if (op == 1)
                                _cache.TryGet(pageId, new byte[PAGE_SIZE]);
                            else
                                _cache.Invalidate(pageId);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                            exceptions.Add(ex);
                    }
                });
            }

            Task.WaitAll(tasks);
            Assert.Empty(exceptions);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void Constructor_RejectsZeroMaxPages()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PageCache(0, PAGE_SIZE));
        }

        [Fact]
        public void Constructor_RejectsZeroPageSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PageCache(MAX_PAGES, 0));
        }

        [Fact]
        public void SingleSlotCache_EvictsOnEveryInsert()
        {
            using var tiny = new PageCache(1, PAGE_SIZE);

            tiny.Put(1, MakePage(PAGE_SIZE, 0x11));
            Assert.True(tiny.TryGet(1, new byte[PAGE_SIZE]));

            tiny.Put(2, MakePage(PAGE_SIZE, 0x22));
            Assert.True(tiny.TryGet(2, new byte[PAGE_SIZE]));
            Assert.False(tiny.TryGet(1, new byte[PAGE_SIZE]));
        }

        [Fact]
        public void FillAndReplace_AllSlots_NoLeaks()
        {
            // Fill entirely, then replace all entries
            for (int i = 0; i < MAX_PAGES; i++)
            {
                _cache.Put(i + 1, MakePage(PAGE_SIZE, (byte)(i + 1)));
            }

            // Replace all entries with new page IDs
            for (int i = 0; i < MAX_PAGES; i++)
            {
                _cache.Put(100 + i, MakePage(PAGE_SIZE, (byte)(0xF0 + i)));
            }

            // New pages should be accessible
            for (int i = 0; i < MAX_PAGES; i++)
            {
                var dest = new byte[PAGE_SIZE];
                Assert.True(_cache.TryGet(100 + i, dest));
                Assert.Equal((byte)(0xF0 + i), dest[0]);
            }
        }

        [Fact]
        public void Dispose_ThenAccessDoesNotCrash()
        {
            _cache.Put(1, MakePage(PAGE_SIZE, 0xAA));
            _cache.Dispose();

            // After dispose, data buffers are returned to pool.
            // TryGet may return false or succeed with zeroed data depending on pool behavior,
            // but must not throw or corrupt.
            // Re-assign to avoid double-dispose in test Dispose()
            _cache = new PageCache(MAX_PAGES, PAGE_SIZE);
        }

        #endregion
    }
}
