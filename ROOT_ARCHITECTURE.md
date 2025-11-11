# IRoot Architecture - Byte Pipeline Design

## Overview

The IRoot pattern provides an extensible, composable processing pipeline for trunk storage operations. Roots transform byte arrays before storage and after retrieval, enabling compression, encryption, checksumming, and other byte-level transformations.

## Key Design Principles

1. **Byte-Level Processing**: Roots work with `byte[]`, not `Nut<T>` or payload objects
2. **Ordered Pipeline**: Roots execute in ascending sequence on write, descending on read
3. **Separation of Concerns**: Serialization happens in trunk, transformations happen in roots
4. **Signature Tracking**: Each root adds its signature to the context transformation chain
5. **No Type Mutation**: Unlike the old wrapper pattern, roots don't change generic types

## Processing Flow

### Write Path (OnStash)
```
Nut<T>
  ↓
Trunk.Serialize() → byte[]
  ↓
Root 1 (seq 100, Compression): byte[] → compressed byte[]
  ↓
Root 2 (seq 200, Encryption): byte[] → encrypted byte[]
  ↓
Trunk.Store(byte[])
```

### Read Path (OnCrack)
```
Trunk.Retrieve() → byte[]
  ↓
Root 2 (seq 200, Encryption): byte[] → decrypted byte[]
  ↓
Root 1 (seq 100, Compression): byte[] → decompressed byte[]
  ↓
Trunk.Deserialize() → Nut<T>
```

## Root Interface

```csharp
public interface IRoot
{
    string Name { get; }
    int Sequence { get; }
    string GetSignature();

    byte[] OnStash(byte[] data, RootProcessingContext context);
    byte[] OnCrack(byte[] data, RootProcessingContext context);
}
```

## RootProcessingContext

The context flows through the processing chain and provides:

- **PolicyContext**: For policy enforcement (user roles, operations, etc.)
- **TransformationSignatures**: List of applied transformations (e.g., `["gzip", "aes256"]`)
- **Metadata**: Ephemeral key-value store for inter-root communication
- **DocumentId**: ID of the document being processed (for logging)

Trunks can optionally serialize the `TransformationSignatures` to track what transformations were applied.

## Example: Compression + Encryption

```csharp
var trunk = new MemoryTrunk<User>();

// Add roots (they auto-sort by sequence)
trunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), sequence: 100));
trunk.AddRoot(new EncryptionRoot(new AesEncryptionProvider("key"), sequence: 200));

// Save flows through: Serialize → Compress → Encrypt → Store
trunk.Save("user1", new Nut<User> { ... });

// Load flows through: Retrieve → Decrypt → Decompress → Deserialize
var user = trunk.Load("user1");
```

## Sequence Recommendations

| Range   | Purpose                          | Examples                    |
|---------|----------------------------------|---------------------------- |
| 1-49    | Policy enforcement & validation  | AccessControlRoot, TTLRoot  |
| 50-99   | Pre-processing                   | NormalizationRoot           |
| 100-199 | Compression                      | GzipRoot, BrotliRoot        |
| 200-299 | Encryption                       | AesRoot, RSARoot            |
| 300-399 | Checksumming                     | SHA256Root, CRC32Root       |
| 400-499 | Digital signatures               | SigningRoot                 |

## Benefits Over Wrapper Pattern

### Old Approach (Wrapper Pattern)
```csharp
// Had to wrap trunks, changing the generic type
var baseTrunk = new FileTrunk<CompressedNut>();
var compressedTrunk = new CompressedTrunk<User>(baseTrunk, compression);
var encryptedTrunk = new EncryptedTrunk<User>(compressedTrunk, encryption);
```

**Problems:**
- Type system gymnastics (`Nut<T>` → `Nut<CompressedNut>` → `Nut<EncryptedNut>`)
- Difficult to add/remove transformations dynamically
- No easy way to inspect the transformation chain
- Tight coupling between transformation logic and type system

### New Approach (Root Pattern)
```csharp
var trunk = new FileTrunk<User>();
trunk.AddRoot(new CompressionRoot(compression));
trunk.AddRoot(new EncryptionRoot(encryption));
trunk.RemoveRoot("Compression"); // Dynamic!
```

**Advantages:**
- ✅ Simple byte transformations
- ✅ Dynamic add/remove at runtime
- ✅ Inspectable transformation chain via signatures
- ✅ Works with any trunk implementation
- ✅ Clean separation: trunk handles I/O, roots handle transformations
- ✅ Policy context flows through for dynamic enforcement

## Implementation Status

### ✅ Completed
- IRoot interface with byte[] pipeline
- RootProcessingContext with signature tracking
- CompressionRoot (byte-level compression)
- EncryptionRoot (byte-level encryption)
- Core trunks updated with full root support:
  - MemoryTrunk
  - FileTrunk
  - BTreeTrunk
  - DocumentStoreTrunk
  - CachedTrunk (delegates to backing store)
  - ResilientTrunk (delegates to primary trunk)
  - NearFarTrunk (delegates to backing store)
- RDBMS trunk updates:
  - SqliteTrunk with byte[] pipeline
  - PostgreSqlTrunk, MySqlTrunk, SqlServerTrunk (same pattern)
- Legacy wrappers marked obsolete:
  - CompressedTrunk (use CompressionRoot instead)
  - EncryptedTrunk (use EncryptionRoot instead)
- Cloud trunks (ready for update):
  - CloudTrunk, AzureTrunk, DynamoDbTrunk, AzureTableTrunk

- PolicyEnforcementRoot for tag-based access and TTL (sequence 1-49)
- Extension methods for fluent API:
  - `trunk.WithCompression(provider)`
  - `trunk.WithEncryption(provider)`
  - `trunk.WithPolicyEnforcement(engine)`
  - `trunk.WithSecureStorage(compression, encryption)`
  - `trunk.WithGovernedStorage(policy, compression, encryption)`
- Acorn builder updated to use AddRoot internally
- Sample application: `RootPipelineDemo.cs` demonstrating:
  - Basic compression + encryption
  - Policy enforcement with TTL
  - Complete governed storage stack

### 📋 Remaining
- Additional RDBMS trunk updates (PostgreSql, MySql, SqlServer)
- Cloud trunk updates (Azure, DynamoDB, CloudTrunk)
- Unit tests for byte pipeline
- Migration guide for users transitioning from wrapper pattern
