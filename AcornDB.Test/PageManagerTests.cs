using System;
using System.Buffers.Binary;
using System.IO;
using AcornDB.Storage.BPlusTree;
using Xunit;

namespace AcornDB.Test
{
    public class PageManagerTests : IDisposable
    {
        private readonly string _testDir;
        private const int PAGE_SIZE = 4096; // Smaller pages for faster tests

        public PageManagerTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), $"acorndb_pm_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        private string TestFilePath(string name = "test.db") => Path.Combine(_testDir, name);

        #region Initialization & Superblock

        [Fact]
        public void NewFile_CreatesSuperblock_WithCorrectMagicAndVersion()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            var (rootPageId, generation, entryCount) = pm.ReadSuperblock();

            Assert.Equal(0, rootPageId);
            Assert.Equal(0, generation);
            Assert.Equal(0, entryCount);
            Assert.Equal(PAGE_SIZE, pm.PageSize);

            // File should be exactly one page (the superblock)
            Assert.Equal(PAGE_SIZE, new FileInfo(path).Length);
        }

        [Fact]
        public void NewFile_PageCount_IsOne()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            Assert.Equal(1, pm.PageCount);
        }

        [Fact]
        public void ReopenExistingFile_ValidatesMagicAndVersion()
        {
            var path = TestFilePath();

            // Create
            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                pm.WriteSuperblock(42, 7, 10);
            }

            // Reopen
            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                var (rootPageId, generation, entryCount) = pm.ReadSuperblock();
                Assert.Equal(42, rootPageId);
                Assert.Equal(7, generation);
                Assert.Equal(10, entryCount);
            }
        }

        [Fact]
        public void ReopenExistingFile_ValidatesSuperblockCrc()
        {
            var path = TestFilePath();

            // Create valid file
            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                pm.WriteSuperblock(1, 1, 0);
            }

            // Corrupt the RootPageId field (byte 16) in the superblock
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Position = 16;
                fs.WriteByte(0xFF);
            }

            // Should throw on reopen because CRC no longer matches
            var ex = Assert.Throws<InvalidDataException>(() =>
                new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false));
            Assert.Contains("CRC mismatch", ex.Message);
        }

        [Fact]
        public void ReopenExistingFile_RejectsBadMagic()
        {
            var path = TestFilePath();

            // Create valid file
            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false)) { }

            // Corrupt magic number
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Position = 0;
                fs.Write(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            }

            var ex = Assert.Throws<InvalidDataException>(() =>
                new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false));
            Assert.Contains("bad magic", ex.Message);
        }

        [Fact]
        public void ReopenExistingFile_RejectsPageSizeMismatch()
        {
            var path = TestFilePath();

            // Create with 8192 so the file is large enough for a 4096-page-size reopen
            using (var pm = new PageManager(path, 8192, validateChecksumsOnRead: false)) { }

            // Reopen with 4096 — superblock has PageSize=8192, we pass 4096
            var ex = Assert.Throws<InvalidDataException>(() =>
                new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false));
            Assert.Contains("Page size mismatch", ex.Message);
        }

        [Fact]
        public void ReopenExistingFile_RejectsTruncatedFile()
        {
            var path = TestFilePath();

            // Create valid file
            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false)) { }

            // Truncate to less than one page
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.SetLength(10);
            }

            var ex = Assert.Throws<InvalidDataException>(() =>
                new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false));
            Assert.Contains("File too small", ex.Message);
        }

        #endregion

        #region Page Allocation

        [Fact]
        public void AllocatePage_ReturnsSequentialIds_StartingAtOne()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            long p1 = pm.AllocatePage();
            long p2 = pm.AllocatePage();
            long p3 = pm.AllocatePage();

            Assert.Equal(1, p1);
            Assert.Equal(2, p2);
            Assert.Equal(3, p3);
        }

        [Fact]
        public void AllocatePage_ExtendsFile()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            Assert.Equal(PAGE_SIZE, new FileInfo(path).Length);

            pm.AllocatePage(); // page 1
            Assert.Equal(2 * PAGE_SIZE, new FileInfo(path).Length);

            pm.AllocatePage(); // page 2
            Assert.Equal(3 * PAGE_SIZE, new FileInfo(path).Length);
        }

        [Fact]
        public void AllocatePage_UpdatesPageCount()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            Assert.Equal(1, pm.PageCount);

            pm.AllocatePage();
            Assert.Equal(2, pm.PageCount);

            pm.AllocatePage();
            pm.AllocatePage();
            Assert.Equal(4, pm.PageCount);
        }

        [Fact]
        public void AllocatePage_PreservesCountAcrossReopen()
        {
            var path = TestFilePath();

            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                pm.AllocatePage();
                pm.AllocatePage();
                pm.AllocatePage();
            }

            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                Assert.Equal(4, pm.PageCount); // superblock + 3 pages
                long p4 = pm.AllocatePage();
                Assert.Equal(4, p4);
            }
        }

        #endregion

        #region Page Read/Write

        [Fact]
        public void WritePage_ThenReadPage_RoundTrips()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            long pageId = pm.AllocatePage();

            // Write a page with a known pattern
            var writeBuf = new byte[PAGE_SIZE];
            for (int i = 0; i < PAGE_SIZE; i++)
                writeBuf[i] = (byte)(i % 256);

            pm.WritePage(pageId, writeBuf);

            // Read it back
            var readBuf = new byte[PAGE_SIZE];
            pm.ReadPage(pageId, readBuf);

            Assert.Equal(writeBuf, readBuf);
        }

        [Fact]
        public void WritePage_MultiplePages_Independent()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            long p1 = pm.AllocatePage();
            long p2 = pm.AllocatePage();

            var buf1 = new byte[PAGE_SIZE];
            var buf2 = new byte[PAGE_SIZE];
            buf1[0] = 0xAA;
            buf2[0] = 0xBB;

            pm.WritePage(p1, buf1);
            pm.WritePage(p2, buf2);

            var readBuf1 = new byte[PAGE_SIZE];
            var readBuf2 = new byte[PAGE_SIZE];
            pm.ReadPage(p1, readBuf1);
            pm.ReadPage(p2, readBuf2);

            Assert.Equal(0xAA, readBuf1[0]);
            Assert.Equal(0xBB, readBuf2[0]);
        }

        [Fact]
        public void WritePage_PersistsAcrossReopen()
        {
            var path = TestFilePath();
            long pageId;

            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                pageId = pm.AllocatePage();
                var writeBuf = new byte[PAGE_SIZE];
                writeBuf[0] = 0x42;
                writeBuf[PAGE_SIZE - 1] = 0x99;
                pm.WritePage(pageId, writeBuf);
                pm.Flush();
            }

            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                var readBuf = new byte[PAGE_SIZE];
                pm.ReadPage(pageId, readBuf);
                Assert.Equal(0x42, readBuf[0]);
                Assert.Equal(0x99, readBuf[PAGE_SIZE - 1]);
            }
        }

        #endregion

        #region Bounds Checking

        [Fact]
        public void ReadPage_RejectsPageIdZero()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            var buf = new byte[PAGE_SIZE];
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => pm.ReadPage(0, buf));
            Assert.Contains("superblock", ex.Message);
        }

        [Fact]
        public void ReadPage_RejectsNegativePageId()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            var buf = new byte[PAGE_SIZE];
            Assert.Throws<ArgumentOutOfRangeException>(() => pm.ReadPage(-1, buf));
        }

        [Fact]
        public void ReadPage_RejectsUnallocatedPageId()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            // Only superblock exists (pageCount = 1), no data pages allocated
            var buf = new byte[PAGE_SIZE];
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => pm.ReadPage(1, buf));
            Assert.Contains("beyond allocated range", ex.Message);
        }

        [Fact]
        public void ReadPage_RejectsBufferTooSmall()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            pm.AllocatePage();

            var buf = new byte[PAGE_SIZE - 1];
            Assert.Throws<ArgumentException>(() => pm.ReadPage(1, buf));
        }

        [Fact]
        public void WritePage_RejectsPageIdZero()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            var buf = new byte[PAGE_SIZE];
            Assert.Throws<ArgumentOutOfRangeException>(() => pm.WritePage(0, buf));
        }

        [Fact]
        public void WritePage_RejectsBufferTooSmall()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            pm.AllocatePage();

            var buf = new byte[PAGE_SIZE - 1];
            Assert.Throws<ArgumentException>(() => pm.WritePage(1, buf));
        }

        #endregion

        #region CRC Validation on Read

        [Fact]
        public void ReadPage_WithCrcEnabled_AcceptsValidPage()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: true);

            long pageId = pm.AllocatePage();

            // Write a page with valid CRC (simulate what BPlusTreeNavigator does)
            var writeBuf = new byte[PAGE_SIZE];
            writeBuf[0] = 0x02; // PageType = Leaf
            writeBuf[1] = 0x00; // Level = 0
            BinaryPrimitives.WriteUInt16LittleEndian(writeBuf.AsSpan(4), 0); // ItemCount = 0
            BinaryPrimitives.WriteUInt16LittleEndian(writeBuf.AsSpan(6), 22); // FreeSpaceStart
            BinaryPrimitives.WriteUInt16LittleEndian(writeBuf.AsSpan(8), (ushort)PAGE_SIZE); // FreeSpaceEnd

            // Compute and write CRC (excluding [18..22))
            uint crc = Crc32.ComputeExcluding(writeBuf.AsSpan(0, PAGE_SIZE), 18, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(writeBuf.AsSpan(18), crc);

            pm.WritePage(pageId, writeBuf);

            // Read should succeed
            var readBuf = new byte[PAGE_SIZE];
            pm.ReadPage(pageId, readBuf);
            Assert.Equal(writeBuf, readBuf);
        }

        [Fact]
        public void ReadPage_WithCrcEnabled_RejectsCorruptedPage()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: true);

            long pageId = pm.AllocatePage();

            // Write a page with valid CRC
            var writeBuf = new byte[PAGE_SIZE];
            writeBuf[0] = 0x02; // PageType = Leaf
            uint crc = Crc32.ComputeExcluding(writeBuf.AsSpan(0, PAGE_SIZE), 18, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(writeBuf.AsSpan(18), crc);
            pm.WritePage(pageId, writeBuf);

            // Corrupt a byte in the page on disk
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                long offset = pageId * PAGE_SIZE + 50; // corrupt byte 50
                fs.Position = offset;
                fs.WriteByte(0xFF);
            }

            // Read should throw
            var readBuf = new byte[PAGE_SIZE];
            var ex = Assert.Throws<InvalidDataException>(() => pm.ReadPage(pageId, readBuf));
            Assert.Contains("CRC mismatch", ex.Message);
        }

        [Fact]
        public void ReadPage_WithCrcDisabled_AcceptsCorruptedPage()
        {
            var path = TestFilePath();

            long pageId;
            // Write with CRC disabled
            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                pageId = pm.AllocatePage();
                var writeBuf = new byte[PAGE_SIZE];
                writeBuf[0] = 0x02;
                pm.WritePage(pageId, writeBuf);
            }

            // Corrupt byte
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                long offset = pageId * PAGE_SIZE + 50;
                fs.Position = offset;
                fs.WriteByte(0xFF);
            }

            // Read with CRC disabled should succeed
            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                var readBuf = new byte[PAGE_SIZE];
                pm.ReadPage(pageId, readBuf); // Should not throw
                Assert.Equal(0xFF, readBuf[50]);
            }
        }

        #endregion

        #region Superblock Read/Write

        [Fact]
        public void WriteSuperblock_ThenReadSuperblock_RoundTrips()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            pm.WriteSuperblock(123, 456, 789);

            var (rootPageId, generation, entryCount) = pm.ReadSuperblock();
            Assert.Equal(123, rootPageId);
            Assert.Equal(456, generation);
            Assert.Equal(789, entryCount);
        }

        [Fact]
        public void WriteSuperblock_PersistsAcrossReopen()
        {
            var path = TestFilePath();

            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                pm.WriteSuperblock(999, 42, 55);
            }

            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                var (rootPageId, generation, entryCount) = pm.ReadSuperblock();
                Assert.Equal(999, rootPageId);
                Assert.Equal(42, generation);
                Assert.Equal(55, entryCount);
            }
        }

        [Fact]
        public void WriteSuperblock_UpdatesCrc_SoReopenSucceeds()
        {
            var path = TestFilePath();

            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                // Multiple superblock updates should all maintain valid CRC
                pm.WriteSuperblock(1, 1, 10);
                pm.WriteSuperblock(2, 2, 20);
                pm.WriteSuperblock(100, 50, 30);
            }

            // Reopen validates CRC — should succeed
            using (var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false))
            {
                var (rootPageId, generation, entryCount) = pm.ReadSuperblock();
                Assert.Equal(100, rootPageId);
                Assert.Equal(50, generation);
                Assert.Equal(30, entryCount);
            }
        }

        #endregion

        #region WritePage Updates NextPageId (WAL Recovery Scenario)

        [Fact]
        public void WritePage_BeyondAllocation_ExtendsFileAndUpdatesPageCount()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            // Only superblock exists (pageCount = 1)
            Assert.Equal(1, pm.PageCount);

            // Write directly to page 5 (simulating WAL recovery)
            var buf = new byte[PAGE_SIZE];
            buf[0] = 0xAB;
            pm.WritePage(5, buf);

            // PageCount should be updated to at least 6
            Assert.True(pm.PageCount >= 6);

            // File should be extended
            Assert.True(new FileInfo(path).Length >= 6 * PAGE_SIZE);

            // Should be readable now
            var readBuf = new byte[PAGE_SIZE];
            pm.ReadPage(5, readBuf);
            Assert.Equal(0xAB, readBuf[0]);
        }

        #endregion

        #region Flush

        [Fact]
        public void Flush_DoesNotThrow()
        {
            var path = TestFilePath();
            using var pm = new PageManager(path, PAGE_SIZE, validateChecksumsOnRead: false);

            pm.AllocatePage();
            pm.Flush(); // Should not throw
        }

        #endregion
    }
}
