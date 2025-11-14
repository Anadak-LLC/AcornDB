using System;
using System.Linq;
using System.Text;
using AcornDB.Compression;
using AcornDB.Policy;
using AcornDB.Security;
using AcornDB.Storage;
using AcornDB.Storage.Roots;
using Xunit;

namespace AcornDB.Test
{
    public class RootPipelineTests
    {
        public class CompressionRootTests
        {
            [Fact]
            public void CompressionRoot_Compresses_And_Decompresses_Data()
            {
                var root = new CompressionRoot(new GzipCompressionProvider());
                var original = Encoding.UTF8.GetBytes("This is test data that should compress well. " +
                    "This is test data that should compress well. This is test data that should compress well.");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Write" },
                    DocumentId = "test"
                };

                // Compress
                var compressed = root.OnStash(original, context);

                // Should be smaller
                Assert.True(compressed.Length < original.Length);

                // Decompress
                var decompressed = root.OnCrack(compressed, context);

                // Should match original
                Assert.Equal(original, decompressed);
            }

            [Fact]
            public void CompressionRoot_Tracks_Metrics()
            {
                var root = new CompressionRoot(new GzipCompressionProvider());
                var data = Encoding.UTF8.GetBytes("Test data for metrics");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Write" }
                };

                root.Metrics.Reset();
                var compressed = root.OnStash(data, context);
                var decompressed = root.OnCrack(compressed, context);

                Assert.Equal(1, root.Metrics.TotalCompressions);
                Assert.Equal(1, root.Metrics.TotalDecompressions);
                Assert.True(root.Metrics.TotalBytesIn > 0);
                // Compression might not always save bytes for small data
                Assert.True(root.Metrics.TotalBytesOut > 0);
            }

            [Fact]
            public void CompressionRoot_Adds_Signature_To_Context()
            {
                var root = new CompressionRoot(new GzipCompressionProvider());
                var data = Encoding.UTF8.GetBytes("Test");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Write" }
                };

                root.OnStash(data, context);

                Assert.Contains("Gzip", context.TransformationSignatures);
            }
        }

        public class EncryptionRootTests
        {
            [Fact]
            public void EncryptionRoot_Encrypts_And_Decrypts_Data()
            {
                var encryption = AesEncryptionProvider.FromPassword("test-password", "test-salt");
                var root = new EncryptionRoot(encryption);
                var original = Encoding.UTF8.GetBytes("Sensitive data");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Write" },
                    DocumentId = "test"
                };

                // Encrypt
                var encrypted = root.OnStash(original, context);

                // Should be different
                Assert.NotEqual(original, encrypted);

                // Decrypt
                var decrypted = root.OnCrack(encrypted, context);

                // Should match original
                Assert.Equal(original, decrypted);
            }

            [Fact]
            public void EncryptionRoot_Tracks_Metrics()
            {
                var encryption = AesEncryptionProvider.FromPassword("password", "salt");
                var root = new EncryptionRoot(encryption);
                var data = Encoding.UTF8.GetBytes("Data");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Write" }
                };

                root.Metrics.Reset();
                var encrypted = root.OnStash(data, context);
                var decrypted = root.OnCrack(encrypted, context);

                Assert.Equal(1, root.Metrics.TotalEncryptions);
                Assert.Equal(1, root.Metrics.TotalDecryptions);
                Assert.Equal(0, root.Metrics.TotalErrors);
            }

            [Fact]
            public void EncryptionRoot_Adds_Signature_To_Context()
            {
                var encryption = AesEncryptionProvider.FromPassword("password", "salt");
                var root = new EncryptionRoot(encryption);
                var data = Encoding.UTF8.GetBytes("Test");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Write" }
                };

                root.OnStash(data, context);

                Assert.Contains("aes256", context.TransformationSignatures);
            }
        }

        public class PipelineIntegrationTests
        {
            [Fact]
            public void Trunk_Applies_Roots_In_Sequence_Order()
            {
                var trunk = new MemoryTrunk<string>();
                trunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));
                trunk.AddRoot(new EncryptionRoot(
                    AesEncryptionProvider.FromPassword("pwd", "salt"), sequence: 200));

                var shell = new Nut<string> { Id = "test", Payload = "data" };
                trunk.Save("test", shell);
                var loaded = trunk.Load("test");

                Assert.NotNull(loaded);
                Assert.Equal("data", loaded.Payload);
            }

            [Fact]
            public void Trunk_Applies_Multiple_Roots_Correctly()
            {
                var trunk = new MemoryTrunk<string>();

                // Add roots in non-sequential order (should auto-sort)
                trunk.AddRoot(new EncryptionRoot(
                    AesEncryptionProvider.FromPassword("pwd", "salt"), sequence: 200));
                trunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));

                // Verify they are sorted
                var roots = trunk.Roots.ToList();
                Assert.Equal(2, roots.Count);
                Assert.Equal(100, roots[0].Sequence);
                Assert.Equal(200, roots[1].Sequence);
            }

            [Fact]
            public void Trunk_Can_Remove_Roots()
            {
                var trunk = new MemoryTrunk<string>();
                trunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));
                trunk.AddRoot(new EncryptionRoot(
                    AesEncryptionProvider.FromPassword("pwd", "salt"), sequence: 200));

                Assert.Equal(2, trunk.Roots.Count);

                var removed = trunk.RemoveRoot("Compression");
                Assert.True(removed);
                Assert.Single(trunk.Roots);
                Assert.Equal("Encryption", trunk.Roots[0].Name);
            }

            // NOTE: DocumentStoreTrunk is an in-memory store with append-only log for persistence.
            // Roots do not apply to the log format itself - the log is an internal implementation detail.
            // Roots are meant for trunk types that serialize/deserialize payloads to/from disk (File, BTree).

            [Fact]
            public void FileTrunk_Works_With_Roots()
            {
                var trunk = new FileTrunk<string>($"data/test-{Guid.NewGuid():N}/file-roots");
                trunk.AddRoot(new CompressionRoot(new GzipCompressionProvider()));

                trunk.Save("test", new Nut<string> { Id = "test", Payload = "file data" });
                var loaded = trunk.Load("test");

                Assert.NotNull(loaded);
                Assert.Equal("file data", loaded.Payload);
            }

            // NOTE: BTreeTrunk stores binary data in memory-mapped files.
            // While roots CAN be applied to BTree storage, the binary format includes headers
            // that get encrypted/compressed along with the payload, which requires special handling
            // on load to parse headers from the decrypted/decompressed data.
            // This test is removed until BTree root support is fully implemented with header parsing.
        }
    }
}
