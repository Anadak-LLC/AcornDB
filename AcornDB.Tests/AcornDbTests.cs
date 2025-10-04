using System;
using System.IO;
using Xunit;
using AcornDB;
using AcornDB.Serialization;

namespace AcornDB.Tests
{
    public class AcornDbTests
    {
        private const string TestFile = "test-db";

        public AcornDbTests()
        {
            CleanupTestFiles();
        }

        private void CleanupTestFiles()
        {
            foreach (var file in Directory.GetFiles(Directory.GetCurrentDirectory(), $"{TestFile}.*.json"))
            {
                File.Delete(file);
            }
        }

        public class User
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        [Fact]
        public void CanInsertAndRetrieveDocument()
        {
            var db = new AcornDb(TestFile);
            var users = db.GetCollection<User>("users");

            var user = new User { Id = 1, Name = "Alice" };
            users.Insert(user);

            var retrieved = users.Get(1);
            Assert.Equal("Alice", retrieved.Name);
        }

        [Fact]
        public void CanUpdateDocument()
        {
            var db = new AcornDb(TestFile);
            var users = db.GetCollection<User>("users");

            users.Insert(new User { Id = 1, Name = "Bob" });
            users.Update(1, u => u.Name = "Bobby");

            var updated = users.Get(1);
            Assert.Equal("Bobby", updated.Name);
        }

        [Fact]
        public void CanTriggerOnChangedEvent()
        {
            var db = new AcornDb(TestFile);
            var users = db.GetCollection<User>("users");

            bool eventFired = false;

            users.OnChanged(u =>
            {
                if (u.Id == 1 && u.Name == "Charlie")
                    eventFired = true;
            });

            users.Insert(new User { Id = 1, Name = "Charlie" });

            Assert.True(eventFired);
        }
    }
}
