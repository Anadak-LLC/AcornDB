using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcornDB;
using AcornDB.Storage.BTree;
using Xunit;

namespace AcornDB.Test
{
    /// <summary>
    /// Crash/restart and corruption-detection tests for BTreeTrunk.
    ///
    /// Categories:
    ///   1. Basic restart persistence (write → dispose → reopen → verify)
    ///   2. Update persistence (update → dispose → reopen → verify latest values)
    ///   3. Delete persistence (delete → dispose → reopen → verify absence)
    ///   4. Range scan correctness after restart
    ///   5. WAL recovery (simulate WAL-only state: written to WAL but not applied to data file)
    ///   6. File truncation / partial write tolerance
    ///   7. Corruption detection (CRC validation catches flipped bytes)
    /// </summary>
    public class BTreeCrashRestartTests : IDisposable
    {
        private readonly string _testDir;
        private readonly BTreeOptions _defaultOptions;

        public BTreeCrashRestartTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"acorndb_crash_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDir);
            _defaultOptions = new BTreeOptions { PageSize = 4096, MaxCachePages = 64 };
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }

        private string SubDir(string name)
        {
            var path = Path.Combine(_testDir, name);
            Directory.CreateDirectory(path);
            return path;
        }

        #region 1. Basic Restart Persistence

