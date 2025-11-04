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
    [Obsolete("EncryptedTrunk is obsolete. Use IRoot pattern instead.")]
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

        public void Stash(string id, Nut<T> nut)
        {
            var encrypted = EncryptNut(nut);
            _innerTrunk.Stash(id, encrypted);
        }

        [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
        public void Save(string id, Nut<T> nut) => Stash(id, nut);

        public Nut<T>? Crack(string id)
        {
            var encrypted = _innerTrunk.Crack(id);
            if (encrypted == null) return null;
            return DecryptNut(encrypted);
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
                .Select(DecryptNut)
                .Where(n => n != null)!;
        }

        [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
        public IEnumerable<Nut<T>> LoadAll() => CrackAll();

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

        // Delegate to inner trunk's capabilities
        public ITrunkCapabilities Capabilities => _innerTrunk.Capabilities;

        // IRoot interface members - obsolete trunk pattern
        public IReadOnlyList<IRoot> Roots => Array.Empty<IRoot>();
        public void AddRoot(IRoot root) => throw new NotSupportedException("EncryptedTrunk is obsolete. Use IRoot pattern instead.");
        public bool RemoveRoot(string name) => false;

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
