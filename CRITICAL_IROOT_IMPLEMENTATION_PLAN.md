# Critical IRoot Implementation Plan

**Status:** IN PROGRESS
**Priority:** CRITICAL (Blocks compression/encryption for 7 trunks)
**Estimated Effort:** 4-6 hours

---

## Problem

7 production trunk implementations have stub IRoot methods, preventing users from applying compression, encryption, or policy enforcement:

| Trunk | File | Line | Impact |
|-------|------|------|--------|
| MySqlTrunk | `AcornDB.Persistence.RDBMS/MySqlTrunk.cs` | 449 | No compression/encryption |
| PostgreSqlTrunk | `AcornDB.Persistence.RDBMS/PostgreSqlTrunk.cs` | 425 | No compression/encryption |
| SqlServerTrunk | `AcornDB.Persistence.RDBMS/SqlServerTrunk.cs` | 424 | No compression/encryption |
| AzureTableTrunk | `AcornDB.Persistence.Cloud/AzureTableTrunk.cs` | 353 | No compression/encryption |
| DynamoDbTrunk | `AcornDB.Persistence.Cloud/DynamoDbTrunk.cs` | 488 | No compression/encryption |
| ParquetTrunk | `AcornDB.Persistence.DataLake/ParquetTrunk.cs` | 498 | No compression/encryption |
| TieredTrunk | `AcornDB.Persistence.DataLake/TieredTrunk.cs` | 314 | No compression/encryption |

---

## Reference Implementation

**SqliteTrunk** (lines 38-115, 140-200) has full IRoot support. Use as template.

### Required Components:

```csharp
// 1. Field declarations
private readonly List<IRoot> _roots = new();
private readonly object _rootsLock = new();
private readonly ISerializer _serializer;

// 2. Constructor parameter
public XxxTrunk(..., ISerializer? serializer = null)
{
    _serializer = serializer ?? new NewtonsoftJsonSerializer();
}

// 3. IRoot interface implementation
public IReadOnlyList<IRoot> Roots
{
    get
    {
        lock (_rootsLock) { return _roots.ToList(); }
    }
}

public void AddRoot(IRoot root)
{
    if (root == null) throw new ArgumentNullException(nameof(root));
    lock (_rootsLock)
    {
        _roots.Add(root);
        _roots.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
    }
}

public bool RemoveRoot(string name)
{
    lock (_rootsLock)
    {
        var root = _roots.FirstOrDefault(r => r.Name == name);
        if (root != null)
        {
            _roots.Remove(root);
            return true;
        }
        return false;
    }
}

// 4. Write path (in FlushAsync or Stash method)
var json = _serializer.Serialize(nut);
var bytes = Encoding.UTF8.GetBytes(json);

var context = new RootProcessingContext
{
    PolicyContext = new PolicyContext { Operation = "Write" },
    DocumentId = id
};

var processedBytes = bytes;
lock (_rootsLock)
{
    foreach (var root in _roots)
    {
        processedBytes = root.OnStash(processedBytes, context);
    }
}

// Store processedBytes (may need Base64 encoding for text columns)
var dataToStore = Convert.ToBase64String(processedBytes);

// 5. Read path (in Crack method)
// Retrieve data (may need Base64 decoding)
var storedBytes = Convert.FromBase64String(data);

var context = new RootProcessingContext
{
    PolicyContext = new PolicyContext { Operation = "Read" },
    DocumentId = id
};

var processedBytes = storedBytes;
lock (_rootsLock)
{
    for (int i = _roots.Count - 1; i >= 0; i--)
    {
        processedBytes = _roots[i].OnCrack(processedBytes, context);
    }
}

var json = Encoding.UTF8.GetString(processedBytes);
var nut = _serializer.Deserialize<Nut<T>>(json);
```

### Required Imports:

```csharp
using System.Text;
using AcornDB.Policy;
```

---

## Implementation Order

### Phase 1: RDBMS Trunks (Highest Priority)
Most users need these for production databases.

**1. MySqlTrunk** - 2 hours
- Already started (MySqlTrunk.cs:456-490 has IRoot methods)
- Need to update FlushAsync (line 341-410) to process through root chain
- Need to update CrackAsync (line 150-180) to process through root chain
- **Status:** 40% complete

**2. PostgreSqlTrunk** - 1.5 hours
- Nearly identical to MySQL pattern
- Update FlushAsync (line ~350)
- Update CrackAsync (line ~160)

**3. SqlServerTrunk** - 1.5 hours
- Similar pattern to MySQL
- Update FlushAsync (line ~320)
- Update CrackAsync (line ~140)

### Phase 2: Cloud NoSQL Trunks (High Priority)

**4. DynamoDbTrunk** - 2 hours
- More complex: JSON stored in DynamoDB attribute
- Write: Process through roots, store as Base64 in attribute
- Read: Retrieve attribute, decode Base64, process through roots
- Update FlushAsync (line ~220-270)
- Update CrackAsync (line ~120-160)

**5. AzureTableTrunk** - 2 hours
- Similar to DynamoDB pattern
- JSON stored in table entity property
- Update FlushAsync (line ~180-230)
- Update CrackAsync (line ~100-140)

### Phase 3: Data Lake Trunks (Medium Priority)

**6. ParquetTrunk** - 2.5 hours
- Complex: Parquet columnar format
- May need to store processed bytes in BLOB column
- Alternative: Apply roots before Parquet serialization
- Update Write method (line ~140-200)
- Update Read method (line ~220-270)

**7. TieredTrunk** - 1.5 hours
- Wrapper trunk - delegate to hot/cold tier trunks
- If inner trunks have IRoot support, delegate
- Otherwise, apply roots before delegation
- Update Stash (line ~120)
- Update Crack (line ~160)

