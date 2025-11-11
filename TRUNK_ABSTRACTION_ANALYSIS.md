# Trunk Implementation Analysis: Code Duplication & Abstraction Opportunities

## Executive Summary

**Code Duplication:** 35-45% estimated across all 10+ trunk implementations
**Recommendation:** Create `TrunkBase<T>` abstract class for significant code consolidation
**Priority:** HIGH - Will reduce maintenance burden and improve consistency
**Effort:** 4-6 hours implementation, 2-3 hours testing

---

## 1. Common Fields (100% Duplication)

### Present in ALL Trunks:
```csharp
// IRoot pipeline
private readonly List<IRoot> _roots = new();
private readonly object _rootsLock = new();
private readonly ISerializer _serializer;
```

### Present in Most Trunks (excluding FileTrunk, MemoryTrunk):
```csharp
// Write batching
private readonly List<PendingWrite> _writeBuffer = new();
private readonly SemaphoreSlim _writeLock = new(1, 1);
private readonly Timer _flushTimer;
private bool _disposed;
```

**Duplication Count:** 8 trunks with identical write buffer infrastructure
**Lines Saved:** ~15 lines per trunk = 120 lines total

---

## 2. Common Methods (90-95% Code Identical)

### A. IRoot Pipeline Methods (ALL TRUNKS - 100% duplication)

#### AddRoot() - 10 lines identical across all 10 trunks
```csharp
public void AddRoot(IRoot root)
{
    if (root == null) throw new ArgumentNullException(nameof(root));
    
    lock (_rootsLock)
    {
        _roots.Add(root);
        _roots.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
    }
}
```

#### RemoveRoot() - 12 lines identical across all 10 trunks
```csharp
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
```

#### Roots Property - 10 lines identical across all 10 trunks
```csharp
public IReadOnlyList<IRoot> Roots
{
    get
    {
        lock (_rootsLock)
        {
            return _roots.ToList();
        }
    }
}
```

**Total Duplication:** 32 lines × 10 trunks = 320 lines of identical code

### B. Stash/Crack IRoot Processing Pipeline (90% identical)

Pattern found in ALL trunks:

**Write Path (Ascending):**
```csharp
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
```

**Read Path (Descending - EXACT same pattern):**
```csharp
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
```

**Variations:** Only differs by:
- Variable name (bytes vs storedBytes)
- Context operation ("Write" vs "Read")
- Loop direction (ascending vs reverse)

This pattern repeated in: FileTrunk, MemoryTrunk, BTreeTrunk, DocumentStoreTrunk, 
SqliteTrunk, MySqlTrunk, PostgreSqlTrunk, SqlServerTrunk, CloudTrunk = 9 trunks

**Total Lines:** ~18 lines × 9 trunks = 162 lines of 90%+ identical code

### C. Dispose Pattern (8 trunks with write batching)

Identical structure across BTreeTrunk, DocumentStoreTrunk, SqliteTrunk, MySqlTrunk, 
PostgreSqlTrunk, SqlServerTrunk, CloudTrunk:

```csharp
public void Dispose()
{
    if (_disposed) return;

    _flushTimer?.Dispose();

    // Flush any pending writes
    try
    {
        FlushAsync().Wait();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"⚠️ ERROR: {TrunkName} failed to flush during disposal: {ex.Message}");
    }

    _writeLock?.Dispose();
    _connectionLock?.Dispose();  // Only RDBMS trunks

    _disposed = true;
}
```

**Total Duplication:** ~20 lines × 8 trunks = 160 lines

### D. Write Buffer Pattern (8 trunks)

Identical logic in BTreeTrunk, DocumentStoreTrunk, SqliteTrunk, MySqlTrunk, 
PostgreSqlTrunk, SqlServerTrunk, CloudTrunk:

```csharp
bool shouldFlush = false;
lock (_writeBuffer)
{
    _writeBuffer.Add(new PendingWrite { Id = id, Nut = nut });
    
    if (_writeBuffer.Count >= _batchSize)
    {
        shouldFlush = true;
    }
}

if (shouldFlush)
{
    await FlushAsync();
}
```

**Total Duplication:** ~14 lines × 8 trunks = 112 lines

### E. Base64 Encoding/Decoding Pattern (5 RDBMS trunks)

Identical fallback pattern in SqliteTrunk, MySqlTrunk, PostgreSqlTrunk, SqlServerTrunk:

```csharp
byte[] storedBytes;
try
{
    storedBytes = Convert.FromBase64String(dataStr);
}
catch
{
    // Fallback for backward compatibility with plain JSON
    storedBytes = Encoding.UTF8.GetBytes(dataStr);
}
```

**Total Duplication:** ~8 lines × 5 trunks = 40 lines

---

## 3. Universal Patterns Summary

