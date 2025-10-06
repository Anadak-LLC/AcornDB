using AcornDB.Storage;
using Xunit;

namespace AcornDB.Test
{
    public class TrunkTests
    {
        public class FileTrunkTests
        {
            [Fact]
            public void FileTrunk_Can_Save_And_Load()
            {
                var trunk = new FileTrunk<string>("data/test-file");
                var shell = new NutShell<string> { Id = "test", Payload = "value" };

                trunk.Save("test", shell);
                var loaded = trunk.Load("test");

                Assert.NotNull(loaded);
                Assert.Equal("value", loaded.Payload);
            }

            [Fact]
            public void FileTrunk_GetHistory_Throws_NotSupportedException()
            {
                var trunk = new FileTrunk<string>("data/test-file");

                Assert.Throws<NotSupportedException>(() => trunk.GetHistory("test"));
            }

            [Fact]
            public void FileTrunk_Can_Export_And_Import()
            {
                var trunk1 = new FileTrunk<string>("data/export-source");
                trunk1.Save("key1", new NutShell<string> { Id = "key1", Payload = "value1" });

                var exported = trunk1.ExportChanges();

                var trunk2 = new FileTrunk<string>("data/export-target");
                trunk2.ImportChanges(exported);

                var loaded = trunk2.Load("key1");
                Assert.Equal("value1", loaded?.Payload);
            }
        }

        public class MemoryTrunkTests
        {
            [Fact]
            public void MemoryTrunk_Can_Save_And_Load()
            {
                var trunk = new MemoryTrunk<string>();
                var shell = new NutShell<string> { Id = "test", Payload = "value" };

                trunk.Save("test", shell);
                var loaded = trunk.Load("test");

                Assert.NotNull(loaded);
                Assert.Equal("value", loaded.Payload);
            }

            [Fact]
            public void MemoryTrunk_Returns_Null_For_Missing_Key()
            {
                var trunk = new MemoryTrunk<string>();
                var loaded = trunk.Load("nonexistent");

                Assert.Null(loaded);
            }

            [Fact]
            public void MemoryTrunk_Can_Delete()
            {
                var trunk = new MemoryTrunk<string>();
                trunk.Save("test", new NutShell<string> { Id = "test", Payload = "value" });

                trunk.Delete("test");
                var loaded = trunk.Load("test");

                Assert.Null(loaded);
            }

            [Fact]
            public void MemoryTrunk_GetHistory_Throws_NotSupportedException()
            {
                var trunk = new MemoryTrunk<string>();

                Assert.Throws<NotSupportedException>(() => trunk.GetHistory("test"));
            }
        }

        public class DocumentStoreTrunkTests
        {
            private static string GetUniquePath(string name) =>
                $"data/test-{Guid.NewGuid():N}/{name}";

            [Fact]
            public void DocumentStoreTrunk_Can_Save_And_Load()
            {
                var trunk = new DocumentStoreTrunk<string>(GetUniquePath("save-load"));
                var shell = new NutShell<string> { Id = "test", Payload = "value" };

                trunk.Save("test", shell);
                var loaded = trunk.Load("test");

                Assert.NotNull(loaded);
                Assert.Equal("value", loaded.Payload);
            }

            [Fact]
            public void DocumentStoreTrunk_Tracks_History()
            {
                var trunk = new DocumentStoreTrunk<string>(GetUniquePath("history"));

                trunk.Save("key1", new NutShell<string> { Id = "key1", Payload = "version1" });
                trunk.Save("key1", new NutShell<string> { Id = "key1", Payload = "version2" });
                trunk.Save("key1", new NutShell<string> { Id = "key1", Payload = "version3" });

                var history = trunk.GetHistory("key1");

                Assert.Equal(2, history.Count); // 2 previous versions
                Assert.Equal("version1", history[0].Payload);
                Assert.Equal("version2", history[1].Payload);
            }

            [Fact]
            public void DocumentStoreTrunk_Current_Shows_Latest()
            {
                var trunk = new DocumentStoreTrunk<string>(GetUniquePath("latest"));

                trunk.Save("key1", new NutShell<string> { Id = "key1", Payload = "version1" });
                trunk.Save("key1", new NutShell<string> { Id = "key1", Payload = "version2" });

                var current = trunk.Load("key1");

                Assert.Equal("version2", current?.Payload);
            }

            [Fact]
            public void DocumentStoreTrunk_Delete_Moves_To_History()
            {
                var trunk = new DocumentStoreTrunk<string>(GetUniquePath("delete"));

                trunk.Save("key1", new NutShell<string> { Id = "key1", Payload = "value" });
                trunk.Delete("key1");

                var current = trunk.Load("key1");
                var history = trunk.GetHistory("key1");

                Assert.Null(current);
                Assert.Single(history);
                Assert.Equal("value", history[0].Payload);
            }

            [Fact]
            public void DocumentStoreTrunk_Persists_Across_Instances()
            {
                var path = GetUniquePath("persist");

                // First instance
                var trunk1 = new DocumentStoreTrunk<string>(path);
                trunk1.Save("persistent", new NutShell<string> { Id = "persistent", Payload = "data" });

                // Second instance (loads from log)
                var trunk2 = new DocumentStoreTrunk<string>(path);
                var loaded = trunk2.Load("persistent");

                Assert.Equal("data", loaded?.Payload);
            }
        }

        public class TreeWithTrunkTests
        {
            private static string GetUniquePath(string name) =>
                $"data/test-{Guid.NewGuid():N}/{name}";

            [Fact]
            public void Tree_Delegates_GetHistory_To_Trunk()
            {
                var trunk = new DocumentStoreTrunk<string>(GetUniquePath("tree-history"));
                var tree = new Tree<string>(trunk);

                tree.Stash("key1", "version1");
                tree.Stash("key1", "version2");

                var history = tree.GetHistory("key1");

                Assert.Single(history); // 1 previous version
                Assert.Equal("version1", history[0].Payload);
            }

            [Fact]
            public void Tree_ExportChanges_Delegates_To_Trunk()
            {
                var trunk = new MemoryTrunk<string>();
                var tree = new Tree<string>(trunk);

                tree.Stash("key1", "value1");
                tree.Stash("key2", "value2");

                var exported = tree.ExportChanges();

                Assert.Equal(2, exported.Count());
            }

            [Fact]
            public void Tree_Can_Switch_Trunks_Via_Export_Import()
            {
                // Start with MemoryTrunk
                var memoryTree = new Tree<string>(new MemoryTrunk<string>());
                memoryTree.Stash("data", "important");

                // Export and import to FileTrunk
                var fileTrunk = new FileTrunk<string>("data/trunk-switch");
                fileTrunk.ImportChanges(memoryTree.ExportChanges());
                var fileTree = new Tree<string>(fileTrunk);

                var loaded = fileTree.Crack("data");
                Assert.Equal("important", loaded);
            }
        }
    }
}
