using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AcornDB.Compression;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// Compressed wrapper for any ITrunk implementation
    /// Compresses payloads before storage, decompresses on retrieval
    /// </summary>
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

        public void Save(string id, Nut<T> nut)
        {
            var compressed = CompressNut(nut);
            _innerTrunk.Save(id, compressed);
        }

        public Nut<T>? Load(string id)
        {
            var compressed = _innerTrunk.Load(id);
            if (compressed == null) return null;
            return DecompressNut(compressed);
        }

        public void Delete(string id)
        {
            _innerTrunk.Delete(id);
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            return _innerTrunk.LoadAll()
                .Select(DecompressNut)
                .Where(n => n != null)!;
        }

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

        public ITrunkCapabilities GetCapabilities()
        {
            return _innerTrunk.GetCapabilities();
        }

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
                Console.WriteLine($"⚠️ Failed to decompress nut '{compressedNut.Id}': {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Wrapper for compressed payload data with metadata
    /// </summary>
    public class CompressedNut
    {
        public byte[] CompressedData { get; set; } = Array.Empty<byte>();
        public int OriginalSize { get; set; }
        public int CompressedSize { get; set; }
        public string Algorithm { get; set; } = "";
        public string OriginalType { get; set; } = "";

        /// <summary>
        /// Compression ratio (e.g., 0.5 = 50% of original size)
        /// </summary>
        public double CompressionRatio => OriginalSize > 0
            ? (double)CompressedSize / OriginalSize
            : 1.0;

        /// <summary>
        /// Space saved in bytes
        /// </summary>
        public int SpaceSaved => OriginalSize - CompressedSize;
    }
}
