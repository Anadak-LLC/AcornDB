using System;
using System.Collections.Generic;
using System.IO.Compression;
using AcornDB.Storage;
using AcornDB.Security;
using AcornDB.Compression;
using AcornDB.Cache;
using AcornDB.Conflict;
using AcornDB.Git;

namespace AcornDB
{
    /// <summary>
    /// Fluent builder for configuring and creating Trees.
    /// Usage: new Acorn&lt;User&gt;().WithEncryption("password").WithCompression().Sprout()
    /// </summary>
    public class Acorn<T>
    {
        private ITrunk<T>? _trunk;
        private string? _storagePath;
        private ICacheStrategy<T>? _cacheStrategy;
        private int _cacheMaxSize = 10_000;
        private IConflictJudge<T>? _conflictJudge;
        private IEncryptionProvider? _encryptionProvider;
        private ICompressionProvider? _compressionProvider;
        private bool _useEncryption;
        private bool _useCompression;
        private string? _encryptionPassword;
        private string? _encryptionSalt;
        private CompressionLevel _compressionLevel = CompressionLevel.Optimal;

        // Git-specific configuration
        private bool _useGitStorage;
        private string? _gitAuthorName;
        private string? _gitAuthorEmail;
        private bool _gitAutoPush;
        private IGitProvider? _gitProvider;

        /// <summary>
        /// Configure encryption with a password
        /// </summary>
        public Acorn<T> WithEncryption(string password, string? salt = null)
        {
            _useEncryption = true;
            _encryptionPassword = password;
            _encryptionSalt = salt;
            return this;
        }

        /// <summary>
        /// Configure encryption with a custom provider
        /// </summary>
        public Acorn<T> WithEncryption(IEncryptionProvider encryptionProvider)
        {
            _useEncryption = true;
            _encryptionProvider = encryptionProvider;
            return this;
        }

        /// <summary>
        /// Configure Gzip compression
        /// </summary>
        public Acorn<T> WithCompression(CompressionLevel level = CompressionLevel.Optimal)
        {
            _useCompression = true;
            _compressionLevel = level;
            return this;
        }

        /// <summary>
        /// Configure compression with a custom provider
        /// </summary>
        public Acorn<T> WithCompression(ICompressionProvider compressionProvider)
        {
            _useCompression = true;
            _compressionProvider = compressionProvider;
            return this;
        }

        /// <summary>
        /// Configure storage path for file-based trunks
        /// </summary>
        public Acorn<T> WithStoragePath(string storagePath)
        {
            _storagePath = storagePath;
            return this;
        }

        /// <summary>
        /// Configure a custom trunk implementation
        /// </summary>
        public Acorn<T> WithTrunk(ITrunk<T> trunk)
        {
            _trunk = trunk;
            return this;
        }

        /// <summary>
        /// Configure cache strategy
        /// </summary>
        public Acorn<T> WithCacheStrategy(ICacheStrategy<T> cacheStrategy)
        {
            _cacheStrategy = cacheStrategy;
            return this;
        }

        /// <summary>
        /// Configure LRU cache with max size
        /// </summary>
        public Acorn<T> WithLRUCache(int maxSize = 10_000)
        {
            _cacheMaxSize = maxSize;
            _cacheStrategy = null; // Will create LRU in Sprout()
            return this;
        }

        /// <summary>
        /// Configure conflict resolution judge
        /// </summary>
        public Acorn<T> WithConflictJudge(IConflictJudge<T> conflictJudge)
        {
            _conflictJudge = conflictJudge;
            return this;
        }

        /// <summary>
        /// Use in-memory trunk (no persistence)
        /// </summary>
        public Acorn<T> InMemory()
        {
            _trunk = new MemoryTrunk<T>();
            return this;
        }

        /// <summary>
        /// Use Git-backed storage where every Stash() is a commit!
        /// 🐿️ Peak nuttiness: Your database IS your Git history!
        /// </summary>
        /// <param name="repoPath">Path to Git repository (will be created if doesn't exist)</param>
        /// <param name="authorName">Git author name for commits</param>
        /// <param name="authorEmail">Git author email for commits</param>
        /// <param name="autoPush">Automatically push to remote after each commit (if remote exists)</param>
        public Acorn<T> WithGitStorage(
            string? repoPath = null,
            string authorName = "AcornDB",
            string authorEmail = "acorn@acorndb.dev",
            bool autoPush = false)
        {
            _useGitStorage = true;
            _storagePath = repoPath; // Will use this as git repo path
            _gitAuthorName = authorName;
            _gitAuthorEmail = authorEmail;
            _gitAutoPush = autoPush;
            return this;
        }

        /// <summary>
        /// Use Git-backed storage with a custom Git provider
        /// </summary>
        public Acorn<T> WithGitStorage(IGitProvider gitProvider)
        {
            _useGitStorage = true;
            _gitProvider = gitProvider;
            return this;
        }