---

## Testing Plan

For each trunk after implementation:

```csharp
[Fact]
public void XxxTrunk_SupportsCompression()
{
    var trunk = new XxxTrunk(...);
    trunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), 100));

    var nut = new Nut<User> { Id = "test", Payload = new User { Name = "Alice" } };
    trunk.Stash("test", nut);

    var retrieved = trunk.Crack("test");
    Assert.NotNull(retrieved);
    Assert.Equal("Alice", retrieved.Payload.Name);
}

[Fact]
public void XxxTrunk_SupportsEncryption()
{
    var trunk = new XxxTrunk(...);
    trunk.AddRoot(new EncryptionRoot(AesEncryptionProvider.FromPassword("test"), 200));

    var nut = new Nut<User> { Id = "test", Payload = new User { Name = "Secret" } };
    trunk.Stash("test", nut);

    var retrieved = trunk.Crack("test");
    Assert.Equal("Secret", retrieved.Payload.Name);
}

[Fact]
public void XxxTrunk_SupportsCompressionAndEncryption()
{
    var trunk = new XxxTrunk(...);
    trunk.AddRoot(new CompressionRoot(new GzipCompressionProvider(), 100));
    trunk.AddRoot(new EncryptionRoot(AesEncryptionProvider.FromPassword("test"), 200));

    // Verify correct ordering: compress then encrypt
    var nut = new Nut<User> { Id = "test", Payload = new User { Name = "Alice" } };
    trunk.Stash("test", nut);

    var retrieved = trunk.Crack("test");
    Assert.Equal("Alice", retrieved.Payload.Name);
}
```

---

## Storage Considerations

### Text Columns (JSON/VARCHAR)
Most RDBMS trunks store JSON as TEXT/VARCHAR:
- After root processing, bytes may not be valid UTF-8
- **Solution:** Base64 encode processed bytes before storage
- **Impact:** ~33% size increase from Base64, but offset by compression

### Binary Columns (BLOB/BYTEA)
If trunk has BLOB/BYTEA column:
- Store processed bytes directly
- No Base64 encoding needed
- Better storage efficiency

### Column Type Changes Required?
- MySqlTrunk: `json_data` is JSON type → may need LONGTEXT or BLOB
- PostgreSqlTrunk: `json_data` is JSONB type → may need BYTEA
- SqlServerTrunk: `json_data` is NVARCHAR(MAX) → works with Base64
- DynamoDbTrunk: Attribute is JSON → use String attribute with Base64
- AzureTableTrunk: Property is String → works with Base64

**Recommended Approach:**
1. Add new column `payload_blob BLOB` for binary data
2. Keep `json_data` for backward compatibility
3. Use `payload_blob` when roots are present, `json_data` otherwise
4. Migration: Convert existing json_data to payload_blob on first access

---

## Backward Compatibility

### For Existing Data:
```csharp
// Read path with fallback
var data = row["payload_blob"] ?? row["json_data"];
var bytes = row["payload_blob"] != null
    ? (byte[])data  // Binary column
    : Encoding.UTF8.GetBytes((string)data);  // JSON column

// Apply roots only if data is from payload_blob
if (row["payload_blob"] != null && _roots.Count > 0)
{
    // Process through root chain
}
```

### Migration Script:
```sql
-- Add binary column
ALTER TABLE acorn_xxx ADD COLUMN payload_blob BLOB;

-- Future writes go to payload_blob when roots present
-- Old data in json_data still readable
```

---

## Progress Tracking

| Trunk | IRoot Methods | Write Path | Read Path | Tests | Complete |
|-------|---------------|------------|-----------|-------|----------|
| MySqlTrunk | ✅ (40%) | ⬜ | ⬜ | ⬜ | 10% |
| PostgreSqlTrunk | ⬜ | ⬜ | ⬜ | ⬜ | 0% |
| SqlServerTrunk | ⬜ | ⬜ | ⬜ | ⬜ | 0% |
| DynamoDbTrunk | ⬜ | ⬜ | ⬜ | ⬜ | 0% |
| AzureTableTrunk | ⬜ | ⬜ | ⬜ | ⬜ | 0% |
| ParquetTrunk | ⬜ | ⬜ | ⬜ | ⬜ | 0% |
| TieredTrunk | ⬜ | ⬜ | ⬜ | ⬜ | 0% |

**Overall Progress:** 1.4% (1/7 trunks, partial)

---

## Alternative: Wrapper Approach (NOT RECOMMENDED)

Instead of modifying each trunk, could create wrapper:

```csharp
public class RootEnabledTrunk<T> : ITrunk<T>
{
    private readonly ITrunk<T> _inner;
    private readonly List<IRoot> _roots = new();

    // Wrap any trunk with root support
}
```

**Pros:**
- No changes to existing trunks
- Works with any trunk immediately

**Cons:**
- Against architectural direction (moving away from wrappers)
- Creates another abstraction layer
- Performance overhead
- Doesn't solve column type issue (still need binary storage)

**Decision:** Implement natively in each trunk.

---

## Conclusion

This is **critical work** that blocks production use of most trunks with modern IRoot features. The implementation is straightforward (follow SqliteTrunk pattern) but requires careful attention to data encoding and backward compatibility.

**Recommended Action:**
Complete Phase 1 (RDBMS trunks) in next sprint. Phase 2 and 3 can follow in subsequent releases.

**Estimated Timeline:**
- Phase 1 (MySQL, PostgreSQL, SQL Server): 1 week
- Phase 2 (DynamoDB, Azure Table): 1 week
- Phase 3 (Parquet, Tiered): 1 week
- Total: 3 weeks for one developer

---

**Status:** Document created. MySqlTrunk IRoot methods added (40% complete). Write/read path implementation pending.
