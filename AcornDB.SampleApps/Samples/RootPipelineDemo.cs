using System;
using System.Collections.Generic;
using System.Linq;
using AcornDB;
using AcornDB.Compression;
using AcornDB.Policy;
using AcornDB.Security;
using AcornDB.Storage;
using AcornDB.Storage.Roots;

namespace AcornDB.SampleApps.Samples
{
    /// <summary>
    /// Demonstrates the new IRoot byte pipeline pattern with compression, encryption, and policy enforcement.
    /// Shows how roots compose cleanly for secure, governed storage.
    /// </summary>
    public class RootPipelineDemo
    {
        public static void Run()
        {
            Console.WriteLine("🌰 AcornDB - IRoot Pipeline Demo");
            Console.WriteLine("================================\n");

            // Example 1: Basic compression and encryption
            Console.WriteLine("📦 Example 1: Compression + Encryption Pipeline");
            DemoCompressionAndEncryption();

            Console.WriteLine("\n" + new string('=', 80) + "\n");

            // Example 2: Policy enforcement with TTL
            Console.WriteLine("🔒 Example 2: Policy Enforcement with TTL");
            DemoPolicyEnforcement();

            Console.WriteLine("\n" + new string('=', 80) + "\n");

            // Example 3: Complete governed storage
            Console.WriteLine("🏛️ Example 3: Complete Governed Storage Stack");
            DemoGovernedStorage();

            Console.WriteLine("\n✅ Demo complete!");
        }

        private static void DemoCompressionAndEncryption()
        {
            // Create trunk with fluent API
            var trunk = new MemoryTrunk<Document>()
                .WithCompression(new GzipCompressionProvider())
                .WithEncryption(AesEncryptionProvider.FromPassword("demo-password", "demo-salt"));

            // Store some data
            var doc = new Document
            {
                Id = "doc1",
                Title = "Confidential Report",
                Content = "This is sensitive information that will be compressed and encrypted."
            };

            trunk.Save(doc.Id, new Nut<Document>
            {
                Id = doc.Id,
                Payload = doc,
                Timestamp = DateTime.UtcNow,
                Version = 1
            });

            Console.WriteLine($"✓ Stored document '{doc.Title}' with compression + encryption");

            // Inspect the root chain
            Console.WriteLine($"  Active roots: {string.Join(" → ", trunk.Roots.Select(r => r.Name))}");

            // Load it back (automatically decompressed and decrypted)
            var loaded = trunk.Load(doc.Id);
            Console.WriteLine($"✓ Loaded document: '{loaded?.Payload.Title}'");
            Console.WriteLine($"  Content matches: {loaded?.Payload.Content == doc.Content}");
        }

        private static void DemoPolicyEnforcement()
        {
            // Create policy engine
            var policyEngine = new LocalPolicyEngine();

            // Create trunk with policy enforcement
            var trunk = new MemoryTrunk<SensitiveDocument>()
                .WithPolicyEnforcement(policyEngine, options: PolicyEnforcementOptions.Strict);

            Console.WriteLine("✓ Created trunk with strict policy enforcement");

            // Store document with TTL
            var doc = new SensitiveDocument
            {
                Id = "temp1",
                Title = "Temporary Document",
                Content = "This will expire in 2 seconds",
                ExpiresAt = DateTime.UtcNow.AddSeconds(2)
            };

            trunk.Save(doc.Id, new Nut<SensitiveDocument>
            {
                Id = doc.Id,
                Payload = doc,
                Timestamp = DateTime.UtcNow,
                Version = 1
            });

            Console.WriteLine($"✓ Stored document with TTL: expires at {doc.ExpiresAt:HH:mm:ss}");

            // Load immediately (should work)
            var loaded = trunk.Load(doc.Id);
            Console.WriteLine($"✓ Loaded immediately: '{loaded?.Payload.Title}'");

            // Wait for expiration
            Console.WriteLine("  Waiting 3 seconds for TTL expiration...");
            System.Threading.Thread.Sleep(3000);

            // Try to load after expiration
            try
            {
                var expired = trunk.Load(doc.Id);
                if (expired == null)
                {
                    Console.WriteLine("✓ Document correctly returned null after TTL expiration");
                }
            }
            catch (PolicyViolationException ex)
            {
                Console.WriteLine($"✓ Policy enforcement blocked access: {ex.Message}");
            }
        }

        private static void DemoGovernedStorage()
        {
            // Create complete governed storage stack
            var policyEngine = new LocalPolicyEngine();

            var trunk = new MemoryTrunk<ClassifiedDocument>()
                .WithPolicyEnforcement(policyEngine, sequence: 10)
                .WithCompression(new GzipCompressionProvider(), sequence: 100)
                .WithEncryption(AesEncryptionProvider.FromPassword("classified", "salt"), sequence: 200);

            Console.WriteLine("✓ Created governed storage with 3-layer pipeline:");
            Console.WriteLine($"  {string.Join(" → ", trunk.Roots.Select(r => $"{r.Name}({r.Sequence})"))}");

            // Store classified document
            var doc = new ClassifiedDocument
            {
                Id = "classified1",
                Title = "Top Secret Report",
                Content = "Sensitive classified information",
                ClassificationLevel = "SECRET"
            };
            doc.AddTag("classified");
            doc.AddTag("restricted");

            trunk.Save(doc.Id, new Nut<ClassifiedDocument>
            {
                Id = doc.Id,
                Payload = doc,
                Timestamp = DateTime.UtcNow,
                Version = 1
            });

            Console.WriteLine($"✓ Stored classified document: '{doc.Title}'");
            Console.WriteLine($"  Classification: {doc.ClassificationLevel}");
            Console.WriteLine($"  Tags: {string.Join(", ", doc.Tags)}");

            // Load it back (passes through all 3 layers in reverse)
            var loaded = trunk.Load(doc.Id);
            Console.WriteLine($"✓ Successfully loaded through full pipeline");
            Console.WriteLine($"  Title: '{loaded?.Payload.Title}'");

            // Show metrics
            var compressionRoot = trunk.Roots.FirstOrDefault(r => r.Name == "Compression") as CompressionRoot;
            var encryptionRoot = trunk.Roots.FirstOrDefault(r => r.Name == "Encryption") as EncryptionRoot;
            var policyRoot = trunk.Roots.FirstOrDefault(r => r.Name == "PolicyEnforcement") as PolicyEnforcementRoot;

            if (compressionRoot != null)
                Console.WriteLine($"\n📊 Compression Metrics: {compressionRoot.Metrics}");
            if (encryptionRoot != null)
                Console.WriteLine($"📊 Encryption Metrics: {encryptionRoot.Metrics}");
            if (policyRoot != null)
                Console.WriteLine($"📊 Policy Metrics: {policyRoot.Metrics}");
        }

        // Sample data models
        public class Document
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Content { get; set; } = "";
        }

        public class SensitiveDocument
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Content { get; set; } = "";
            public DateTime? ExpiresAt { get; set; }
        }

        public class ClassifiedDocument : IPolicyTaggable
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Content { get; set; } = "";
            public string ClassificationLevel { get; set; } = "";

            private readonly List<string> _tags = new();
            public IEnumerable<string> Tags => _tags;

            public void AddTag(string tag) => _tags.Add(tag);
            public bool HasTag(string tag) => _tags.Contains(tag);
        }
    }
}