        [Fact]
        public void WriteN_Dispose_Reopen_ReadAllBack()
        {
            var dir = SubDir("basic_restart");
            int count = 200;

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < count; i++)
                    trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = $"v-{i}" });
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                Assert.Equal(count, trunk.Count);
                for (int i = 0; i < count; i++)
                {
                    var result = trunk.Crack($"k-{i:D5}");
                    Assert.NotNull(result);
                    Assert.Equal($"v-{i}", result!.Payload);
                }
            }
        }

        [Fact]
        public void WriteN_Dispose_Reopen_CrackAll_ReturnsSortedComplete()
        {
            var dir = SubDir("crackall_restart");
            int count = 150;

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < count; i++)
                    trunk.Stash($"doc-{i:D5}", new Nut<string> { Id = $"doc-{i:D5}", Payload = $"p-{i}" });
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                var all = trunk.CrackAll().ToList();
                Assert.Equal(count, all.Count);

                // Verify sorted order
                for (int i = 1; i < all.Count; i++)
                    Assert.True(string.Compare(all[i - 1].Id, all[i].Id, StringComparison.Ordinal) < 0);
            }
        }

        [Fact]
        public void EmptyTree_Dispose_Reopen_RemainsEmpty()
        {
            var dir = SubDir("empty_restart");

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                Assert.Equal(0, trunk.Count);
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                Assert.Equal(0, trunk.Count);
                Assert.Null(trunk.Crack("anything"));
                Assert.Empty(trunk.CrackAll());
            }
        }

        [Fact]
        public void MultipleRestartCycles_DataPersists()
        {
            var dir = SubDir("multi_restart");

            // Cycle 1: insert 50
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < 50; i++)
                    trunk.Stash($"k-{i:D3}", new Nut<string> { Id = $"k-{i:D3}", Payload = $"c1-{i}" });
            }

            // Cycle 2: insert 50 more
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                Assert.Equal(50, trunk.Count);
                for (int i = 50; i < 100; i++)
                    trunk.Stash($"k-{i:D3}", new Nut<string> { Id = $"k-{i:D3}", Payload = $"c2-{i}" });
            }

            // Cycle 3: verify all 100
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                Assert.Equal(100, trunk.Count);
                for (int i = 0; i < 100; i++)
                {
                    var result = trunk.Crack($"k-{i:D3}");
                    Assert.NotNull(result);
                }
            }
        }

        #endregion

        #region 2. Update Persistence

        [Fact]
        public void UpdateN_Dispose_Reopen_VerifyLatestValues()
        {
            var dir = SubDir("update_restart");
            int count = 100;

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                // Insert originals
                for (int i = 0; i < count; i++)
                    trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = $"orig-{i}" });

                // Update every other key
                for (int i = 0; i < count; i += 2)
                    trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = $"updated-{i}" });
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                Assert.Equal(count, trunk.Count);
                for (int i = 0; i < count; i++)
                {
                    var result = trunk.Crack($"k-{i:D5}");
                    Assert.NotNull(result);
                    if (i % 2 == 0)
                        Assert.Equal($"updated-{i}", result!.Payload);
                    else
                        Assert.Equal($"orig-{i}", result!.Payload);
                }
            }
        }

        [Fact]
        public void UpdateSameKey_ManyTimes_Dispose_Reopen_SeesLast()
        {
            var dir = SubDir("update_same_key");

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int v = 0; v < 50; v++)
                    trunk.Stash("thekey", new Nut<string> { Id = "thekey", Payload = $"version-{v}" });
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                var result = trunk.Crack("thekey");
                Assert.NotNull(result);
                Assert.Equal("version-49", result!.Payload);
                Assert.Equal(1, trunk.Count);
            }
        }

        #endregion

        #region 3. Delete Persistence

        [Fact]
        public void DeleteSubset_Dispose_Reopen_VerifyAbsence()
        {
            var dir = SubDir("delete_restart");
            int count = 200;

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < count; i++)
                    trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = $"v-{i}" });

                // Delete every 3rd key
                for (int i = 0; i < count; i += 3)
                    trunk.Toss($"k-{i:D5}");
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < count; i++)
                {
                    var result = trunk.Crack($"k-{i:D5}");
                    if (i % 3 == 0)
                        Assert.Null(result);
                    else
                    {
                        Assert.NotNull(result);
                        Assert.Equal($"v-{i}", result!.Payload);
                    }
                }
            }
        }

        [Fact]
        public void DeleteAll_Dispose_Reopen_TreeEmpty()
        {
            var dir = SubDir("delete_all_restart");
            int count = 100;

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < count; i++)
                    trunk.Stash($"k-{i:D3}", new Nut<string> { Id = $"k-{i:D3}", Payload = $"v-{i}" });

                for (int i = 0; i < count; i++)
                    trunk.Toss($"k-{i:D3}");
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                Assert.Equal(0, trunk.Count);
                Assert.Null(trunk.Crack("k-000"));
                Assert.Empty(trunk.CrackAll());
            }
        }

        [Fact]
        public void DeleteThenReinsert_Dispose_Reopen_SeesNewValues()
        {
            var dir = SubDir("delete_reinsert_restart");

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < 50; i++)
                    trunk.Stash($"k-{i:D3}", new Nut<string> { Id = $"k-{i:D3}", Payload = $"original-{i}" });

                // Delete all
                for (int i = 0; i < 50; i++)
                    trunk.Toss($"k-{i:D3}");

                // Reinsert with new values
                for (int i = 0; i < 50; i++)
                    trunk.Stash($"k-{i:D3}", new Nut<string> { Id = $"k-{i:D3}", Payload = $"reinserted-{i}" });
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                Assert.Equal(50, trunk.Count);
                for (int i = 0; i < 50; i++)
                {
                    var result = trunk.Crack($"k-{i:D3}");
                    Assert.NotNull(result);
                    Assert.Equal($"reinserted-{i}", result!.Payload);
                }
            }
        }

        #endregion

        #region 4. Range Scan After Restart

        [Fact]
        public void RangeScan_AfterRestart_ReturnsCorrectOrdering()
        {
            var dir = SubDir("rangescan_restart");

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < 100; i++)
                    trunk.Stash($"key-{i:D3}", new Nut<string> { Id = $"key-{i:D3}", Payload = $"val-{i}" });
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                var results = trunk.RangeScan("key-020", "key-040").ToList();

                Assert.Equal(21, results.Count); // 020..040 inclusive
                Assert.Equal("key-020", results.First().Id);
                Assert.Equal("key-040", results.Last().Id);

                // Verify sorted
                for (int i = 1; i < results.Count; i++)
                    Assert.True(string.Compare(results[i - 1].Id, results[i].Id, StringComparison.Ordinal) < 0);
            }
        }

        [Fact]
        public void RangeScan_AfterDeleteAndRestart_SkipsDeleted()
        {
            var dir = SubDir("rangescan_delete_restart");

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < 100; i++)
                    trunk.Stash($"key-{i:D3}", new Nut<string> { Id = $"key-{i:D3}", Payload = $"val-{i}" });

                // Delete keys 030-039
                for (int i = 30; i < 40; i++)
                    trunk.Toss($"key-{i:D3}");
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                var results = trunk.RangeScan("key-025", "key-045").ToList();

                // Should have 025-029 (5) + 040-045 (6) = 11
                Assert.Equal(11, results.Count);
                var ids = results.Select(r => r.Id).ToHashSet();
                for (int i = 30; i < 40; i++)
                    Assert.DoesNotContain($"key-{i:D3}", ids);
            }
        }

        #endregion

        #region 5. WAL Recovery

        [Fact]
        public void WAL_WithCommittedEntries_RecoveredOnReopen()
        {
            // This test verifies that the WAL recovery path works correctly.
            // The normal write path writes to both WAL and data file, so WAL recovery
            // is effectively a replay that should produce the same result.
            // We verify this by writing data, closing, and reopening — the WAL recovery
            // runs on every open (WalManager.Recover()).
            var dir = SubDir("wal_recovery");
            int count = 100;

            // Write data (WAL + data file both get updates during normal operation)
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < count; i++)
                    trunk.Stash($"w-{i:D3}", new Nut<string> { Id = $"w-{i:D3}", Payload = $"wal-{i}" });
            }

            // Reopen triggers Recover() — should see all data
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                Assert.Equal(count, trunk.Count);
                for (int i = 0; i < count; i++)
                {
                    var result = trunk.Crack($"w-{i:D3}");
                    Assert.NotNull(result);
                    Assert.Equal($"wal-{i}", result!.Payload);
                }
            }
        }

        [Fact]
        public void WAL_TruncatedUncommittedTail_DiscardedOnRecovery()
        {
            // Write a valid WAL with a committed batch, then append garbage (simulating
            // an incomplete write that never got a COMMIT record).
            var dir = SubDir("wal_truncated_tail");
            var walPath = Path.Combine(dir, "btree.wal");
            var dbPath = Path.Combine(dir, "btree.db");

            // Phase 1: Write committed data
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < 20; i++)
                    trunk.Stash($"ok-{i:D3}", new Nut<string> { Id = $"ok-{i:D3}", Payload = $"val-{i}" });
            }

            // Phase 2: Append junk to the WAL file (simulating an incomplete page record)
            // The WAL is truncated to 0 after recovery, so we need to write fresh junk.
            // Write a partial PAGE record: type byte + partial page ID
            using (var walFs = new FileStream(walPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                walFs.Seek(0, SeekOrigin.End);
                // Write a PAGE record type byte followed by incomplete data
                walFs.WriteByte(0x01); // RECORD_TYPE_PAGE
                walFs.Write(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }); // partial pageId
                walFs.Flush();
            }

            // Phase 3: Reopen — should recover committed data, discard the junk tail
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                Assert.Equal(20, trunk.Count);
                for (int i = 0; i < 20; i++)
                {
                    var result = trunk.Crack($"ok-{i:D3}");
                    Assert.NotNull(result);
                    Assert.Equal($"val-{i}", result!.Payload);
                }
            }
        }

        [Fact]
        public void WAL_CorruptedCRC_StopsReplayAtCorruption()
        {
            // Write committed data, close. Then write a full-sized but CRC-corrupted
            // PAGE record to the WAL. Recovery should stop at the corrupted record,
            // but the already-applied committed data from the data file is still intact.
            var dir = SubDir("wal_corrupt_crc");
            var walPath = Path.Combine(dir, "btree.wal");
            int pageSize = _defaultOptions.PageSize;

            // Phase 1: Commit valid data
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < 30; i++)
                    trunk.Stash($"valid-{i:D3}", new Nut<string> { Id = $"valid-{i:D3}", Payload = $"ok-{i}" });
            }

            // Phase 2: Append a full PAGE record with bad CRC to the WAL
            int recordSize = 1 + 8 + 4 + pageSize + 4; // type + pageId + dataLen + data + crc
            var badRecord = new byte[recordSize];
            badRecord[0] = 0x01; // RECORD_TYPE_PAGE
            BinaryPrimitives.WriteInt64LittleEndian(badRecord.AsSpan(1), 999); // fake page ID
            BinaryPrimitives.WriteInt32LittleEndian(badRecord.AsSpan(9), pageSize);
            // Leave page data as zeros
            // Write an intentionally wrong CRC
            BinaryPrimitives.WriteUInt32LittleEndian(badRecord.AsSpan(recordSize - 4), 0xDEADBEEF);

            using (var walFs = new FileStream(walPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                walFs.Seek(0, SeekOrigin.End);
                walFs.Write(badRecord);
                walFs.Flush();
            }

            // Phase 3: Reopen — WAL recovery stops at bad CRC, committed data is intact
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                Assert.Equal(30, trunk.Count);
                for (int i = 0; i < 30; i++)
                {
                    var result = trunk.Crack($"valid-{i:D3}");
                    Assert.NotNull(result);
                    Assert.Equal($"ok-{i}", result!.Payload);
                }
            }
        }

        #endregion

        #region 6. File Truncation / Partial Write

        [Fact]
        public void DataFile_TruncatedToSuperblockOnly_FailsOnRead()
        {
            // Write data to create a multi-page file, then truncate the file
            // to only the superblock page. The superblock still references a root page
            // that no longer exists, so reads should fail with a clear error.
            var dir = SubDir("truncated_data");
            var dbPath = Path.Combine(dir, "btree.db");
            int pageSize = _defaultOptions.PageSize;

            // Write enough data to create multiple pages
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < 100; i++)
                    trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = new string('x', 200) });
            }

            long originalLength = new FileInfo(dbPath).Length;
            Assert.True(originalLength > pageSize * 2, "File should have multiple pages");

            // Clear the WAL so recovery doesn't recreate missing pages
            var walPath = Path.Combine(dir, "btree.wal");
            File.WriteAllBytes(walPath, Array.Empty<byte>());

            // Truncate to just the superblock (1 page). The root page is now gone.
            using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Write))
            {
                fs.SetLength(pageSize);
            }

            // Reopen — superblock is intact but references a root page beyond file bounds.
            // The open should succeed (superblock validates) but any read that follows
            // the root pointer will fail because the page is out of range.
            bool gotError = false;
            try
            {
                using var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions);
                trunk.CrackAll().ToList();
            }
            catch (Exception ex) when (
                ex is InvalidDataException ||
                ex is ArgumentOutOfRangeException ||
                ex is IOException)
            {
                gotError = true;
            }

            Assert.True(gotError, "Truncated data file should produce a clear error on read");
        }

        [Fact]
        public void Superblock_Truncated_FailsWithClearError()
        {
            // Create a valid data file, then truncate it so the superblock is incomplete.
            var dir = SubDir("truncated_superblock");
            var dbPath = Path.Combine(dir, "btree.db");

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                trunk.Stash("k1", new Nut<string> { Id = "k1", Payload = "v1" });
            }

            // Truncate to less than one page (superblock is page 0, must be pageSize bytes)
            using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Write))
            {
                fs.SetLength(20); // Much less than a full page
            }

            Assert.Throws<InvalidDataException>(() =>
            {
                using var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions);
            });
        }

        #endregion

        #region 7. Corruption Detection (CRC Validation)

        [Fact]
        public void PageCRC_FlippedByte_DetectedOnRead()
        {
            // Write data, close, find the root page from the superblock,
            // flip a byte in it, reopen — read should throw CRC mismatch.
            var dir = SubDir("page_crc_flip");
            var dbPath = Path.Combine(dir, "btree.db");
            int pageSize = _defaultOptions.PageSize;

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < 50; i++)
                    trunk.Stash($"k-{i:D3}", new Nut<string> { Id = $"k-{i:D3}", Payload = $"val-{i}" });
            }

            // Read the superblock to find the root page ID
            long rootPageId;
            using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var sbBuf = new byte[42];
                fs.Read(sbBuf, 0, 42);
                rootPageId = BinaryPrimitives.ReadInt64LittleEndian(sbBuf.AsSpan(16));
            }
            Assert.True(rootPageId > 0, "Root page should exist");

            // Clear the WAL so recovery doesn't overwrite our corruption with correct data
            var walPath = Path.Combine(dir, "btree.wal");
            File.WriteAllBytes(walPath, Array.Empty<byte>());

            // Flip a byte in the root page's record area (offset 30 within the page,
            // avoiding the CRC field at bytes 18-21)
            long rootPageOffset = rootPageId * pageSize;
            using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite))
            {
                long corruptOffset = rootPageOffset + 30;
                fs.Seek(corruptOffset, SeekOrigin.Begin);
                int original = fs.ReadByte();
                fs.Seek(corruptOffset, SeekOrigin.Begin);
                fs.WriteByte((byte)(original ^ 0xFF));
                fs.Flush();
            }

            // Reopen with CRC validation — reading the corrupted root page should throw
            var optionsWithCrc = new BTreeOptions
            {
                PageSize = pageSize,
                MaxCachePages = 64,
                ValidateChecksumsOnRead = true
            };

            var ex = Assert.Throws<InvalidDataException>(() =>
            {
                using var trunk = new BTreeTrunk<string>(customPath: dir, options: optionsWithCrc);
                // Any operation that reads the root page will trigger CRC check
                trunk.CrackAll().ToList();
            });

            Assert.Contains("CRC mismatch", ex.Message);
        }

        [Fact]
        public void SuperblockCRC_FlippedByte_DetectedOnOpen()
        {
            // Write data, close, flip a byte in the superblock, reopen should throw.
            var dir = SubDir("superblock_crc_flip");
            var dbPath = Path.Combine(dir, "btree.db");

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                trunk.Stash("k1", new Nut<string> { Id = "k1", Payload = "v1" });
            }

            // Flip a byte in the superblock's RootPageId field (offset 16)
            using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Seek(16, SeekOrigin.Begin);
                int original = fs.ReadByte();
                fs.Seek(16, SeekOrigin.Begin);
                fs.WriteByte((byte)(original ^ 0x01)); // flip LSB
                fs.Flush();
            }

            var ex = Assert.Throws<InvalidDataException>(() =>
            {
                using var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions);
            });

            Assert.Contains("CRC mismatch", ex.Message);
        }

        [Fact]
        public void BadMagicNumber_DetectedOnOpen()
        {
            var dir = SubDir("bad_magic");
            var dbPath = Path.Combine(dir, "btree.db");

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                trunk.Stash("k1", new Nut<string> { Id = "k1", Payload = "v1" });
            }

            // Overwrite magic number at offset 0
            using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Seek(0, SeekOrigin.Begin);
                fs.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
                fs.Flush();
            }

            var ex = Assert.Throws<InvalidDataException>(() =>
            {
                using var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions);
            });

            Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BadFormatVersion_DetectedOnOpen()
        {
            var dir = SubDir("bad_version");
            var dbPath = Path.Combine(dir, "btree.db");

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                trunk.Stash("k1", new Nut<string> { Id = "k1", Payload = "v1" });
            }

            // Overwrite format version at offset 4 with a high version, and fix the CRC
            using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite))
            {
                var superblock = new byte[_defaultOptions.PageSize];
                fs.Read(superblock, 0, superblock.Length);

                // Set format version to 255 (unsupported)
                BinaryPrimitives.WriteUInt16LittleEndian(superblock.AsSpan(4), 255);

                // Recompute superblock CRC so the CRC check passes but the version check fails
                uint crc = Crc32.Compute(superblock.AsSpan(0, 38));
                BinaryPrimitives.WriteUInt32LittleEndian(superblock.AsSpan(38), crc);

                fs.Seek(0, SeekOrigin.Begin);
                fs.Write(superblock, 0, superblock.Length);
                fs.Flush();
            }

            var ex = Assert.Throws<InvalidDataException>(() =>
            {
                using var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions);
            });

            Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PageSizeMismatch_DetectedOnOpen()
        {
            var dir = SubDir("page_size_mismatch");
            var dbPath = Path.Combine(dir, "btree.db");

            // Create with 4096 page size
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                trunk.Stash("k1", new Nut<string> { Id = "k1", Payload = "v1" });
            }

            // Try to reopen with 8192 page size
            var wrongOptions = new BTreeOptions { PageSize = 8192, MaxCachePages = 64 };

            var ex = Assert.Throws<InvalidDataException>(() =>
            {
                using var trunk = new BTreeTrunk<string>(customPath: dir, options: wrongOptions);
            });

            Assert.Contains("Page size mismatch", ex.Message);
        }

        [Fact]
        public void PageCRC_ValidAfterNormalOperations()
        {
            // Verify that normal write/read cycles never trip CRC validation.
            // This is a sanity check that CRC computation is consistent.
            var dir = SubDir("crc_sanity");
            var options = new BTreeOptions
            {
                PageSize = 4096,
                MaxCachePages = 64,
                ValidateChecksumsOnRead = true
            };

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: options))
            {
                for (int i = 0; i < 200; i++)
                    trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = new string('z', 100) });

                // Update some
                for (int i = 0; i < 100; i += 5)
                    trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = "updated" });

                // Delete some
                for (int i = 1; i < 100; i += 7)
                    trunk.Toss($"k-{i:D5}");

                // Read all — should not throw CRC errors
                var all = trunk.CrackAll().ToList();
                Assert.True(all.Count > 0);
            }

            // Reopen with CRC validation — still no errors
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: options))
            {
                var all = trunk.CrackAll().ToList();
                Assert.True(all.Count > 0);

                // Verify specific reads work
                Assert.Equal("updated", trunk.Crack("k-00000")!.Payload);
            }
        }

        [Fact]
        public void MultiplePagesCorrupted_CorruptionDetected()
        {
            // Corrupt all data pages (1 through N) and verify corruption is caught.
            var dir = SubDir("multi_corrupt");
            var dbPath = Path.Combine(dir, "btree.db");
            int pageSize = _defaultOptions.PageSize;

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: _defaultOptions))
            {
                for (int i = 0; i < 200; i++)
                    trunk.Stash($"k-{i:D5}", new Nut<string> { Id = $"k-{i:D5}", Payload = new string('a', 150) });
            }

            // Clear the WAL so recovery doesn't overwrite our corruption
            var walPath = Path.Combine(dir, "btree.wal");
            File.WriteAllBytes(walPath, Array.Empty<byte>());

            // Determine how many pages exist
            long fileLen = new FileInfo(dbPath).Length;
            long pageCount = fileLen / pageSize;

            // Corrupt every data page (skip page 0 = superblock)
            using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite))
            {
                for (long pg = 1; pg < pageCount; pg++)
                {
                    // Corrupt byte 50 of each page (in the record/slot area, not the CRC field)
                    long offset = pg * pageSize + 50;
                    fs.Seek(offset, SeekOrigin.Begin);
                    int b = fs.ReadByte();
                    fs.Seek(offset, SeekOrigin.Begin);
                    fs.WriteByte((byte)(b ^ 0xFF));
                }
                fs.Flush();
            }

            Assert.Throws<InvalidDataException>(() =>
            {
                using var trunk = new BTreeTrunk<string>(customPath: dir, options: new BTreeOptions
                {
                    PageSize = pageSize,
                    MaxCachePages = 64,
                    ValidateChecksumsOnRead = true
                });
                trunk.CrackAll().ToList();
            });
        }

        #endregion

        #region 8. Multi-Level Tree Restart

        [Fact]
        public void MultiLevelTree_Dispose_Reopen_AllKeysAccessible()
        {
            // Small page size forces deep trees with multiple internal levels
            var dir = SubDir("multilevel_restart");
            var options = new BTreeOptions { PageSize = 4096, MaxCachePages = 32 };
            int count = 500;

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: options))
            {
                for (int i = 0; i < count; i++)
                    trunk.Stash($"ml-{i:D5}", new Nut<string> { Id = $"ml-{i:D5}", Payload = new string('x', 80 + (i % 40)) });
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: options))
            {
                Assert.Equal(count, trunk.Count);

                // Random-order reads
                var rng = new Random(42);
                var indices = Enumerable.Range(0, count).OrderBy(_ => rng.Next()).ToList();
                foreach (var i in indices)
                {
                    var result = trunk.Crack($"ml-{i:D5}");
                    Assert.NotNull(result);
                    Assert.Equal(80 + (i % 40), result!.Payload.Length);
                }
            }
        }

        [Fact]
        public void MultiLevelTree_DeleteHalf_Restart_RemainingIntact()
        {
            var dir = SubDir("multilevel_delete_restart");
            var options = new BTreeOptions { PageSize = 4096, MaxCachePages = 32 };
            int count = 400;

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: options))
            {
                for (int i = 0; i < count; i++)
                    trunk.Stash($"ml-{i:D5}", new Nut<string> { Id = $"ml-{i:D5}", Payload = $"val-{i}" });

                // Delete even keys
                for (int i = 0; i < count; i += 2)
                    trunk.Toss($"ml-{i:D5}");
            }

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: options))
            {
                Assert.Equal(count / 2, trunk.Count);

                for (int i = 0; i < count; i++)
                {
                    var result = trunk.Crack($"ml-{i:D5}");
                    if (i % 2 == 0)
                        Assert.Null(result);
                    else
                    {
                        Assert.NotNull(result);
                        Assert.Equal($"val-{i}", result!.Payload);
                    }
                }
            }
        }

        #endregion

        #region 9. FsyncOnCommit Configuration

        [Fact]
        public void FsyncOnCommit_False_InsertReadDeleteWork()
        {
            // Verifies that FsyncOnCommit=false is wired through correctly:
            // writes, reads, updates, deletes, and persistence across dispose/reopen all work.
            var dir = SubDir("fsync_off");
            var options = new BTreeOptions
            {
                PageSize = 4096,
                MaxCachePages = 64,
                FsyncOnCommit = false
            };

            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: options))
            {
                for (int i = 0; i < 100; i++)
                    trunk.Stash($"k-{i:D3}", new Nut<string> { Id = $"k-{i:D3}", Payload = $"v-{i}" });

                Assert.Equal(100, trunk.Count);

                // Update
                trunk.Stash("k-050", new Nut<string> { Id = "k-050", Payload = "updated" });
                Assert.Equal("updated", trunk.Crack("k-050")!.Payload);

                // Delete
                trunk.Toss("k-099");
                Assert.Null(trunk.Crack("k-099"));
                Assert.Equal(99, trunk.Count);
            }

            // Reopen with same option — data persisted (OS may have flushed buffers on close)
            using (var trunk = new BTreeTrunk<string>(customPath: dir, options: options))
            {
                Assert.Equal(99, trunk.Count);
                Assert.Equal("updated", trunk.Crack("k-050")!.Payload);
                Assert.Null(trunk.Crack("k-099"));
            }
        }

        #endregion
    }
}
