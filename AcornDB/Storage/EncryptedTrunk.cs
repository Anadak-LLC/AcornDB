using System;
using AcornDB.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AcornDB.Security;
using AcornDB.Storage.Serialization;
using Newtonsoft.Json;

namespace AcornDB.Storage
{
    /// <summary>
    /// [DEPRECATED] Encrypted wrapper for any ITrunk implementation
    /// Encrypts payloads before storage, decrypts on retrieval
    ///
    /// ⚠️ IMPORTANT: This class is DEPRECATED and will be REMOVED in v0.6.0.
    ///
    /// Why this is deprecated:
    /// - Old wrapper pattern creates type system complexity (Nut<T> → Nut<EncryptedNut>)
    /// - Cannot dynamically add/remove encryption at runtime
    /// - Difficult to inspect transformation chain
    /// - Doesn't support policy context or transformation tracking
    ///
    /// Migration to IRoot pattern:
    ///
    /// OLD CODE:
    ///   var baseTrunk = new FileTrunk<EncryptedNut>();
    ///   var encryptedTrunk = new EncryptedTrunk<User>(baseTrunk, AesEncryptionProvider.FromPassword("secret"));
    ///   var tree = new Tree<User>(encryptedTrunk);
    ///
    /// NEW CODE (Option 1 - Direct):
    ///   var trunk = new FileTrunk<User>();
    ///   trunk.AddRoot(new EncryptionRoot(AesEncryptionProvider.FromPassword("secret"), sequence: 200));
    ///   var tree = new Tree<User>(trunk);
    ///
    /// NEW CODE (Option 2 - Fluent):
    ///   var trunk = new FileTrunk<User>()
    ///       .WithEncryption(AesEncryptionProvider.FromPassword("secret"));
    ///   var tree = new Tree<User>(trunk);
    ///
    /// NEW CODE (Option 3 - Acorn Builder):
    ///   var tree = new Acorn<User>()
    ///       .WithEncryption("secret")
    ///       .Sprout();
    ///
    /// NEW CODE (Option 4 - Combined with Compression):
    ///   var tree = new Acorn<User>()
    ///       .WithCompression()      // Sequence 100
    ///       .WithEncryption("secret") // Sequence 200
    ///       .Sprout();
    ///
    /// See ROOT_ARCHITECTURE.md for complete migration guide.
    /// </summary>
    [Obsolete("EncryptedTrunk is deprecated and will be REMOVED in v0.6.0. Use EncryptionRoot with trunk.AddRoot() or trunk.WithEncryption() instead. " +
              "Example: trunk.WithEncryption(AesEncryptionProvider.FromPassword(\"secret\")). See ROOT_ARCHITECTURE.md for migration guide.", true)]
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
                AcornLog.Info($"⚠️ Failed to decrypt nut '{encryptedNut.Id}': {ex.Message}");
                return null;
            }
        }
    }
}
