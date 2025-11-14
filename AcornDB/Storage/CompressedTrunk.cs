using System;
using AcornDB.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AcornDB.Compression;
using AcornDB.Storage.Serialization;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// [DEPRECATED] Compressed wrapper for any ITrunk implementation
    /// Compresses payloads before storage, decompresses on retrieval
    ///
    /// ⚠️ IMPORTANT: This class is DEPRECATED and will be REMOVED in v0.6.0.
    ///
    /// Why this is deprecated:
    /// - Old wrapper pattern creates type system complexity (Nut<T> → Nut<CompressedNut>)
    /// - Cannot dynamically add/remove compression at runtime
    /// - Difficult to inspect transformation chain
    /// - Doesn't support policy context or transformation tracking
    ///
    /// Migration to IRoot pattern:
    ///
    /// OLD CODE:
    ///   var baseTrunk = new FileTrunk<CompressedNut>();
    ///   var compressedTrunk = new CompressedTrunk<User>(baseTrunk, new GzipCompressionProvider());
    ///   var tree = new Tree<User>(compressedTrunk);
    ///
    /// NEW CODE (Option 1 - Direct):
    ///   var trunk = new FileTrunk<User>();
    ///   trunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));
    ///   var tree = new Tree<User>(trunk);
    ///
    /// NEW CODE (Option 2 - Fluent):
    ///   var trunk = new FileTrunk<User>()
    ///       .WithCompression(new GzipCompressionProvider());
    ///   var tree = new Tree<User>(trunk);
    ///
    /// NEW CODE (Option 3 - Acorn Builder):
    ///   var tree = new Acorn<User>()
    ///       .WithCompression()
    ///       .Sprout();
    ///
    /// See ROOT_ARCHITECTURE.md for complete migration guide.
    /// </summary>
    [Obsolete("CompressedTrunk is deprecated and will be REMOVED in v0.6.0. Use CompressionRoot with trunk.AddRoot() or trunk.WithCompression() instead. " +
              "Example: trunk.WithCompression(new GzipCompressionProvider()). See ROOT_ARCHITECTURE.md for migration guide.", true)]
    public class CompressedTrunk<T> : ITrunk<T>
    {
        private readonly ITrunk<CompressedNut> _innerTrunk;
        private readonly ICompressionProvider _compression;
        private readonly ISerializer _serializer;

        public CompressedTrunk(
            ITrunk<CompressedNut> innerTrunk,
            ICompressionProvider compression,
            ISerializer? serializer = null)
        {
            _innerTrunk = innerTrunk ?? throw new ArgumentNullException(nameof(innerTrunk));
            _compression = compression ?? throw new ArgumentNullException(nameof(compression));
            _serializer = serializer ?? new NewtonsoftJsonSerializer();
        }

        public void Stash(string id, Nut<T> nut)
        {
            var compressed = CompressNut(nut);
            _innerTrunk.Stash(id, compressed);
        }

        [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
        public void Save(string id, Nut<T> nut) => Stash(id, nut);

        public Nut<T>? Crack(string id)
        {
            var compressed = _innerTrunk.Crack(id);
            if (compressed == null) return null;
            return DecompressNut(compressed);
        }

        [Obsolete("Use Crack() instead. This method will be removed in a future version.")]
        public Nut<T>? Load(string id) => Crack(id);

        public void Toss(string id)
        {
            _innerTrunk.Toss(id);
        }

        [Obsolete("Use Toss() instead. This method will be removed in a future version.")]
        public void Delete(string id) => Toss(id);

        public IEnumerable<Nut<T>> CrackAll()
        {
            return _innerTrunk.CrackAll()
                .Select(DecompressNut)
                .Where(n => n != null)!;
        }

        [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
        public IEnumerable<Nut<T>> LoadAll() => CrackAll();

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            var compressedHistory = _innerTrunk.GetHistory(id);
            return compressedHistory
                .Select(DecompressNut)
                .Where(n => n != null)
                .ToList()!;
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return _innerTrunk.ExportChanges()
                .Select(DecompressNut)
                .Where(n => n != null)!;
        }

        public void ImportChanges(IEnumerable<Nut<T>> changes)
        {
            var compressed = changes.Select(CompressNut);
            _innerTrunk.ImportChanges(compressed);
        }

        // Delegate to inner trunk's capabilities
        public ITrunkCapabilities Capabilities => _innerTrunk.Capabilities;

        // Root processors - not supported on wrapper trunks
        public IReadOnlyList<IRoot> Roots => Array.Empty<IRoot>();
        public void AddRoot(IRoot root) => throw new NotSupportedException("CompressedTrunk is obsolete. Use CompressionRoot with a modern trunk instead.");
        public bool RemoveRoot(string name) => false;

        private Nut<CompressedNut> CompressNut(Nut<T> nut)
        {
            var json = _serializer.Serialize(nut.Payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            var compressed = _compression.Compress(bytes);

            return new Nut<CompressedNut>
            {
                Id = nut.Id,
                Payload = new CompressedNut
                {
                    CompressedData = compressed,
                    OriginalSize = bytes.Length,
                    CompressedSize = compressed.Length,
                    Algorithm = _compression.AlgorithmName,
                    OriginalType = typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? "Unknown"
                },
                Timestamp = nut.Timestamp,
                ExpiresAt = nut.ExpiresAt,
                Version = nut.Version,
                ChangeId = nut.ChangeId,
                OriginNodeId = nut.OriginNodeId,
                HopCount = nut.HopCount
            };
        }

        private Nut<T>? DecompressNut(Nut<CompressedNut> compressedNut)
        {
            try
            {
                var decompressed = _compression.Decompress(compressedNut.Payload.CompressedData);
                var json = Encoding.UTF8.GetString(decompressed);
                var payload = _serializer.Deserialize<T>(json);

                return new Nut<T>
                {
                    Id = compressedNut.Id,
                    Payload = payload,
                    Timestamp = compressedNut.Timestamp,
                    ExpiresAt = compressedNut.ExpiresAt,
                    Version = compressedNut.Version,
                    ChangeId = compressedNut.ChangeId,
                    OriginNodeId = compressedNut.OriginNodeId,
                    HopCount = compressedNut.HopCount
                };
            }
            catch (Exception ex)
            {
                AcornLog.Info($"⚠️ Failed to decompress nut '{compressedNut.Id}': {ex.Message}");
                return null;
            }
        }
    }
}
