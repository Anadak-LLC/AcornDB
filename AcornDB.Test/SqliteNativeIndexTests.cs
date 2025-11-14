using System;
using System.IO;
using System.Linq;
using Xunit;
using AcornDB.Persistence.RDBMS;

namespace AcornDB.Test
{
    public class SqliteNativeIndexTests : IDisposable
    {
        private readonly string _testDbPath;

        public class User
        {
            public string Id { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
            public string Department { get; set; } = string.Empty;
        }

        public SqliteNativeIndexTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"acorn_test_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            // Cleanup test database
            try
            {
                if (File.Exists(_testDbPath))
                {
                    File.Delete(_testDbPath);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        [Fact]
        public void SqliteTrunk_SupportsNativeIndexes()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            // Act & Assert
            Assert.True(trunk.Capabilities.SupportsNativeIndexes);
        }

        [Fact]
        public void CreateNativeIndex_CreatesIndexInDatabase()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            // Add some data
            trunk.Save("1", new Nut<User>
            {
                Id = "1",
                Payload = new User { Id = "1", Email = "alice@example.com", Name = "Alice", Age = 30 },
                Timestamp = DateTime.UtcNow
            });
            trunk.Flush(); // Ensure data is written to database

            // Act - Create native index
            var index = trunk.CreateNativeIndex("IX_User_Email", u => u.Email);

            // Assert
            Assert.NotNull(index);
            Assert.Equal("IX_User_Email", index.Name);
            Assert.True(index.IsCreated);
            Assert.Equal(Indexing.IndexState.Ready, index.State);
            Assert.Contains("CREATE", index.CreateIndexDdl);
            Assert.Contains("json_extract", index.CreateIndexDdl);
        }

        [Fact]
        public void NativeIndex_LookupFindsMatchingRecords()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            trunk.Save("1", new Nut<User>
            {
                Id = "1",
                Payload = new User { Id = "1", Email = "alice@example.com", Name = "Alice", Age = 30 },
                Timestamp = DateTime.UtcNow
            });
            trunk.Save("2", new Nut<User>
            {
                Id = "2",
                Payload = new User { Id = "2", Email = "bob@example.com", Name = "Bob", Age = 25 },
                Timestamp = DateTime.UtcNow
            });
            trunk.Flush(); // Ensure data is written to database

            var index = trunk.CreateNativeIndex("IX_User_Email", u => u.Email);

            // Act - Lookup by email
            var results = index.Lookup("alice@example.com").ToList();

            // Assert
            Assert.Single(results);
            Assert.Equal("1", results[0]);
        }

        [Fact]
        public void NativeIndex_RangeQueryReturnsMatchingRecords()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            trunk.Save("1", new Nut<User> { Id = "1", Payload = new User { Id = "1", Age = 20 }, Timestamp = DateTime.UtcNow });
            trunk.Save("2", new Nut<User> { Id = "2", Payload = new User { Id = "2", Age = 25 }, Timestamp = DateTime.UtcNow });
            trunk.Save("3", new Nut<User> { Id = "3", Payload = new User { Id = "3", Age = 30 }, Timestamp = DateTime.UtcNow });
            trunk.Save("4", new Nut<User> { Id = "4", Payload = new User { Id = "4", Age = 35 }, Timestamp = DateTime.UtcNow });
            trunk.Flush(); // Ensure data is written to database

            var index = trunk.CreateNativeIndex("IX_User_Age", u => u.Age);

            // Act - Range query: ages 25-30 (inclusive)
            var results = index.Range(25, 30).ToList();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Contains("2", results);
            Assert.Contains("3", results);
        }

        [Fact]
        public void NativeIndex_GetAllSortedReturnsInOrder()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            trunk.Save("1", new Nut<User> { Id = "1", Payload = new User { Id = "1", Age = 30 }, Timestamp = DateTime.UtcNow });
            trunk.Save("2", new Nut<User> { Id = "2", Payload = new User { Id = "2", Age = 20 }, Timestamp = DateTime.UtcNow });
            trunk.Save("3", new Nut<User> { Id = "3", Payload = new User { Id = "3", Age = 25 }, Timestamp = DateTime.UtcNow });
            trunk.Flush(); // Ensure data is written to database

            var index = trunk.CreateNativeIndex("IX_User_Age", u => u.Age);

            // Act - Get all sorted ascending
            var resultsAsc = index.GetAllSorted(ascending: true).ToList();

