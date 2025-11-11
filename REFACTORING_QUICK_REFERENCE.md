# AcornDB Refactoring Quick Reference

## 9 Opportunities at a Glance

### HIGH PRIORITY (Do First - Week 1)

#### 1. TrunkWrapperBase<T>
- **What:** Abstract base for wrapper trunks
- **Saves:** 200-240 lines across 4 classes
- **Time:** 3-4 hours
- **Risk:** LOW
- **Classes:** CachedTrunk, ResilientTrunk, NearFarTrunk, AzureTrunk

#### 2. WriteBatchHelper<T>
- **What:** Reusable batching helper
- **Saves:** 480-640 lines across 8 classes
- **Time:** 4-5 hours
- **Risk:** LOW
- **Classes:** BTreeTrunk, DocumentStoreTrunk, SqliteTrunk, MySqlTrunk, PostgreSqlTrunk, CloudTrunk, AzureTableTrunk, DynamoDbTrunk

#### 3. RdbmsTrunkBase<T>
- **What:** Shared RDBMS initialization
- **Saves:** 240-300 lines across 3 classes
- **Time:** 6-8 hours
- **Risk:** MEDIUM
- **Classes:** SqliteTrunk, MySqlTrunk, PostgreSqlTrunk

---

### MEDIUM PRIORITY (Do Second - Week 2)

#### 4. AsyncSyncBridge
- **What:** Helper for async-to-sync calls
- **Saves:** 300-360 lines (implicit) across 6+ classes
- **Time:** 2-3 hours
- **Risk:** VERY LOW
- **Pattern:** Replace `GetAwaiter().GetResult()` with helper

#### 5. ConcurrentDictionary for Tree<T>
- **What:** Thread-safe cache modernization
- **Saves:** 40-50 lock blocks
- **Time:** 3-4 hours
- **Risk:** MEDIUM
- **Benefit:** Lock-free reads, better performance

#### 6. Capabilities Helper (Included in #1)
- **What:** Centralize capabilities delegation
- **Saves:** 120-160 lines
- **Time:** Included in TrunkWrapperBase<T>
- **Risk:** VERY LOW

---

### LOW PRIORITY (Nice to Have - Week 3)

#### 7. Disposal Pattern (Included in #1)
- **What:** Centralize disposal logic
- **Saves:** 80-120 lines
- **Time:** Included in TrunkWrapperBase<T>
- **Risk:** VERY LOW

#### 8. SerializerBase
- **What:** Abstract base for serializers
- **Saves:** 50-70 lines per new serializer
- **Time:** 1-2 hours
- **Risk:** LOW
- **Benefit:** Consistent validation

#### 9. TrunkBuilderExtensions
- **What:** Consolidate 3 extension files
- **Saves:** ~285 lines
- **Time:** 1 hour
- **Risk:** VERY LOW
- **Benefit:** Single place to find all builder methods

---

## Implementation Checklist

### Phase 1: Foundation (Week 1)

- [ ] Create TrunkWrapperBase<T>
  - [ ] Write abstract class with all delegation methods
  - [ ] Add CreateCapabilities() helper
  - [ ] Add Dispose() pattern
  - [ ] Write unit tests for base class

- [ ] Refactor CachedTrunk
  - [ ] Inherit from TrunkWrapperBase<T>
  - [ ] Update capabilities property
  - [ ] Run tests

- [ ] Refactor ResilientTrunk
  - [ ] Inherit from TrunkWrapperBase<T>
  - [ ] Keep custom logic intact
  - [ ] Run tests

- [ ] Refactor NearFarTrunk
  - [ ] Inherit from TrunkWrapperBase<T>
  - [ ] Run tests

- [ ] Refactor AzureTrunk
  - [ ] Inherit from TrunkWrapperBase<T>
  - [ ] Run tests

- [ ] Create WriteBatchHelper<T>
  - [ ] Write helper class with batching logic
  - [ ] Support both sync and async flush
  - [ ] Write unit tests

- [ ] Refactor BTreeTrunk
  - [ ] Use WriteBatchHelper<T>
  - [ ] Remove _writeBuffer, _writeLock, _flushTimer
  - [ ] Update FlushAsync() to use helper
  - [ ] Run full test suite

- [ ] Refactor DocumentStoreTrunk
  - [ ] Use WriteBatchHelper<T>
  - [ ] Run tests

- [ ] Week 1 Complete: ~1,000-1,200 lines eliminated

### Phase 2: Database Layer (Week 2)

- [ ] Create RdbmsTrunkBase<T>
  - [ ] Extract common initialization
  - [ ] Move timer/batching to base
  - [ ] Define abstract methods
  - [ ] Write unit tests

- [ ] Refactor SqliteTrunk
  - [ ] Inherit from RdbmsTrunkBase<T>
  - [ ] Implement abstract methods
  - [ ] Run SQLite integration tests
  - [ ] Benchmark vs original

