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
                var shell = new Nut<string> { Id = "test", Payload = "value" };

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
                trunk1.Save("key1", new Nut<string> { Id = "key1", Payload = "value1" });

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
                var shell = new Nut<string> { Id = "test", Payload = "value" };

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
                trunk.Save("test", new Nut<string> { Id = "test", Payload = "value" });

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
                var shell = new Nut<string> { Id = "test", Payload = "value" };

                trunk.Save("test", shell);
                var loaded = trunk.Load("test");

                Assert.NotNull(loaded);
                Assert.Equal("value", loaded.Payload);
            }

            [Fact]
            public void DocumentStoreTrunk_Tracks_History()
            {
                var trunk = new DocumentStoreTrunk<string>(GetUniquePath("history"));

                trunk.Save("key1", new Nut<string> { Id = "key1", Payload = "version1" });
                trunk.Save("key1", new Nut<string> { Id = "key1", Payload = "version2" });
                trunk.Save("key1", new Nut<string> { Id = "key1", Payload = "version3" });

                var history = trunk.GetHistory("key1");

                Assert.Equal(2, history.Count); // 2 previous versions
                Assert.Equal("version1", history[0].Payload);
                Assert.Equal("version2", history[1].Payload);
            }

            [Fact]
            public void DocumentStoreTrunk_Current_Shows_Latest()
            {
                var trunk = new DocumentStoreTrunk<string>(GetUniquePath("latest"));

                trunk.Save("key1", new Nut<string> { Id = "key1", Payload = "version1" });
                trunk.Save("key1", new Nut<string> { Id = "key1", Payload = "version2" });

                var current = trunk.Load("key1");

                Assert.Equal("version2", current?.Payload);
            }

            [Fact]
            public void DocumentStoreTrunk_Delete_Moves_To_History()
            {
                var trunk = new DocumentStoreTrunk<string>(GetUniquePath("delete"));

                trunk.Save("key1", new Nut<string> { Id = "key1", Payload = "value" });
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
                trunk1.Save("persistent", new Nut<string> { Id = "persistent", Payload = "data" });
                trunk1.Dispose();

                // Second instance (loads from log)
                var trunk2 = new DocumentStoreTrunk<string>(path);
                var loaded = trunk2.Load("persistent");

                Assert.Equal("data", loaded?.Payload);
                trunk2.Dispose();
            }

            [Fact]
            public void DocumentStoreTrunk_Uses_Compact_JSON_Format()
            {
                var path = GetUniquePath("compact-json");

                var trunk = new DocumentStoreTrunk<string>(path);
                trunk.Save("test1", new Nut<string> { Id = "test1", Payload = "data1" });
                trunk.Save("test2", new Nut<string> { Id = "test2", Payload = "data2" });
                trunk.Save("test3", new Nut<string> { Id = "test3", Payload = "data3" });
                trunk.Dispose();

                // Read log file and verify it's compact JSON (one line per entry)
                var logPath = System.IO.Path.Combine(path, "changes.log");
                var lines = System.IO.File.ReadAllLines(logPath);

                // Should have 3 lines (one per save)
                Assert.Equal(3, lines.Length);

                // Each line should be valid JSON (no internal newlines)
                foreach (var line in lines)
                {
                    Assert.DoesNotContain('\n', line);
                    Assert.DoesNotContain('\r', line);

                    // Should be parseable as JSON
                    var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject(line);
                    Assert.NotNull(parsed);
                }
            }

            [Fact]
            public void DocumentStoreTrunk_Loads_From_Compact_JSON_Log()
            {
                var path = GetUniquePath("load-compact");

                // Write compact JSON log
                var trunk1 = new DocumentStoreTrunk<string>(path);
                trunk1.Save("key1", new Nut<string> { Id = "key1", Payload = "value1" });
                trunk1.Save("key2", new Nut<string> { Id = "key2", Payload = "value2" });
                trunk1.Dispose();

                // Reload from log
                var trunk2 = new DocumentStoreTrunk<string>(path);
                var loaded1 = trunk2.Load("key1");
                var loaded2 = trunk2.Load("key2");

                Assert.Equal("value1", loaded1?.Payload);
                Assert.Equal("value2", loaded2?.Payload);
                trunk2.Dispose();
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