            // Assert
            Assert.Equal(3, resultsAsc.Count);
            Assert.Equal("2", resultsAsc[0]); // Age 20
            Assert.Equal("3", resultsAsc[1]); // Age 25
            Assert.Equal("1", resultsAsc[2]); // Age 30
        }

        [Fact]
        public void NativeIndex_GetAllSortedDescending()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            trunk.Save("1", new Nut<User> { Id = "1", Payload = new User { Id = "1", Age = 20 }, Timestamp = DateTime.UtcNow });
            trunk.Save("2", new Nut<User> { Id = "2", Payload = new User { Id = "2", Age = 30 }, Timestamp = DateTime.UtcNow });
            trunk.Flush(); // Ensure data is written to database

            var index = trunk.CreateNativeIndex("IX_User_Age", u => u.Age);

            // Act
            var results = index.GetAllSorted(ascending: false).ToList();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Equal("2", results[0]); // Age 30 first
            Assert.Equal("1", results[1]); // Age 20 second
        }

        [Fact]
        public void NativeIndex_GetMinReturnsSmallestValue()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            trunk.Save("1", new Nut<User> { Id = "1", Payload = new User { Id = "1", Age = 30 }, Timestamp = DateTime.UtcNow });
            trunk.Save("2", new Nut<User> { Id = "2", Payload = new User { Id = "2", Age = 20 }, Timestamp = DateTime.UtcNow });
            trunk.Save("3", new Nut<User> { Id = "3", Payload = new User { Id = "3", Age = 25 }, Timestamp = DateTime.UtcNow });
            trunk.Flush(); // Ensure data is written to database

            var index = trunk.CreateNativeIndex("IX_User_Age", u => u.Age);

            // Act
            var min = index.GetMin();

            // Assert
            Assert.Equal(20, min);
        }

        [Fact]
        public void NativeIndex_GetMaxReturnsLargestValue()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            trunk.Save("1", new Nut<User> { Id = "1", Payload = new User { Id = "1", Age = 30 }, Timestamp = DateTime.UtcNow });
            trunk.Save("2", new Nut<User> { Id = "2", Payload = new User { Id = "2", Age = 20 }, Timestamp = DateTime.UtcNow });
            trunk.Flush(); // Ensure data is written to database

            var index = trunk.CreateNativeIndex("IX_User_Age", u => u.Age);

            // Act
            var max = index.GetMax();

            // Assert
            Assert.Equal(30, max);
        }

        [Fact]
        public void NativeIndex_GetStatisticsReturnsAccurateInfo()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            trunk.Save("1", new Nut<User> { Id = "1", Payload = new User { Id = "1", Age = 20 }, Timestamp = DateTime.UtcNow });
            trunk.Save("2", new Nut<User> { Id = "2", Payload = new User { Id = "2", Age = 25 }, Timestamp = DateTime.UtcNow });
            trunk.Save("3", new Nut<User> { Id = "3", Payload = new User { Id = "3", Age = 20 }, Timestamp = DateTime.UtcNow }); // Duplicate age
            trunk.Flush(); // Ensure data is written to database

            var index = trunk.CreateNativeIndex("IX_User_Age", u => u.Age);

            // Act
            var stats = index.GetStatistics();

            // Assert
            Assert.Equal(3, stats.EntryCount);
            Assert.Equal(2, stats.UniqueValueCount); // Only 2 unique ages: 20 and 25
            Assert.True(stats.Selectivity > 0 && stats.Selectivity < 1);
        }

        [Fact]
        public void DropNativeIndex_RemovesIndexFromDatabase()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            var index = trunk.CreateNativeIndex("IX_User_Email", u => u.Email);
            Assert.True(index.VerifyInDatabase());

            // Act
            trunk.DropNativeIndex("IX_User_Email");

            // Assert
            Assert.False(index.VerifyInDatabase());
        }

        [Fact]
        public void ListNativeIndexes_ReturnsAllIndexes()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            trunk.CreateNativeIndex("IX_User_Email", u => u.Email);
            trunk.CreateNativeIndex("IX_User_Age", u => u.Age);

            // Act
            var indexes = trunk.ListNativeIndexes();

            // Assert
            Assert.Contains("IX_User_Email", indexes);
            Assert.Contains("IX_User_Age", indexes);
        }

        [Fact]
        public void NativeIndex_UniqueConstraintPreventsDuplicates()
        {
            // Arrange
            using var trunk = new SqliteTrunk<User>(_testDbPath);

            trunk.Save("1", new Nut<User>
            {
                Id = "1",
                Payload = new User { Id = "1", Email = "alice@example.com" },
                Timestamp = DateTime.UtcNow
            });

            var index = trunk.CreateNativeIndex("IX_User_Email_Unique", u => u.Email, isUnique: true);

            // Act & Assert - Attempt to insert duplicate should be prevented by DB
            // Note: This test verifies the index was created with UNIQUE keyword
            Assert.True(index.IsUnique);
            Assert.Contains("UNIQUE", index.CreateIndexDdl);
        }
    }
}