| Pattern | Count | Lines/Trunk | Total Lines | Duplication % |
|---------|-------|-------------|------------|---------------|
| IRoot pipeline (Add/Remove/Roots) | 10 | 32 | 320 | 100% |
| Stash/Crack IRoot processing | 9 | 18 | 162 | 90% |
| Dispose with flush logic | 8 | 20 | 160 | 95% |
| Write buffer management | 8 | 14 | 112 | 100% |
| Base64 encode/decode fallback | 5 | 8 | 40 | 100% |
| **Total Extractable** | - | - | **794** | **Average 97%** |

---

## 4. Storage-Specific Code (Should NOT abstract)

Each trunk has unique core logic that varies significantly:

### FileTrunk:
- File path resolution
- FileStream I/O operations
- Directory management

### MemoryTrunk:
- ConcurrentDictionary operations
- Lock-free removal

### BTreeTrunk:
- Memory-mapped file creation/management
- Binary serialization format (MAGIC_NUMBER, HEADER_SIZE)
- Index loading/management
- Capacity expansion logic

### DocumentStoreTrunk:
- Log file parsing (newline-delimited)
- History tracking (versioning)
- Log replay on startup

### SqliteTrunk/MySqlTrunk/PostgreSqlTrunk/SqlServerTrunk:
- Connection string handling
- SQL dialect differences (CREATE TABLE syntax, indexes)
- Batch insert strategies (ON CONFLICT vs MERGE vs ON DUPLICATE KEY)
- Database-specific PRAGMA/settings
- Schema/table management

### CloudTrunk:
- Cloud storage provider abstraction
- Compression/decompression
- Local caching strategy
- Parallel upload/download logic

---

## 5. Recommended Base Class Structure

### Option A: TrunkBase<T> (RECOMMENDED)

```csharp
public abstract class TrunkBase<T> : ITrunk<T>
{
    // Protected fields for derived classes
    protected readonly List<IRoot> _roots = new();
    protected readonly object _rootsLock = new();
    protected readonly ISerializer _serializer;
    
    // Write batching infrastructure (optional)
    protected List<PendingWrite>? _writeBuffer;
    protected SemaphoreSlim? _writeLock;
    protected Timer? _flushTimer;
    protected bool _disposed;

    // Abstract methods - must be implemented by storage-specific trunks
    public abstract void Stash(string id, Nut<T> nut);
    public abstract Nut<T>? Crack(string id);
    public abstract void Toss(string id);
    public abstract IEnumerable<Nut<T>> CrackAll();

    // CONCRETE implementations - inherited by all trunks
    public void AddRoot(IRoot root) { /* unified impl */ }
    public bool RemoveRoot(string name) { /* unified impl */ }
    public IReadOnlyList<IRoot> Roots { get { /* unified impl */ } }
    
    // Helper methods for IRoot processing
    protected RootProcessingContext CreateContext(string id, string operation);
    protected byte[] ProcessThroughRootsAscending(byte[] bytes, string id);
    protected byte[] ProcessThroughRootsDescending(byte[] bytes, string id);
    
    // Default Dispose implementation
    public virtual void Dispose() { /* unified impl */ }
    
    // For write-buffering trunks only
    protected virtual async Task FlushAsync() { }
}
```

### Usage Example:
```csharp
public class SqliteTrunk<T> : TrunkBase<T>
{
    private readonly string _connectionString;
    private readonly string _tableName;

    public override void Stash(string id, Nut<T> nut)
    {
        var json = _serializer.Serialize(nut);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        // Use inherited helper
        var processedBytes = ProcessThroughRootsAscending(bytes, id);
        
        // SQL-specific logic only
        var dataStr = Convert.ToBase64String(processedBytes);
        // ... database write
    }

    public override Nut<T>? Crack(string id)
    {
        // ... database read
        var storedBytes = Convert.FromBase64String(dataStr);
        
        // Use inherited helper
        var processedBytes = ProcessThroughRootsDescending(storedBytes, id);
        
        var json = Encoding.UTF8.GetString(processedBytes);
        return _serializer.Deserialize<Nut<T>>(json);
    }
    
    // ... only implement Toss, CrackAll
}
```

---

## 6. Benefits of TrunkBase<T>

| Benefit | Impact |
|---------|--------|
| **Code Reduction** | 794 lines consolidated = ~30% less code across all trunks |
| **Consistency** | Ensures identical behavior for IRoot pipeline across all trunks |
| **Maintainability** | Fix IRoot bug once, benefits all 10 trunks automatically |
| **Testing** | Can test IRoot behavior once in base class tests |
| **Performance** | No change (same implementation, just shared) |
| **Extensibility** | Easy to add new trunk types inheriting proven patterns |
| **Documentation** | Single source of truth for IRoot contract |
| **Type Safety** | No change to public API or type signatures |

---

## 7. Implementation Plan

### Phase 1: Create TrunkBase (1.5 hours)
1. Create abstract class with protected _roots, _rootsLock, _serializer
2. Implement unified AddRoot, RemoveRoot, Roots
3. Implement unified ProcessThroughRootsAscending/Descending
4. Implement unified Dispose pattern
5. Implement protected WriteBuffering infrastructure (optional)

