using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading;
using AcornDB.Storage;
using Xunit;

namespace AcornDB.Test
{
    /// <summary>
    /// Tests for crash/restart durability, corruption detection, and compaction correctness
    /// of the BitcaskTrunk (Bitcask-style append-only log).
    ///
    /// Note: The trunk stores serialized Nut&lt;T&gt; JSON. On read, Crack() deserializes
    /// the stored bytes as T, and reconstructs Nut metadata (Id, Timestamp, Version) from
    /// the index entry. For full payload round-trip fidelity, use Tree&lt;T&gt; which handles
    /// caching and the Nut wrapper. These tests focus on index reconstruction, metadata
    /// preservation, and structural integrity across restart/compaction/corruption scenarios.
    /// </summary>
    public class BitcaskTrunkCrashRestartTests : IDisposable
    {
        private readonly string _tempDir;

        public class TestDoc
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public int Value { get; set; }
        }

        public BitcaskTrunkCrashRestartTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"acorndb_crash_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private string SubDir(string name) => Path.Combine(_tempDir, name);

        private string DbFilePath(string dir) => Path.Combine(dir, "btree_v2.db");

        #region Basic Restart Tests (Trunk-level: index + metadata)

        /// <summary>
        /// Write N items, dispose, reopen a new trunk instance, verify all keys are recoverable.
        /// Validates that the index is correctly rebuilt from the data file on restart.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        public void WriteN_Dispose_Reopen_AllKeysRecoverable(int count)
        {
            var dir = SubDir($"restart_{count}");

            // Phase 1: Write
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < count; i++)
                {
                    var doc = new TestDoc { Id = $"doc-{i}", Name = $"Name {i}", Value = i };
                    trunk.Stash(doc.Id, new Nut<TestDoc> { Id = doc.Id, Payload = doc, Version = 1 });
                }
                // Let batch flush
                Thread.Sleep(300);
            }

            // Phase 2: Reopen and verify all keys exist
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < count; i++)
                {
                    var nut = trunk.Crack($"doc-{i}");
                    Assert.NotNull(nut);
                    Assert.Equal($"doc-{i}", nut!.Id);
                }
            }
        }

        /// <summary>
        /// Payload round-trip via trunk: the trunk stores serialized Nut&lt;T&gt; and
        /// Crack() deserializes as T. Properties of T that share names with Nut&lt;T&gt;
        /// (like Id) are populated; nested payload fields are not accessible via raw Crack().
        /// Tree&lt;T&gt; loads via CrackAll → Crack into cache, so it also deserializes
        /// stored data as T. This test documents the round-trip behavior.
        /// </summary>
        [Fact]
        public void WriteAndReopen_PayloadFieldsPreserved()
        {
            var dir = SubDir("roundtrip_behavior");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("doc-1", new Nut<TestDoc>
                {
                    Id = "doc-1",
                    Payload = new TestDoc { Id = "doc-1", Name = "hello", Value = 42 },
                    Version = 3
                });
                Thread.Sleep(300);
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                var nut = trunk.Crack("doc-1");
                Assert.NotNull(nut);
                Assert.Equal("doc-1", nut!.Id);
                Assert.Equal(3, nut.Version);
                Assert.Equal("doc-1", nut.Payload.Id);
                Assert.Equal("hello", nut.Payload.Name);
                Assert.Equal(42, nut.Payload.Value);
            }
        }

        /// <summary>
        /// Updates overwrite previous values; after restart only the latest index entry is visible.
        /// The Bitcask model means the last write for a key wins during index reload.
        /// </summary>
        [Fact]
        public void UpdatesAreVisible_AfterRestart()
        {
            var dir = SubDir("update_restart");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("item", new Nut<TestDoc>
                {
                    Id = "item",
                    Payload = new TestDoc { Id = "item", Name = "v1", Value = 1 },
                    Version = 1
                });
                Thread.Sleep(200);

                trunk.Stash("item", new Nut<TestDoc>
                {
                    Id = "item",
                    Payload = new TestDoc { Id = "item", Name = "v2", Value = 2 },
                    Version = 2
                });
                Thread.Sleep(200);

                trunk.Stash("item", new Nut<TestDoc>
                {
                    Id = "item",
                    Payload = new TestDoc { Id = "item", Name = "v3", Value = 3 },
                    Version = 3
                });
                Thread.Sleep(200);
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                var nut = trunk.Crack("item");
                Assert.NotNull(nut);
                // Version comes from the index entry (last write wins)
                Assert.Equal(3, nut!.Version);
            }
        }

        /// <summary>
        /// Toss() writes a tombstone record to disk. On restart, LoadIndex processes
        /// the tombstone and removes the key from the index. Deleted keys stay deleted.
        /// </summary>
        [Fact]
        public void DeleteThenRestart_KeyStaysDeleted()
        {
            var dir = SubDir("delete_restart");

            // Phase 1: Write both keys, ensure flush, then delete one
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("keep", new Nut<TestDoc>
                {
                    Id = "keep",
                    Payload = new TestDoc { Id = "keep", Name = "kept", Value = 1 }
                });
                trunk.Stash("remove", new Nut<TestDoc>
                {
                    Id = "remove",
                    Payload = new TestDoc { Id = "remove", Name = "removed", Value = 2 }
                });
                Thread.Sleep(300);

                Assert.NotNull(trunk.Crack("keep"));
                Assert.NotNull(trunk.Crack("remove"));

                // Toss writes a tombstone record to disk
                trunk.Toss("remove");
            }

            // Phase 2: After restart, tombstone removes key from index — stays deleted.
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                Assert.NotNull(trunk.Crack("keep"));
                Assert.Null(trunk.Crack("remove"));
            }
        }

        /// <summary>
        /// Delete a key, re-stash it, restart — the re-stash record appears after the
        /// tombstone in the log, so LoadIndex restores the key.
        /// </summary>
        [Fact]
        public void DeleteThenReStash_ThenRestart_KeyRestored()
        {
            var dir = SubDir("delete_restash_restart");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("cycle", new Nut<TestDoc>
                {
                    Id = "cycle",
                    Payload = new TestDoc { Id = "cycle", Name = "v1", Value = 1 },
                    Version = 1
                });
                Thread.Sleep(300);

                trunk.Toss("cycle");
                Assert.Null(trunk.Crack("cycle"));

                trunk.Stash("cycle", new Nut<TestDoc>
                {
                    Id = "cycle",
                    Payload = new TestDoc { Id = "cycle", Name = "v2", Value = 2 },
                    Version = 2
                });
                Thread.Sleep(300);

                Assert.NotNull(trunk.Crack("cycle"));
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                var nut = trunk.Crack("cycle");
                Assert.NotNull(nut);
                Assert.Equal(2, nut!.Version);
            }
        }

        /// <summary>
        /// Multiple deletes of different keys — all stay deleted after restart.
        /// </summary>
        [Fact]
        public void DeleteMultipleKeys_ThenRestart_AllStayDeleted()
        {
            var dir = SubDir("delete_multi_restart");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 10; i++)
                {
                    trunk.Stash($"doc-{i}", new Nut<TestDoc>
                    {
                        Id = $"doc-{i}",
                        Payload = new TestDoc { Id = $"doc-{i}", Name = $"n{i}", Value = i }
                    });
                }
                Thread.Sleep(300);

                // Delete even-numbered keys
                for (int i = 0; i < 10; i += 2)
                {
                    trunk.Toss($"doc-{i}");
                }
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 10; i++)
                {
                    var nut = trunk.Crack($"doc-{i}");
                    if (i % 2 == 0)
                        Assert.Null(nut);
                    else
                        Assert.NotNull(nut);
                }

                var all = trunk.CrackAll().ToList();
                Assert.Equal(5, all.Count);
            }
        }

        /// <summary>
        /// Tossing a non-existent key is a no-op (no tombstone written, no exception).
        /// </summary>
        [Fact]
        public void TossNonExistentKey_IsNoOp()
        {
            var dir = SubDir("toss_nonexistent");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("exists", new Nut<TestDoc>
                {
                    Id = "exists",
                    Payload = new TestDoc { Id = "exists" }
                });
                Thread.Sleep(300);

                // Should not throw
                trunk.Toss("does-not-exist");
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                Assert.NotNull(trunk.Crack("exists"));
                Assert.Null(trunk.Crack("does-not-exist"));
            }
        }

        /// <summary>
        /// Tombstone records are correctly validated when CRC validation is enabled.
        /// </summary>
        [Fact]
        public void Tombstone_SurvivesCrcValidation()
        {
            var dir = SubDir("tombstone_crc");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("target", new Nut<TestDoc>
                {
                    Id = "target",
                    Payload = new TestDoc { Id = "target", Name = "doomed", Value = 1 }
                });
                Thread.Sleep(300);
                trunk.Toss("target");
            }

            // Reopen with CRC validation — tombstone CRC should pass
            using (var trunk = new BitcaskTrunk<TestDoc>(dir, validateCrcOnRead: true))
            {
                Assert.Null(trunk.Crack("target"));
            }
        }

        /// <summary>
        /// Delete + compact + restart: compaction only writes live index entries,
        /// so deleted keys should NOT reappear.
        /// </summary>
        [Fact]
        public void DeleteThenCompact_ThenRestart_KeyGone()
        {
            var dir = SubDir("delete_compact_restart");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("keep", new Nut<TestDoc>
                {
                    Id = "keep",
                    Payload = new TestDoc { Id = "keep", Name = "kept", Value = 1 }
                });
                trunk.Stash("remove", new Nut<TestDoc>
                {
                    Id = "remove",
                    Payload = new TestDoc { Id = "remove", Name = "removed", Value = 2 }
                });
                Thread.Sleep(200);

                trunk.Toss("remove");
                trunk.Compact();
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                Assert.NotNull(trunk.Crack("keep"));
                Assert.Null(trunk.Crack("remove")); // compaction purged it
            }
        }

        /// <summary>
        /// Multiple restarts without writes should be idempotent — index rebuilds identically.
        /// </summary>
        [Fact]
        public void MultipleRestarts_WithoutWrites_Stable()
        {
            var dir = SubDir("multi_restart");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 20; i++)
                {
                    trunk.Stash($"k{i}", new Nut<TestDoc>
                    {
                        Id = $"k{i}",
                        Payload = new TestDoc { Id = $"k{i}", Name = $"n{i}", Value = i },
                        Version = i + 1
                    });
                }
                Thread.Sleep(300);
            }

            // Reopen 3 times without writing
            for (int restart = 0; restart < 3; restart++)
            {
                using var trunk = new BitcaskTrunk<TestDoc>(dir);
                for (int i = 0; i < 20; i++)
                {
                    var nut = trunk.Crack($"k{i}");
                    Assert.NotNull(nut);
                    Assert.Equal($"k{i}", nut!.Id);
                    Assert.Equal(i + 1, nut.Version);
                }
            }
        }

        #endregion

        #region Compaction + Restart Tests

        /// <summary>
        /// Write items, compact, reopen, verify all live keys and metadata intact.
        /// </summary>
        [Fact]
        public void Compact_ThenReopen_AllLiveDataIntact()
        {
            var dir = SubDir("compact_reopen");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 50; i++)
                {
                    trunk.Stash($"doc-{i}", new Nut<TestDoc>
                    {
                        Id = $"doc-{i}",
                        Payload = new TestDoc { Id = $"doc-{i}", Name = $"n{i}", Value = i },
                        Version = i + 1
                    });
                }
                Thread.Sleep(200);
                trunk.Compact();
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 50; i++)
                {
                    var nut = trunk.Crack($"doc-{i}");
                    Assert.NotNull(nut);
                    Assert.Equal($"doc-{i}", nut!.Id);
                    Assert.Equal(i + 1, nut.Version);
                }
            }
        }

        /// <summary>
        /// Overwrite values, compact, reopen — only latest index entry visible per key.
        /// Verifies compaction deduplication and last-write-wins on reload.
        /// </summary>
        [Fact]
        public void OverwriteThenCompact_ThenReopen_LatestVersionOnly()
        {
            var dir = SubDir("overwrite_compact");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                // Write original values
                for (int i = 0; i < 30; i++)
                {
                    trunk.Stash($"doc-{i}", new Nut<TestDoc>
                    {
                        Id = $"doc-{i}",
                        Payload = new TestDoc { Id = $"doc-{i}" },
                        Version = 1
                    });
                }
                Thread.Sleep(200);

                // Overwrite half with new version
                for (int i = 0; i < 15; i++)
                {
                    trunk.Stash($"doc-{i}", new Nut<TestDoc>
                    {
                        Id = $"doc-{i}",
                        Payload = new TestDoc { Id = $"doc-{i}" },
                        Version = 2
                    });
                }
                Thread.Sleep(200);

                trunk.Compact();
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                // Updated items should have version 2
                for (int i = 0; i < 15; i++)
                {
                    var nut = trunk.Crack($"doc-{i}");
                    Assert.NotNull(nut);
                    Assert.Equal(2, nut!.Version);
                }

                // Unchanged items should have version 1
                for (int i = 15; i < 30; i++)
                {
                    var nut = trunk.Crack($"doc-{i}");
                    Assert.NotNull(nut);
                    Assert.Equal(1, nut!.Version);
                }
            }
        }

        /// <summary>
        /// Compaction reduces actual data size when there are dead records (overwrites/deletes).
        /// Compares the logical data region, not the MMF-padded file size.
        /// </summary>
        [Fact]
        public void Compact_ReducesDataSize_WithOverwrites()
        {
            var dir = SubDir("compact_datasize");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                // Write and overwrite the same key many times to create dead records
                for (int round = 0; round < 50; round++)
                {
                    trunk.Stash("same-key", new Nut<TestDoc>
                    {
                        Id = "same-key",
                        Payload = new TestDoc { Id = "same-key", Name = new string('X', 1000), Value = round }
                    });
                }
                Thread.Sleep(200);

                var dbPath = DbFilePath(dir);

                // Measure actual data size (sum of valid records) before compaction
                long dataSizeBefore = FindValidDataEnd(dbPath);
                Assert.True(dataSizeBefore > 0, "Should have valid data before compaction");

                trunk.Compact();

                // After compaction, only one record remains (last write for "same-key")
                long dataSizeAfter = FindValidDataEnd(dbPath);
                Assert.True(dataSizeAfter > 0, "Should have valid data after compaction");
                Assert.True(dataSizeAfter < dataSizeBefore,
                    $"Compacted data ({dataSizeAfter}) should be smaller than pre-compaction ({dataSizeBefore})");
            }
        }

        /// <summary>
        /// Write records, compact, write MORE records, then restart.
        /// Validates that _filePosition is correctly set to the compacted data end
        /// (not the MMF-padded file length), so post-compaction writes land immediately
        /// after the compacted data and are recoverable on restart.
        /// Regression test for: _filePosition gap after InitializeMemoryMappedFile in Compact().
        /// </summary>
        [Fact]
        public void Compact_ThenWriteMore_ThenRestart_AllDataAccessible()
        {
            var dir = SubDir("compact_write_restart");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                // Phase 1: Write initial records + overwrites to create dead space
                for (int i = 0; i < 20; i++)
                {
                    trunk.Stash($"pre-{i}", new Nut<TestDoc>
                    {
                        Id = $"pre-{i}",
                        Payload = new TestDoc { Id = $"pre-{i}", Name = $"v1", Value = i },
                        Version = 1
                    });
                }
                Thread.Sleep(200);

                // Overwrite half to create dead records
                for (int i = 0; i < 10; i++)
                {
                    trunk.Stash($"pre-{i}", new Nut<TestDoc>
                    {
                        Id = $"pre-{i}",
                        Payload = new TestDoc { Id = $"pre-{i}", Name = $"v2", Value = i * 10 },
                        Version = 2
                    });
                }
                Thread.Sleep(200);

                // Phase 2: Compact — eliminates dead records, resets file
                trunk.Compact();

                // Phase 3: Write new records AFTER compaction (in same session)
                for (int i = 0; i < 15; i++)
                {
                    trunk.Stash($"post-{i}", new Nut<TestDoc>
                    {
                        Id = $"post-{i}",
                        Payload = new TestDoc { Id = $"post-{i}", Name = $"after-compact", Value = i + 100 },
                        Version = 3
                    });
                }
                Thread.Sleep(200);

                // Verify all data accessible in current session
                for (int i = 0; i < 20; i++)
                    Assert.NotNull(trunk.Crack($"pre-{i}"));
                for (int i = 0; i < 15; i++)
                    Assert.NotNull(trunk.Crack($"post-{i}"));
            }

            // Phase 4: Restart — LoadIndex must find both pre-compaction (rewritten) and post-compaction records
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                // Pre-compaction records (survived compaction)
                for (int i = 0; i < 20; i++)
                {
                    var nut = trunk.Crack($"pre-{i}");
                    Assert.NotNull(nut);
                    if (i < 10)
                        Assert.Equal(2, nut!.Version); // overwritten
                    else
                        Assert.Equal(1, nut!.Version); // original
                }

                // Post-compaction records (written after Compact())
                for (int i = 0; i < 15; i++)
                {
                    var nut = trunk.Crack($"post-{i}");
                    Assert.NotNull(nut);
                    Assert.Equal(3, nut!.Version);
                }

                var all = trunk.CrackAll().ToList();
                Assert.Equal(35, all.Count); // 20 pre + 15 post
            }
        }

        /// <summary>
        /// Metadata (timestamp, version) is preserved through compaction.
        /// </summary>
        [Fact]
        public void Metadata_SurvivesCompaction()
        {
            var dir = SubDir("metadata_compact");
            var ts = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("meta", new Nut<TestDoc>
                {
                    Id = "meta",
                    Payload = new TestDoc { Id = "meta", Name = "pre-compact", Value = 10 },
                    Timestamp = ts,
                    Version = 3
                });
                Thread.Sleep(200);

                trunk.Compact();
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                var nut = trunk.Crack("meta");
                Assert.NotNull(nut);
                Assert.Equal(ts, nut!.Timestamp);
                Assert.Equal(3, nut.Version);
            }
        }

        #endregion

        #region Partial Write / Truncation Tests

        /// <summary>
        /// Simulate a crash mid-record by truncating the file partway through the last record.
        /// On reopen, LoadIndex should stop cleanly at the last valid record.
        /// </summary>
        [Fact]
        public void TruncatedFile_MidRecord_LoadsValidRecordsOnly()
        {
            var dir = SubDir("truncate_mid");

            // Write several records
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 10; i++)
                {
                    trunk.Stash($"doc-{i}", new Nut<TestDoc>
                    {
                        Id = $"doc-{i}",
                        Payload = new TestDoc { Id = $"doc-{i}", Name = $"n{i}", Value = i }
                    });
                }
                Thread.Sleep(300);
            }

            var dbPath = DbFilePath(dir);

            // Find the actual data length by scanning for the end of valid records
            long validDataEnd = FindValidDataEnd(dbPath);
            Assert.True(validDataEnd > 0, "Should have written some valid data");

            // Truncate partway through — cut off the last ~50 bytes to corrupt the last record
            long truncateAt = validDataEnd - 50;
            if (truncateAt < 0) truncateAt = validDataEnd / 2;
            TruncateFile(dbPath, truncateAt);

            // Reopen — should load all records before the truncation point
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                int loaded = 0;
                for (int i = 0; i < 10; i++)
                {
                    if (trunk.Crack($"doc-{i}") != null) loaded++;
                }

                // At least some records should survive (all records before the truncated one)
                Assert.True(loaded > 0, "Should recover at least some records");
                Assert.True(loaded < 10, "Should not recover all records (last was truncated)");
            }
        }

        /// <summary>
        /// File truncated to just the header of the last record (no key/payload).
        /// LoadIndex should stop cleanly.
        /// </summary>
        [Fact]
        public void TruncatedFile_HeaderOnly_LoadsValidRecords()
        {
            var dir = SubDir("truncate_header");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 5; i++)
                {
                    trunk.Stash($"doc-{i}", new Nut<TestDoc>
                    {
                        Id = $"doc-{i}",
                        Payload = new TestDoc { Id = $"doc-{i}", Name = $"n{i}", Value = i },
                        Version = i + 1
                    });
                }
                Thread.Sleep(300);
            }

            var dbPath = DbFilePath(dir);
            long validEnd = FindValidDataEnd(dbPath);

            // Leave valid data + just 20 bytes of a "next" incomplete header
            // (less than V2_HEADER_SIZE of 32 bytes)
            TruncateFile(dbPath, validEnd + 20);

            // Reopen — all 5 records should still be valid
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 5; i++)
                {
                    var nut = trunk.Crack($"doc-{i}");
                    Assert.NotNull(nut);
                    Assert.Equal($"doc-{i}", nut!.Id);
                    Assert.Equal(i + 1, nut.Version);
                }
            }
        }

        /// <summary>
        /// Completely empty file (0 bytes of actual data, but padded by MMF to initial size).
        /// Should open cleanly with no records.
        /// </summary>
        [Fact]
        public void EmptyFile_OpensCleanly()
        {
            var dir = SubDir("empty_file");

            // Create and immediately dispose — no writes
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                // no writes
            }

            // Reopen
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                Assert.Null(trunk.Crack("nonexistent"));
                var all = trunk.CrackAll();
                Assert.Empty(all);
            }
        }

        /// <summary>
        /// File with garbage bytes at the start — LoadIndex should stop immediately
        /// (no valid magic number found).
        /// </summary>
        [Fact]
        public void GarbageAtStart_NoRecordsLoaded()
        {
            var dir = SubDir("garbage_start");
            Directory.CreateDirectory(dir);

            var dbPath = DbFilePath(dir);

            // Write a file with garbage content
            var garbage = new byte[4096];
            new Random(42).NextBytes(garbage);
            File.WriteAllBytes(dbPath, garbage);

            // Open — should load zero records (magic mismatch at position 0)
            using var trunk = new BitcaskTrunk<TestDoc>(dir);
            Assert.Null(trunk.Crack("anything"));
            Assert.Empty(trunk.CrackAll());
        }

        /// <summary>
        /// File truncated to exactly 0 bytes. BitcaskTrunk should treat it as new.
        /// </summary>
        [Fact]
        public void ZeroLengthFile_TreatedAsNew()
        {
            var dir = SubDir("zero_length");

            // Write some data then close
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("test", new Nut<TestDoc>
                {
                    Id = "test",
                    Payload = new TestDoc { Id = "test" }
                });
                Thread.Sleep(200);
            }

            // Truncate to 0
            var dbPath = DbFilePath(dir);
            TruncateFile(dbPath, 0);

            // Reopen — should be empty
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                Assert.Null(trunk.Crack("test"));
                Assert.Empty(trunk.CrackAll());
            }
        }

        #endregion

        #region CRC Corruption Detection Tests

        /// <summary>
        /// Flip a byte in the payload region of a stored record.
        /// On read, the data will be corrupted but without explicit CRC validation on read,
        /// the corruption may manifest as a deserialization error or silent data corruption.
        /// This test documents the current behavior.
        /// </summary>
        [Fact]
        public void CorruptedPayload_DocumentsCurrentBehavior()
        {
            var dir = SubDir("corrupt_payload");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("target", new Nut<TestDoc>
                {
                    Id = "target",
                    Payload = new TestDoc { Id = "target", Name = "important data", Value = 42 }
                });
                Thread.Sleep(300);
            }

            var dbPath = DbFilePath(dir);

            // Find the payload region and corrupt a byte
            CorruptPayloadInFile(dbPath, "target");

            // Reopen and attempt to read — corruption may cause exception or wrong data
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                // The key will be found in the index (index load doesn't validate payload).
                // Reading the corrupted payload either:
                // 1. Fails JSON deserialization (exception)
                // 2. Produces wrong data (silent corruption)
                // Both outcomes confirm CRC isn't validated on read (current behavior).
                try
                {
                    var nut = trunk.Crack("target");
                    // If deserialization succeeds, the data is silently corrupted
                }
                catch (Exception)
                {
                    // JSON parse failure from corrupted payload — expected possible outcome
                }
            }
        }

        /// <summary>
        /// Verify that the CRC stored in a v2 record matches the key + payload bytes.
        /// This validates that CRC is written correctly, even if not checked on read.
        /// </summary>
        [Fact]
        public void V2Record_CRC_IsCorrectlyWritten()
        {
            var dir = SubDir("crc_written");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("crc-test", new Nut<TestDoc>
                {
                    Id = "crc-test",
                    Payload = new TestDoc { Id = "crc-test", Name = "verify crc", Value = 99 }
                });
                Thread.Sleep(300);
            }

            var dbPath = DbFilePath(dir);

            // Read the raw file and verify CRC
            using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            // Read v2 header
            int magic = reader.ReadInt32();
            Assert.Equal(0x41435232, magic); // ACR2

            short formatVer = reader.ReadInt16();
            Assert.Equal(2, formatVer);

            short flags = reader.ReadInt16();
            int keyLen = reader.ReadInt32();
            int payloadLen = reader.ReadInt32();
            long timestamp = reader.ReadInt64();
            int version = reader.ReadInt32();
            uint storedCrc = reader.ReadUInt32();

            // Read key and payload
            byte[] keyBytes = reader.ReadBytes(keyLen);
            byte[] payloadBytes = reader.ReadBytes(payloadLen);

            // Compute expected CRC
            var crc = new Crc32();
            crc.Append(keyBytes);
            crc.Append(payloadBytes);
            uint expectedCrc = crc.GetCurrentHashAsUInt32();

            Assert.Equal(expectedCrc, storedCrc);

            // Also verify key decodes correctly
            string key = Encoding.UTF8.GetString(keyBytes);
            Assert.Equal("crc-test", key);
        }

        /// <summary>
        /// Corrupt the CRC field itself. Without validateCrcOnRead, the record still loads.
        /// </summary>
        [Fact]
        public void CorruptedCRC_RecordStillLoads_WhenValidationDisabled()
        {
            var dir = SubDir("corrupt_crc");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("crc-victim", new Nut<TestDoc>
                {
                    Id = "crc-victim",
                    Payload = new TestDoc { Id = "crc-victim", Name = "test", Value = 1 }
                });
                Thread.Sleep(300);
            }

            var dbPath = DbFilePath(dir);

            // Corrupt the CRC field (bytes 28-31 in the first record)
            CorruptBytesInFile(dbPath, 28, 4);

            // Reopen without CRC validation — record should still load
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                var nut = trunk.Crack("crc-victim");
                Assert.NotNull(nut);
                Assert.Equal("crc-victim", nut!.Id);
            }
        }

        /// <summary>
        /// With CRC validation enabled, corrupting the CRC field causes the record to be
        /// rejected during index load (returns null from Crack, not in index).
        /// </summary>
        [Fact]
        public void CorruptedCRC_RecordRejectedAtIndexLoad_WhenValidationEnabled()
        {
            var dir = SubDir("crc_crack_throws");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("crc-victim", new Nut<TestDoc>
                {
                    Id = "crc-victim",
                    Payload = new TestDoc { Id = "crc-victim", Name = "test", Value = 1 }
                });
                Thread.Sleep(300);
            }

            var dbPath = DbFilePath(dir);
            CorruptBytesInFile(dbPath, 28, 4);

            // CRC validation rejects the record during index load — Crack returns null
            using (var trunk = new BitcaskTrunk<TestDoc>(dir, validateCrcOnRead: true))
            {
                Assert.Null(trunk.Crack("crc-victim"));
            }
        }

        /// <summary>
        /// With CRC validation enabled, corrupting payload bytes causes the record to be
        /// rejected during index load (CRC mismatch detected before the record enters the index).
        /// </summary>
        [Fact]
        public void CorruptedPayload_RejectedAtIndexLoad_WhenValidationEnabled()
        {
            var dir = SubDir("crc_payload_corrupt");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("target", new Nut<TestDoc>
                {
                    Id = "target",
                    Payload = new TestDoc { Id = "target", Name = "important data", Value = 42 }
                });
                Thread.Sleep(300);
            }

            var dbPath = DbFilePath(dir);
            CorruptPayloadInFile(dbPath, "target");

            // CRC validation during index load catches the payload corruption
            using (var trunk = new BitcaskTrunk<TestDoc>(dir, validateCrcOnRead: true))
            {
                Assert.Null(trunk.Crack("target"));
            }
        }

        /// <summary>
        /// CRC validation in Crack() catches corruption that occurs after index load
        /// (e.g., bitflip in a file that was already indexed). To test this, we corrupt
        /// the file after the trunk has loaded and the record is already in the index.
        /// </summary>
        [Fact]
        public void CorruptedPayloadAfterIndexLoad_ThrowsCrcOnCrack()
        {
            var dir = SubDir("crc_crack_post_index");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir, validateCrcOnRead: true))
            {
                trunk.Stash("target", new Nut<TestDoc>
                {
                    Id = "target",
                    Payload = new TestDoc { Id = "target", Name = "important", Value = 1 }
                });
                Thread.Sleep(300);

                // Record is in-memory index. Now corrupt the payload on disk.
                var dbPath = DbFilePath(dir);
                CorruptPayloadInFile(dbPath, "target");

                // Crack() validates CRC from the on-disk bytes → detects corruption
                Assert.Throws<CrcValidationException>(() => trunk.Crack("target"));
            }
        }

        /// <summary>
        /// With CRC validation enabled during index load, a corrupted CRC stops loading at
        /// that record (same as corrupted magic/keylen — treated as end of valid data).
        /// </summary>
        [Fact]
        public void CorruptedCRC_StopsIndexLoad_WhenValidationEnabled()
        {
            var dir = SubDir("crc_index_stop");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("first", new Nut<TestDoc>
                {
                    Id = "first",
                    Payload = new TestDoc { Id = "first", Name = "first", Value = 1 }
                });
                trunk.Stash("second", new Nut<TestDoc>
                {
                    Id = "second",
                    Payload = new TestDoc { Id = "second", Name = "second", Value = 2 }
                });
                trunk.Stash("third", new Nut<TestDoc>
                {
                    Id = "third",
                    Payload = new TestDoc { Id = "third", Name = "third", Value = 3 }
                });
                Thread.Sleep(300);
            }

            var dbPath = DbFilePath(dir);

            // Corrupt the CRC of the second record
            long secondOffset = FindNthRecordOffset(dbPath, 1);
            Assert.True(secondOffset > 0);
            CorruptBytesInFile(dbPath, secondOffset + 28, 4);

            // With CRC validation, only the first record should load
            using (var trunk = new BitcaskTrunk<TestDoc>(dir, validateCrcOnRead: true))
            {
                Assert.NotNull(trunk.Crack("first"));
                Assert.Null(trunk.Crack("second")); // CRC mismatch stops index load
                Assert.Null(trunk.Crack("third"));  // unreachable past corruption
            }
        }

        /// <summary>
        /// Valid data passes CRC validation without issue — no false positives.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        public void ValidData_PassesCrcValidation(int count)
        {
            var dir = SubDir($"crc_valid_{count}");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < count; i++)
                {
                    trunk.Stash($"key-{i}", new Nut<TestDoc>
                    {
                        Id = $"key-{i}",
                        Payload = new TestDoc { Id = $"key-{i}", Name = $"item {i}", Value = i }
                    });
                }
                Thread.Sleep(300);
            }

            // Reopen with CRC validation — all reads should succeed
            using (var trunk = new BitcaskTrunk<TestDoc>(dir, validateCrcOnRead: true))
            {
                for (int i = 0; i < count; i++)
                {
                    var nut = trunk.Crack($"key-{i}");
                    Assert.NotNull(nut);
                    Assert.Equal($"key-{i}", nut!.Id);
                }

                var all = trunk.CrackAll().ToList();
                Assert.Equal(count, all.Count);
            }
        }

        /// <summary>
        /// CRC validation works correctly after compaction (compacted records have fresh CRCs).
        /// </summary>
        [Fact]
        public void CrcValidation_WorksAfterCompaction()
        {
            var dir = SubDir("crc_after_compact");

            // Write + overwrite to create dead records
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 20; i++)
                {
                    trunk.Stash($"key-{i}", new Nut<TestDoc>
                    {
                        Id = $"key-{i}",
                        Payload = new TestDoc { Id = $"key-{i}", Name = $"v1", Value = i }
                    });
                }
                Thread.Sleep(300);

                // Overwrite half to create dead records
                for (int i = 0; i < 10; i++)
                {
                    trunk.Stash($"key-{i}", new Nut<TestDoc>
                    {
                        Id = $"key-{i}",
                        Payload = new TestDoc { Id = $"key-{i}", Name = $"v2", Value = i * 10 }
                    });
                }
                Thread.Sleep(300);

                trunk.Compact();
            }

            // Reopen with CRC validation
            using (var trunk = new BitcaskTrunk<TestDoc>(dir, validateCrcOnRead: true))
            {
                for (int i = 0; i < 20; i++)
                {
                    var nut = trunk.Crack($"key-{i}");
                    Assert.NotNull(nut);
                }
            }
        }

        /// <summary>
        /// Corrupt the magic number of the second record. LoadIndex should stop
        /// at the corruption point, loading only the first record.
        /// </summary>
        [Fact]
        public void CorruptedMagic_StopsAtCorruption()
        {
            var dir = SubDir("corrupt_magic");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("first", new Nut<TestDoc>
                {
                    Id = "first",
                    Payload = new TestDoc { Id = "first", Name = "first", Value = 1 }
                });
                trunk.Stash("second", new Nut<TestDoc>
                {
                    Id = "second",
                    Payload = new TestDoc { Id = "second", Name = "second", Value = 2 }
                });
                trunk.Stash("third", new Nut<TestDoc>
                {
                    Id = "third",
                    Payload = new TestDoc { Id = "third", Name = "third", Value = 3 }
                });
                Thread.Sleep(300);
            }

            var dbPath = DbFilePath(dir);

            // Find offset of second record and corrupt its magic
            long secondRecordOffset = FindNthRecordOffset(dbPath, 1);
            Assert.True(secondRecordOffset > 0, "Should find second record");

            CorruptBytesInFile(dbPath, secondRecordOffset, 4);

            // Reopen — should load only the first record
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                Assert.NotNull(trunk.Crack("first"));
                Assert.Null(trunk.Crack("second")); // corrupted magic stops index load
                Assert.Null(trunk.Crack("third"));  // unreachable past corruption
            }
        }

        /// <summary>
        /// Corrupt the key length field to an absurdly large value.
        /// LoadIndex should reject the record via sanity checks.
        /// </summary>
        [Fact]
        public void CorruptedKeyLength_RecordRejected()
        {
            var dir = SubDir("corrupt_keylen");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("victim", new Nut<TestDoc>
                {
                    Id = "victim",
                    Payload = new TestDoc { Id = "victim", Name = "data", Value = 1 }
                });
                Thread.Sleep(300);
            }

            var dbPath = DbFilePath(dir);

            // Corrupt KeyLen field (bytes 8-11) with a huge value
            using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                fs.Seek(8, SeekOrigin.Begin);
                var buf = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(buf, 0x7FFFFFFF); // ~2GB key
                fs.Write(buf, 0, 4);
            }

            // Reopen — sanity check rejects the record
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                Assert.Null(trunk.Crack("victim"));
                Assert.Empty(trunk.CrackAll());
            }
        }

        /// <summary>
        /// Corrupt the payload length field to a negative value.
        /// LoadIndex should reject the record via sanity checks.
        /// </summary>
        [Fact]
        public void CorruptedPayloadLength_RecordRejected()
        {
            var dir = SubDir("corrupt_payloadlen");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("victim", new Nut<TestDoc>
                {
                    Id = "victim",
                    Payload = new TestDoc { Id = "victim", Name = "data", Value = 1 }
                });
                Thread.Sleep(300);
            }

            var dbPath = DbFilePath(dir);

            // Corrupt PayloadLen field (bytes 12-15) with a negative value
            using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                fs.Seek(12, SeekOrigin.Begin);
                var buf = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(buf, -1);
                fs.Write(buf, 0, 4);
            }

            // Reopen — sanity check rejects the record
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                Assert.Null(trunk.Crack("victim"));
                Assert.Empty(trunk.CrackAll());
            }
        }

        #endregion

        #region Metadata Preservation Tests

        /// <summary>
        /// Timestamp and version should survive restart (stored in record header, rebuilt into index).
        /// </summary>
        [Fact]
        public void Metadata_Timestamp_Version_SurviveRestart()
        {
            var dir = SubDir("metadata_restart");
            var ts = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc);

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("meta", new Nut<TestDoc>
                {
                    Id = "meta",
                    Payload = new TestDoc { Id = "meta", Name = "metadata test", Value = 7 },
                    Timestamp = ts,
                    Version = 5
                });
                Thread.Sleep(300);
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                var nut = trunk.Crack("meta");
                Assert.NotNull(nut);
                Assert.Equal(ts, nut!.Timestamp);
                Assert.Equal(5, nut.Version);
            }
        }

        /// <summary>
        /// Different keys with different versions/timestamps all survive restart.
        /// </summary>
        [Fact]
        public void MultipleKeys_DifferentMetadata_SurviveRestart()
        {
            var dir = SubDir("multi_metadata");
            var baseTs = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 10; i++)
                {
                    trunk.Stash($"key-{i}", new Nut<TestDoc>
                    {
                        Id = $"key-{i}",
                        Payload = new TestDoc { Id = $"key-{i}" },
                        Timestamp = baseTs.AddHours(i),
                        Version = i + 1
                    });
                }
                Thread.Sleep(300);
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 10; i++)
                {
                    var nut = trunk.Crack($"key-{i}");
                    Assert.NotNull(nut);
                    Assert.Equal(baseTs.AddHours(i), nut!.Timestamp);
                    Assert.Equal(i + 1, nut.Version);
                }
            }
        }

        #endregion

        #region Edge Case Tests

        /// <summary>
        /// Large payloads survive restart (key is found, data is readable).
        /// </summary>
        [Fact]
        public void LargePayload_SurvivesRestart()
        {
            var dir = SubDir("large_payload");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash("big", new Nut<TestDoc>
                {
                    Id = "big",
                    Payload = new TestDoc { Id = "big", Name = new string('A', 100_000), Value = 42 }
                });
                Thread.Sleep(300);
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                var nut = trunk.Crack("big");
                Assert.NotNull(nut);
                Assert.Equal("big", nut!.Id);
            }
        }

        /// <summary>
        /// Unicode keys survive restart (index key matching works correctly).
        /// </summary>
        [Fact]
        public void UnicodeKeys_SurviveRestart()
        {
            var dir = SubDir("unicode_restart");
            var unicodeKey = "日本語-キー-🎉";

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                trunk.Stash(unicodeKey, new Nut<TestDoc>
                {
                    Id = unicodeKey,
                    Payload = new TestDoc { Id = unicodeKey, Name = "unicode", Value = 1 }
                });
                Thread.Sleep(300);
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                var nut = trunk.Crack(unicodeKey);
                Assert.NotNull(nut);
                Assert.Equal(unicodeKey, nut!.Id);
            }
        }

        /// <summary>
        /// CrackAll returns correct count after restart.
        /// Note: CrackAll iterates the in-memory index, which is lazily loaded
        /// on the first Crack() call. We must trigger index loading first.
        /// </summary>
        [Fact]
        public void CrackAll_AfterRestart_ReturnsAllItems()
        {
            var dir = SubDir("crackall_restart");

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 25; i++)
                {
                    trunk.Stash($"item-{i}", new Nut<TestDoc>
                    {
                        Id = $"item-{i}",
                        Payload = new TestDoc { Id = $"item-{i}", Name = $"n{i}", Value = i }
                    });
                }
                Thread.Sleep(300);
            }

            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                // Trigger lazy index loading via a Crack call
                trunk.Crack("item-0");

                var all = trunk.CrackAll().ToList();
                Assert.Equal(25, all.Count);

                var ids = all.Select(n => n.Id).OrderBy(id => id).ToList();
                for (int i = 0; i < 25; i++)
                {
                    Assert.Contains($"item-{i}", ids);
                }
            }
        }

        /// <summary>
        /// Write after restart appends correctly — both old and new data are accessible.
        /// </summary>
        [Fact]
        public void WriteAfterRestart_OldAndNewDataAccessible()
        {
            var dir = SubDir("write_after_restart");

            // Phase 1: Write initial data
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                for (int i = 0; i < 10; i++)
                {
                    trunk.Stash($"old-{i}", new Nut<TestDoc>
                    {
                        Id = $"old-{i}",
                        Payload = new TestDoc { Id = $"old-{i}" },
                        Version = 1
                    });
                }
                Thread.Sleep(300);
            }

            // Phase 2: Reopen, trigger index load (via a read), then write new data
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                // Trigger lazy index loading so _filePosition is set correctly
                // (without this, writes go to end of MMF-padded file, creating a gap)
                Assert.NotNull(trunk.Crack("old-0"));

                for (int i = 0; i < 10; i++)
                {
                    trunk.Stash($"new-{i}", new Nut<TestDoc>
                    {
                        Id = $"new-{i}",
                        Payload = new TestDoc { Id = $"new-{i}" },
                        Version = 2
                    });
                }
                Thread.Sleep(300);

                // Both old and new keys should be accessible
                for (int i = 0; i < 10; i++)
                {
                    Assert.NotNull(trunk.Crack($"old-{i}"));
                    Assert.NotNull(trunk.Crack($"new-{i}"));
                }
            }

            // Phase 3: Reopen again and verify all data
            using (var trunk = new BitcaskTrunk<TestDoc>(dir))
            {
                // Trigger lazy index load
                trunk.Crack("old-0");

                var all = trunk.CrackAll().ToList();
                Assert.Equal(20, all.Count);
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Find the end of valid record data by scanning v2 records from the start.
        /// </summary>
        private static long FindValidDataEnd(string dbPath)
        {
            using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            long position = 0;
            long fileLen = fs.Length;

            while (position + 32 <= fileLen)
            {
                fs.Seek(position, SeekOrigin.Begin);
                int magic = reader.ReadInt32();
                if (magic != 0x41435232) break; // Not ACR2

                short fmtVer = reader.ReadInt16();
                if (fmtVer != 2) break;

                reader.ReadInt16(); // flags
                int keyLen = reader.ReadInt32();
                int payloadLen = reader.ReadInt32();

                if (keyLen <= 0 || payloadLen < 0) break;

                long recordSize = 32 + keyLen + payloadLen;
                if (position + recordSize > fileLen) break;

                position += recordSize;
            }

            return position;
        }

        /// <summary>
        /// Find the file offset of the Nth record (0-based).
        /// </summary>
        private static long FindNthRecordOffset(string dbPath, int n)
        {
            using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            long position = 0;
            long fileLen = fs.Length;
            int recordIndex = 0;

            while (position + 32 <= fileLen)
            {
                if (recordIndex == n) return position;

                fs.Seek(position, SeekOrigin.Begin);
                int magic = reader.ReadInt32();
                if (magic != 0x41435232) break;

                short fmtVer = reader.ReadInt16();
                if (fmtVer != 2) break;

                reader.ReadInt16(); // flags
                int keyLen = reader.ReadInt32();
                int payloadLen = reader.ReadInt32();

                if (keyLen <= 0 || payloadLen < 0) break;

                position += 32 + keyLen + payloadLen;
                recordIndex++;
            }

            if (recordIndex == n) return position;
            return -1;
        }

        /// <summary>
        /// Truncate a file to the specified length, preserving existing content.
        /// </summary>
        private static void TruncateFile(string dbPath, long newLength)
        {
            using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            fs.SetLength(newLength);
        }

        /// <summary>
        /// Corrupt bytes in a file at the specified offset by flipping all bits.
        /// </summary>
        private static void CorruptBytesInFile(string dbPath, long offset, int count)
        {
            using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            fs.Seek(offset, SeekOrigin.Begin);
            var buf = new byte[count];
            fs.Read(buf, 0, count);
            for (int i = 0; i < count; i++) buf[i] = (byte)~buf[i];
            fs.Seek(offset, SeekOrigin.Begin);
            fs.Write(buf, 0, count);
        }

        /// <summary>
        /// Find the payload region for a key in a v2 file and corrupt a byte in it.
        /// </summary>
        private static void CorruptPayloadInFile(string dbPath, string targetKey)
        {
            using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            using var reader = new BinaryReader(fs);

            long position = 0;
            long fileLen = fs.Length;

            while (position + 32 <= fileLen)
            {
                fs.Seek(position, SeekOrigin.Begin);
                int magic = reader.ReadInt32();
                if (magic != 0x41435232) break;

                short fmtVer = reader.ReadInt16();
                if (fmtVer != 2) break;

                reader.ReadInt16(); // flags
                int keyLen = reader.ReadInt32();
                int payloadLen = reader.ReadInt32();
                reader.ReadInt64(); // timestamp
                reader.ReadInt32(); // version
                reader.ReadUInt32(); // crc

                var keyBytes = reader.ReadBytes(keyLen);
                string key = Encoding.UTF8.GetString(keyBytes);

                if (key == targetKey && payloadLen > 0)
                {
                    // Corrupt the first byte of the payload
                    long payloadStart = position + 32 + keyLen;
                    fs.Seek(payloadStart, SeekOrigin.Begin);
                    byte b = (byte)fs.ReadByte();
                    fs.Seek(payloadStart, SeekOrigin.Begin);
                    fs.WriteByte((byte)~b);
                    return;
                }

                position += 32 + keyLen + payloadLen;
            }

            throw new InvalidOperationException($"Key '{targetKey}' not found in file");
        }

        #endregion
    }
}
