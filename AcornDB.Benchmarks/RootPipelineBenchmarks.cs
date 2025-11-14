using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Storage.Roots;
using AcornDB.Compression;
using AcornDB.Security;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for IRoot pipeline (compression, encryption) performance impact.
    /// Measures throughput reduction and storage savings from root processors.
    ///
    /// TRUNK SELECTION RATIONALE:
    /// Uses BTreeTrunk for all benchmarks because:
    /// - Compression/encryption are features of PERSISTENT storage (file-based trunks)
    /// - MemoryTrunk doesn't benefit from compression (data is never serialized to disk)
    /// - BTreeTrunk provides realistic measurement of compression/encryption overhead
    /// - File I/O is where compression savings actually matter (disk space, network transfer)
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 5)]
    public class RootPipelineBenchmarks
    {
        private BTreeTrunk<TestDocument>? _btreeTrunk;
        private FileTrunk<TestDocument>? _fileTrunk;
        private Tree<TestDocument>? _tree;

        public class TestDocument
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public int Value { get; set; }
            public DateTime Created { get; set; }
        }

        [Params(1_000, 10_000)]
        public int DocumentCount;

        [Params(1024, 10240, 102400)] // 1KB, 10KB, 100KB documents
        public int DocumentSize;

        private string _tempDir = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"acorndb_roots_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _btreeTrunk?.Dispose();
            _fileTrunk = null;

            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); } catch { }
            }
        }

        private Tree<TestDocument> CreateTree(ITrunk<TestDocument> trunk)
        {
            var tree = new Tree<TestDocument>(trunk);
            tree.TtlEnforcementEnabled = false;
            tree.CacheEvictionEnabled = false;
            return tree;
        }

        private string GenerateContent(int size)
        {
            // Generate compressible content
            var baseText = "This is a test document with repeated content for compression testing. ";
            var sb = new System.Text.StringBuilder();
            while (sb.Length < size)
            {
                sb.Append(baseText);
            }
            return sb.ToString().Substring(0, size);
        }

        // ===== BTreeTrunk Baseline (No Roots) =====

        [Benchmark(Baseline = true)]
        public void BTreeTrunk_NoRoots_Baseline()
        {
            var dir = Path.Combine(_tempDir, $"btree_baseline_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _tree = CreateTree(_btreeTrunk);

            var content = GenerateContent(DocumentSize);

            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Content = content,
                    Value = i,
                    Created = DateTime.UtcNow
                });
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Compression Benchmarks =====

        [Benchmark]
        public void BTreeTrunk_WithCompression_Gzip()
        {
            var dir = Path.Combine(_tempDir, $"btree_gzip_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _btreeTrunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));
            _tree = CreateTree(_btreeTrunk);

            var content = GenerateContent(DocumentSize);

            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Content = content,
                    Value = i,
                    Created = DateTime.UtcNow
                });
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void BTreeTrunk_WithCompression_Brotli()
        {
            var dir = Path.Combine(_tempDir, $"btree_brotli_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _btreeTrunk.AddRoot(new CompressionRoot(new BrotliCompressionProvider(), sequence: 100));
            _tree = CreateTree(_btreeTrunk);

            var content = GenerateContent(DocumentSize);

            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Content = content,
                    Value = i,
                    Created = DateTime.UtcNow
                });
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Encryption Benchmarks =====

        [Benchmark]
        public void BTreeTrunk_WithEncryption_AES256()
        {
            var dir = Path.Combine(_tempDir, $"btree_aes_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            var encryption = AesEncryptionProvider.FromPassword("benchmark-password", "benchmark-salt");
            _btreeTrunk.AddRoot(new EncryptionRoot(encryption, sequence: 200));
            _tree = CreateTree(_btreeTrunk);

            var content = GenerateContent(DocumentSize);

            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Content = content,
                    Value = i,
                    Created = DateTime.UtcNow
                });
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Pipeline (Compression + Encryption) =====

        [Benchmark]
        public void BTreeTrunk_WithCompressionAndEncryption()
        {
            var dir = Path.Combine(_tempDir, $"btree_both_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _btreeTrunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));
            var encryption = AesEncryptionProvider.FromPassword("benchmark-password", "benchmark-salt");
            _btreeTrunk.AddRoot(new EncryptionRoot(encryption, sequence: 200));
            _tree = CreateTree(_btreeTrunk);

            var content = GenerateContent(DocumentSize);

            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Content = content,
                    Value = i,
                    Created = DateTime.UtcNow
                });
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== FileTrunk Comparisons =====

        [Benchmark]
        public void FileTrunk_WithCompression()
        {
            var dir = Path.Combine(_tempDir, $"file_gzip_{Guid.NewGuid()}");
            _fileTrunk = new FileTrunk<TestDocument>(dir);
            _fileTrunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));
            _tree = CreateTree(_fileTrunk);

            var content = GenerateContent(DocumentSize);

            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Content = content,
                    Value = i,
                    Created = DateTime.UtcNow
                });
            }
        }

        [Benchmark]
        public void FileTrunk_WithEncryption()
        {
            var dir = Path.Combine(_tempDir, $"file_aes_{Guid.NewGuid()}");
            _fileTrunk = new FileTrunk<TestDocument>(dir);
            var encryption = AesEncryptionProvider.FromPassword("benchmark-password", "benchmark-salt");
            _fileTrunk.AddRoot(new EncryptionRoot(encryption, sequence: 200));
            _tree = CreateTree(_fileTrunk);

            var content = GenerateContent(DocumentSize);

            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Content = content,
                    Value = i,
                    Created = DateTime.UtcNow
                });
            }
        }

        // ===== Read Performance with Roots =====

        [Benchmark]
        public void BTreeTrunk_Read_WithCompression()
        {
            var dir = Path.Combine(_tempDir, $"btree_read_gzip_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _btreeTrunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));
            _tree = CreateTree(_btreeTrunk);

            var content = GenerateContent(DocumentSize);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Content = content,
                    Value = i,
                    Created = DateTime.UtcNow
                });
            }

            // Benchmark: Read all documents (decompression overhead)
            for (int i = 0; i < DocumentCount; i++)
            {
                var doc = _tree.Crack($"doc-{i}");
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void BTreeTrunk_Read_WithEncryption()
        {
            var dir = Path.Combine(_tempDir, $"btree_read_aes_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            var encryption = AesEncryptionProvider.FromPassword("benchmark-password", "benchmark-salt");
            _btreeTrunk.AddRoot(new EncryptionRoot(encryption, sequence: 200));
            _tree = CreateTree(_btreeTrunk);

            var content = GenerateContent(DocumentSize);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Content = content,
                    Value = i,
                    Created = DateTime.UtcNow
                });
            }

            // Benchmark: Read all documents (decryption overhead)
            for (int i = 0; i < DocumentCount; i++)
            {
                var doc = _tree.Crack($"doc-{i}");
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        [Benchmark]
        public void BTreeTrunk_Read_WithCompressionAndEncryption()
        {
            var dir = Path.Combine(_tempDir, $"btree_read_both_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _btreeTrunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));
            var encryption = AesEncryptionProvider.FromPassword("benchmark-password", "benchmark-salt");
            _btreeTrunk.AddRoot(new EncryptionRoot(encryption, sequence: 200));
            _tree = CreateTree(_btreeTrunk);

            var content = GenerateContent(DocumentSize);

            // Pre-populate
            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Content = content,
                    Value = i,
                    Created = DateTime.UtcNow
                });
            }

            // Benchmark: Read all documents (decrypt + decompress overhead)
            for (int i = 0; i < DocumentCount; i++)
            {
                var doc = _tree.Crack($"doc-{i}");
            }

            _btreeTrunk.Dispose();
            _btreeTrunk = null;
        }

        // ===== Compression Ratio Analysis =====

        [Benchmark]
        public void Compression_Ratio_Measurement_Gzip()
        {
            var dir = Path.Combine(_tempDir, $"ratio_gzip_{Guid.NewGuid()}");
            _btreeTrunk = new BTreeTrunk<TestDocument>(dir);
            _btreeTrunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));
            _tree = CreateTree(_btreeTrunk);

            var content = GenerateContent(DocumentSize);

            for (int i = 0; i < DocumentCount; i++)
            {
                _tree.Stash(new TestDocument
                {
                    Id = $"doc-{i}",
                    Name = $"Document {i}",
                    Content = content,
                    Value = i,
                    Created = DateTime.UtcNow
                });
            }

            _btreeTrunk.Dispose();

            // File size comparison (check disk usage)
            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
            long totalSize = files.Sum(f => new FileInfo(f).Length);

            // Store for reporting (BenchmarkDotNet doesn't directly show this, but it's useful info)
            _btreeTrunk = null;
        }
    }

    /// <summary>
    /// Expected Performance Impact:
    ///
    /// Write Performance (1K docs, 10KB each):
    /// - No Roots (Baseline): ~50ms
    /// - Gzip Compression: ~70ms (+40% slower, 60-70% storage savings)
    /// - Brotli Compression: ~100ms (+100% slower, 70-80% storage savings)
    /// - AES Encryption: ~60ms (+20% slower, no storage savings)
    /// - Compression + Encryption: ~80ms (+60% slower, 60-70% storage savings)
    ///
    /// Read Performance (1K docs, 10KB each):
    /// - No Roots (Baseline): ~10ms
    /// - Gzip Decompression: ~15ms (+50% slower)
    /// - AES Decryption: ~12ms (+20% slower)
    /// - Decompress + Decrypt: ~18ms (+80% slower)
    ///
    /// Storage Savings (highly dependent on data compressibility):
    /// - Text/JSON (high compressibility): 60-80% reduction
    /// - Binary/Images (low compressibility): 0-20% reduction
    /// - Pre-compressed data: May increase size due to overhead
    ///
    /// Recommendation:
    /// - Use Gzip for balanced compression speed/ratio
    /// - Use Brotli for maximum compression (slower writes)
    /// - Use encryption when data security > performance
    /// - Profile your actual data to measure compression ratio
    /// </summary>
}