        /// <summary>
        /// Grow a trunk from the Nursery by type ID
        /// Example: new Acorn&lt;User&gt;().WithTrunkFromNursery("file", new() { { "path", "./data" } }).Sprout()
        /// </summary>
        /// <param name="typeId">Trunk type identifier (e.g., "file", "memory", "docstore", "git", "azure")</param>
        /// <param name="configuration">Configuration dictionary for the trunk</param>
        public Acorn<T> WithTrunkFromNursery(string typeId, Dictionary<string, object>? configuration = null)
        {
            var config = configuration ?? new Dictionary<string, object>();
            _trunk = Nursery.Grow<T>(typeId, config);
            return this;
        }

        /// <summary>
        /// Sprout the configured tree!
        /// </summary>
        public Tree<T> Sprout()
        {
            ITrunk<T> trunk;

            // If user provided a custom trunk, use it directly
            if (_trunk != null)
            {
                trunk = _trunk;
            }
            else
            {
                // Otherwise, build the trunk based on configuration
                trunk = BuildTrunk();
            }

            // Create cache strategy if not provided
            var cacheStrategy = _cacheStrategy ?? new LRUCacheStrategy<T>(_cacheMaxSize);

            // Create conflict judge if not provided
            var conflictJudge = _conflictJudge ?? new TimestampJudge<T>();

            // Create and return the tree
            return new Tree<T>(trunk, cacheStrategy, conflictJudge);
        }

        private ITrunk<T> BuildTrunk()
        {
            // Check for Git storage first (highest priority)
            if (_useGitStorage)
            {
                return BuildGitTrunk();
            }

            if (_useEncryption && _useCompression)
            {
                // Both encryption and compression
                return BuildEncryptedAndCompressedTrunk();
            }
            else if (_useEncryption)
            {
                // Just encryption
                return BuildEncryptedTrunk();
            }
            else if (_useCompression)
            {
                // Just compression
                return BuildCompressedTrunk();
            }
            else
            {
                // No encryption or compression
                return CreateFileTrunk<T>(_storagePath);
            }
        }

        private ITrunk<TPayload> CreateFileTrunk<TPayload>(string? storagePath)
        {
            return string.IsNullOrEmpty(storagePath)
                ? new FileTrunk<TPayload>()
                : new FileTrunk<TPayload>(storagePath);
        }

        private ITrunk<T> BuildEncryptedTrunk()
        {
            var encryption = _encryptionProvider ?? CreateEncryptionProvider();
            var innerTrunk = CreateFileTrunk<EncryptedNut>(_storagePath);
            return new EncryptedTrunk<T>(innerTrunk, encryption);
        }

        private ITrunk<T> BuildCompressedTrunk()
        {
            var compression = _compressionProvider ?? new GzipCompressionProvider(_compressionLevel);
            var innerTrunk = CreateFileTrunk<CompressedNut>(_storagePath);
            return new CompressedTrunk<T>(innerTrunk, compression);
        }

        private ITrunk<T> BuildEncryptedAndCompressedTrunk()
        {
            // Create providers
            var encryption = _encryptionProvider ?? CreateEncryptionProvider();
            var compression = _compressionProvider ?? new GzipCompressionProvider(_compressionLevel);

            // Create base trunk
            var baseTrunk = CreateFileTrunk<CompressedNut>(_storagePath);

            // Wrap: base -> compression -> encryption
            var compressedTrunk = new CompressedTrunk<EncryptedNut>(baseTrunk, compression);
            return new EncryptedTrunk<T>(compressedTrunk, encryption);
        }

        private ITrunk<T> BuildGitTrunk()
        {
            // Use custom provider if provided
            if (_gitProvider != null)
            {
                // Custom provider already configured, can't pass constructor params
                throw new NotSupportedException(
                    "When using a custom IGitProvider, please create GitHubTrunk manually and use WithTrunk()");
            }

            // Use default LibGit2Sharp provider
            var repoPath = _storagePath ?? $"./acorndb_git_{typeof(T).Name}";
            var authorName = _gitAuthorName ?? "AcornDB";
            var authorEmail = _gitAuthorEmail ?? "acorn@acorndb.dev";
            var autoPush = _gitAutoPush;

            return new GitHubTrunk<T>(repoPath, authorName, authorEmail, autoPush);
        }

        private IEncryptionProvider CreateEncryptionProvider()
        {
            if (!string.IsNullOrEmpty(_encryptionPassword))
            {
                var salt = _encryptionSalt ?? $"AcornDB_{typeof(T).FullName}";
                return AesEncryptionProvider.FromPassword(_encryptionPassword, salt);
            }

            throw new InvalidOperationException("Encryption password not provided");
        }
    }
}