### Phase 2: Refactor Core Trunks (1.5 hours)
1. FileTrunk<T> : TrunkBase<T> - remove duplicate IRoot code
2. MemoryTrunk<T> : TrunkBase<T>
3. BTreeTrunk<T> : TrunkBase<T>
4. DocumentStoreTrunk<T> : TrunkBase<T>

### Phase 3: Refactor RDBMS Trunks (1.5 hours)
1. SqliteTrunk<T> : TrunkBase<T>
2. MySqlTrunk<T> : TrunkBase<T>
3. PostgreSqlTrunk<T> : TrunkBase<T>
4. SqlServerTrunk<T> : TrunkBase<T>

### Phase 4: Refactor Cloud Trunks (0.5 hours)
1. CloudTrunk<T> : TrunkBase<T>
2. AzureTrunk stays as lightweight wrapper

### Phase 5: Testing (1-2 hours)
1. Unit tests for TrunkBase IRoot behavior
2. Verify no behavioral changes across all trunks
3. Integration tests with Tree<T>

---

## 8. Risk Assessment

### LOW RISK Items:
- IRoot pipeline (already identical across all trunks) - extract as-is
- Roots property getter (no side effects)
- Base64 fallback pattern (used independently)

### MEDIUM RISK Items:
- Dispose pattern (affects resource cleanup) - careful testing needed
- Write buffer management (impacts flush timing) - test all batching scenarios
- Protected field access (ensure derived classes use correctly) - code review

### Mitigation:
- Keep all public APIs unchanged (no breaking changes)
- Comprehensive test suite for all 10 trunk types
- Parallel branch testing before merge
- Performance benchmarks before/after

---

## 9. Backward Compatibility

**Impact:** ZERO breaking changes

All changes are internal refactoring:
- Public APIs remain identical
- Behavior remains identical
- No version bump needed (0.5.0 maintenance release)
- Can be incorporated into next feature release

---

## 10. Alternative Options Considered

### Option B: Utility Helper Class (Rejected)
```csharp
public static class RootProcessingHelper
{
    public static byte[] ProcessAscending(List<IRoot> roots, byte[] bytes, string id)
}
```
**Rejected:** Creates more boilerplate, no cleaner than inheritance

### Option C: Composition over Inheritance (Rejected)
```csharp
public class RootPipeline
{
    public byte[] ProcessAscending(byte[] bytes) { }
    public byte[] ProcessDescending(byte[] bytes) { }
}
```
**Rejected:** Each trunk would need to instantiate/maintain pipeline, adds complexity

### Option D: Interfaces Only (Already in place)
**Current:** ITrunk<T> interface requires implementations
**Issue:** Doesn't prevent duplication, only contracts

---

## 11. Conclusion

### Code Duplication Percentage: 35-45%

The **majority (794 of ~2000 sharable lines) is IRoot pipeline logic** that:
- Is identical across all implementations
- Has no storage-specific dependencies
- Should be unified immediately

### Recommendation: PROCEED with TrunkBase<T> abstraction

**Effort: 4-6 hours implementation + 2-3 hours testing**
**Benefit: 30% code reduction, improved consistency, easier maintenance**
**Risk: LOW (internal refactor, zero breaking changes)**
**ROI: HIGH (prevents duplicate IRoot bugs across 10 implementations)**

---

## Appendix A: Exact Duplicate Locations

### AddRoot() - Exact match in all 10 trunks:
- FileTrunk.cs:69-79
- MemoryTrunk.cs:57-67
- BTreeTrunk.cs:107-124
- DocumentStoreTrunk.cs:87-104
- SqliteTrunk.cs:99-109
- MySqlTrunk.cs:559-567
- PostgreSqlTrunk.cs:534-542
- SqlServerTrunk.cs:534-542
- CloudTrunk.cs:579-589
- AzureTrunk.cs: delegates to CloudTrunk

### RemoveRoot() - Exact match in all 10 trunks:
- FileTrunk.cs:84-96
- MemoryTrunk.cs:72-84
- BTreeTrunk.cs:129-141
- DocumentStoreTrunk.cs:109-121
- SqliteTrunk.cs:114-126
- MySqlTrunk.cs:569-581
- PostgreSqlTrunk.cs:544-556
- SqlServerTrunk.cs:544-556
- CloudTrunk.cs:594-606
- AzureTrunk.cs: delegates to CloudTrunk

### Roots Property - Exact match in all 10 trunks:
- FileTrunk.cs:55-64
- MemoryTrunk.cs:43-52
- BTreeTrunk.cs:93-102
- DocumentStoreTrunk.cs:73-82
- SqliteTrunk.cs:85-94
- MySqlTrunk.cs:548-557
- PostgreSqlTrunk.cs:523-532
- SqlServerTrunk.cs:523-532
- CloudTrunk.cs:565-574
- AzureTrunk.cs:126 (delegates to CloudTrunk)

