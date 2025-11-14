using System;
using System.Text;
using AcornDB.Compression;
using AcornDB.Policy;
using AcornDB.Security;
using AcornDB.Storage;
using AcornDB.Storage.Roots;
using Xunit;

namespace AcornDB.Test
{
    public class PolicyEnforcementRootTests
    {
        public class BasicPolicyEnforcementTests
        {
            [Fact]
            public void PolicyRoot_Validates_Data_On_Write()
            {
                var engine = new LocalPolicyEngine();
                var root = new PolicyEnforcementRoot(engine, sequence: 10);
                var data = Encoding.UTF8.GetBytes("{\"test\":\"data\"}");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Write" },
                    DocumentId = "test"
                };

                // Should not throw for valid data
                var result = root.OnStash(data, context);
                Assert.Equal(data, result);
            }

            [Fact]
            public void PolicyRoot_Validates_Data_On_Read()
            {
                var engine = new LocalPolicyEngine();
                var root = new PolicyEnforcementRoot(engine, sequence: 10);
                var data = Encoding.UTF8.GetBytes("{\"test\":\"data\"}");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Read" },
                    DocumentId = "test"
                };

                // Should not throw for valid data
                var result = root.OnCrack(data, context);
                Assert.Equal(data, result);
            }

            [Fact]
            public void PolicyRoot_Skips_Validation_When_EnforceOnWrite_Is_False()
            {
                var engine = new LocalPolicyEngine();
                var options = new PolicyEnforcementOptions { EnforceOnWrite = false };
                var root = new PolicyEnforcementRoot(engine, sequence: 10, options: options);
                var data = Encoding.UTF8.GetBytes("invalid json");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Write" }
                };

                // Should not throw even for invalid data when EnforceOnWrite is false
                var result = root.OnStash(data, context);
                Assert.Equal(data, result);
            }

            [Fact]
            public void PolicyRoot_Skips_Validation_When_EnforceOnRead_Is_False()
            {
                var engine = new LocalPolicyEngine();
                var options = new PolicyEnforcementOptions { EnforceOnRead = false };
                var root = new PolicyEnforcementRoot(engine, sequence: 10, options: options);
                var data = Encoding.UTF8.GetBytes("invalid json");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Read" }
                };

                // Should not throw even for invalid data when EnforceOnRead is false
                var result = root.OnCrack(data, context);
                Assert.Equal(data, result);
            }
        }

        public class MetricsTests
        {
            [Fact]
            public void PolicyRoot_Tracks_Success_Metrics()
            {
                var engine = new LocalPolicyEngine();
                var root = new PolicyEnforcementRoot(engine, sequence: 10);
                var data = Encoding.UTF8.GetBytes("{\"valid\":\"json\"}");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Write" }
                };

                root.Metrics.Reset();
                root.OnStash(data, context);
                root.OnCrack(data, context);

                Assert.True(root.Metrics.TotalWriteChecks >= 1);
                Assert.True(root.Metrics.TotalReadChecks >= 1);
                Assert.Equal(0, root.Metrics.TotalDenials);
                Assert.Equal(0, root.Metrics.TotalErrors);
            }
        }

        public class SignatureTests
        {
            [Fact]
            public void PolicyRoot_Adds_Signature_To_Context()
            {
                var engine = new LocalPolicyEngine();
                var root = new PolicyEnforcementRoot(engine, sequence: 10);
                var data = Encoding.UTF8.GetBytes("{\"test\":\"data\"}");
                var context = new RootProcessingContext
                {
                    PolicyContext = new PolicyContext { Operation = "Write" }
                };

                root.OnStash(data, context);

                Assert.Contains("policy-enforcement", context.TransformationSignatures);
            }
        }

        public class IntegrationTests
        {
            [Fact]
            public void PolicyRoot_Works_With_Compression_And_Encryption()
            {
                var trunk = new MemoryTrunk<TestDocument>();
                var engine = new LocalPolicyEngine();

                // Add policy enforcement first (sequence 10)
                trunk.AddRoot(new PolicyEnforcementRoot(engine, sequence: 10));
                trunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));
                trunk.AddRoot(new EncryptionRoot(
                    AesEncryptionProvider.FromPassword("pwd", "salt"), sequence: 200));

                var doc = new TestDocument
                {
                    Id = "doc1",
                    Data = "test data"
                };

                trunk.Save("doc1", new Nut<TestDocument> { Id = "doc1", Payload = doc });
                var loaded = trunk.Load("doc1");

                Assert.NotNull(loaded);
                Assert.Equal("test data", loaded.Payload.Data);
            }

            [Fact]
            public void PolicyRoot_Works_With_DocumentStoreTrunk()
            {
                var path = $"data/test-{Guid.NewGuid():N}/policy-docstore";
                var trunk = new DocumentStoreTrunk<TestDocument>(path);
                var engine = new LocalPolicyEngine();

                trunk.AddRoot(new PolicyEnforcementRoot(engine, sequence: 10));

                var doc = new TestDocument { Id = "doc1", Data = "test" };
                trunk.Save("doc1", new Nut<TestDocument> { Id = "doc1", Payload = doc });
                var loaded = trunk.Load("doc1");

                Assert.NotNull(loaded);
                Assert.Equal("test", loaded.Payload.Data);

                trunk.Dispose();
            }
        }

        public class TestDocument
        {
            public string Id { get; set; } = "";
            public string Data { get; set; } = "";
        }
    }
}
