using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AcornDB.Security;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// Encrypted wrapper for any ITrunk implementation
    /// Encrypts payloads before storage, decrypts on retrieval
    /// </summary>
    public class EncryptedTrunk<T> : ITrunk<T>
    {
        private readonly ITrunk<EncryptedNut> _innerTrunk;
        private readonly IEncryptionProvider _encryption;
        private readonly ISerializer _serializer;

        public EncryptedTrunk(ITrunk<EncryptedNut> innerTrunk, IEncryptionProvider encryption, ISerializer? serializer = null)
        {
            _innerTrunk = innerTrunk ?? throw new ArgumentNullException(nameof(innerTrunk));
            _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
            _serializer = serializer ?? new NewtonsoftJsonSerializer();
        }

        public void Save(string id, Nut<T> nut)
        {
            var encrypted = EncryptNut(nut);
            _innerTrunk.Save(id, encrypted);
        }

        public Nut<T>? Load(string id)
        {
            var encrypted = _innerTrunk.Load(id);
            if (encrypted == null) return null;
            return DecryptNut(encrypted);
        }

        public void Delete(string id)
        {
            _innerTrunk.Delete(id);
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            return _innerTrunk.LoadAll()
                .Select(DecryptNut)
                .Where(n => n != null)!;
        }

        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            var encryptedHistory = _innerTrunk.GetHistory(id);
            return encryptedHistory
                .Select(DecryptNut)
                .Where(n => n != null)
                .ToList()!;
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return _innerTrunk.ExportChanges()
                .Select(DecryptNut)
                .Where(n => n != null)!;
        }

        public void ImportChanges(IEnumerable<Nut<T>> changes)
        {
            var encrypted = changes.Select(EncryptNut);
            _innerTrunk.ImportChanges(encrypted);
        }

        public ITrunkCapabilities GetCapabilities()
        {
            // Delegate to inner trunk's capabilities
            return _innerTrunk.GetCapabilities();
        }

        private Nut<EncryptedNut> EncryptNut(Nut<T> nut)
        {
            var json = _serializer.Serialize(nut.Payload);
            var encrypted = _encryption.Encrypt(json);

            return new Nut<EncryptedNut>
            {
                Id = nut.Id,
                Payload = new EncryptedNut
                {
                    EncryptedData = encrypted,
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

        private Nut<T>? DecryptNut(Nut<EncryptedNut> encryptedNut)
        {
            try
            {
                var decrypted = _encryption.Decrypt(encryptedNut.Payload.EncryptedData);
                var payload = _serializer.Deserialize<T>(decrypted);

                return new Nut<T>
                {
                    Id = encryptedNut.Id,
                    Payload = payload,
                    Timestamp = encryptedNut.Timestamp,
                    ExpiresAt = encryptedNut.ExpiresAt,
                    Version = encryptedNut.Version,
                    ChangeId = encryptedNut.ChangeId,
                    OriginNodeId = encryptedNut.OriginNodeId,
                    HopCount = encryptedNut.HopCount
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to decrypt nut '{encryptedNut.Id}': {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Wrapper for encrypted payload data
    /// </summary>
    public class EncryptedNut
    {
        public string EncryptedData { get; set; } = "";
        public string OriginalType { get; set; } = "";
    }
}