- [ ] Refactor MySqlTrunk
  - [ ] Inherit from RdbmsTrunkBase<T>
  - [ ] Run MySQL integration tests
  - [ ] Benchmark

- [ ] Refactor PostgreSqlTrunk
  - [ ] Inherit from RdbmsTrunkBase<T>
  - [ ] Run PostgreSQL integration tests
  - [ ] Benchmark

- [ ] Create AsyncSyncBridge helper
  - [ ] Write static helper class
  - [ ] Write unit tests
  - [ ] Document usage

- [ ] Update CloudTrunk
  - [ ] Replace GetAwaiter().GetResult() with helper
  - [ ] Run tests

- [ ] Update AzureTableTrunk
  - [ ] Replace GetAwaiter().GetResult() with helper
  - [ ] Run tests

- [ ] Update DynamoDbTrunk
  - [ ] Replace GetAwaiter().GetResult() with helper
  - [ ] Run tests

- [ ] Week 2 Complete: ~500-600 additional lines eliminated

### Phase 3: Polish (Week 3)

- [ ] Convert Tree<T> cache to ConcurrentDictionary
  - [ ] Audit all access patterns
  - [ ] Convert to ConcurrentDictionary
  - [ ] Add concurrent access tests
  - [ ] Benchmark lock vs lockfree

- [ ] Consolidate extension methods
  - [ ] Create TrunkBuilderExtensions.cs
  - [ ] Move methods from 3 files
  - [ ] Update using statements
  - [ ] Run all builder pattern tests

- [ ] Create SerializerBase (if time)
  - [ ] Write abstract base class
  - [ ] Add validation logic
  - [ ] Document for future serializers

- [ ] Update documentation
  - [ ] Add new patterns to architecture docs
  - [ ] Document base classes
  - [ ] Update contributor guide

- [ ] Final cleanup
  - [ ] Run full test suite (100% coverage required)
  - [ ] Performance benchmark baseline vs final
  - [ ] Code review of all changes
  - [ ] Update CHANGELOG

---

## Commands to Run

```bash
# Week 1 Testing
dotnet test AcornDB.Test /p:CollectCoverage=true

# Week 2 Database Testing
dotnet test AcornDB.Test --filter "SQLite" /p:CollectCoverage=true
dotnet test AcornDB.Test --filter "MySQL" /p:CollectCoverage=true
dotnet test AcornDB.Test --filter "PostgreSQL" /p:CollectCoverage=true

# Final Benchmarking
dotnet run -c Release --project AcornDB.Benchmarks \
  --filter "*Competitive*"

# Static Analysis
dotnet build /p:EnforceCodeStyleInBuild=true
```

---

## Validation Criteria

Each refactoring must satisfy:

1. **Behavior:** No code path should change
2. **Tests:** 100% passing, no new test modifications needed
3. **Performance:** Benchmarks show no regression
4. **Code Quality:** Complexity metrics stay same or improve
5. **Documentation:** Comments updated with new patterns

---

## File Changes Summary

### New Files Created
- TrunkWrapperBase.cs
- WriteBatchHelper.cs
- AsyncSyncBridge.cs
- RdbmsTrunkBase.cs (optional intermediate)
- SerializerBase.cs (optional)

### Files Modified (Refactored)
- CachedTrunk.cs
- ResilientTrunk.cs
- NearFarTrunk.cs
- AzureTrunk.cs
- BTreeTrunk.cs
- DocumentStoreTrunk.cs
- SqliteTrunk.cs
- MySqlTrunk.cs
- PostgreSqlTrunk.cs
- CloudTrunk.cs
- AzureTableTrunk.cs
- DynamoDbTrunk.cs
- Tree.cs (and partials)

### Files Consolidated
- TrunkExtensions.cs (consolidate)
- ResilienceExtensions.cs (consolidate)
- CachingExtensions.cs (consolidate)
- → TrunkBuilderExtensions.cs

---

## FAQ

**Q: Can we do these refactorings incrementally?**  
A: Yes! Each refactoring is independent. Do them one at a time, run full tests after each.

**Q: What if a refactoring introduces a bug?**  
A: Revert that specific refactoring and debug. Other refactorings proceed normally.

**Q: Should we include RdbmsTrunkBase<T> for SQL Server?**  
A: Recommend deferring SQL Server until it's needed, but design RdbmsTrunkBase<T> to support it.

**Q: How long does full test suite take?**  
A: ~15-20 minutes on modern hardware. Run before committing each change.

**Q: Do we need to update v0.6.0 release notes?**  
A: No - these are internal refactorings. No API changes from user perspective.

---

## Resources

- **Full Analysis:** REFACTORING_OPPORTUNITIES_ANALYSIS.md
- **Summary:** REFACTORING_SUMMARY.md  
- **Code Examples:** Included in full analysis
- **TrunkBase<T> Precedent:** Check features/propagation_enhancements branch

---

**Last Updated:** November 6, 2024
