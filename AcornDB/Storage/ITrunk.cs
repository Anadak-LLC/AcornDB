using System;

namespace AcornDB.Storage;

public interface ITrunk<T>
{
    // Core persistence operations - using consistent tree semantics
    void Stash(string id, Nut<T> nut);
    Nut<T>? Crack(string id);
    void Toss(string id);
    IEnumerable<Nut<T>> CrackAll();

    // Backward compatibility - obsolete methods
    [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
    void Save(string id, Nut<T> nut) => Stash(id, nut);

    [Obsolete("Use Crack() instead. This method will be removed in a future version.")]
    Nut<T>? Load(string id) => Crack(id);

    [Obsolete("Use Toss() instead. This method will be removed in a future version.")]
    void Delete(string id) => Toss(id);

    [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
    IEnumerable<Nut<T>> LoadAll() => CrackAll();

    // Optional: History support (time-travel)
    IReadOnlyList<Nut<T>> GetHistory(string id);

    // Root processors for byte-level transformations
    IReadOnlyList<IRoot> Roots { get; }
    void AddRoot(IRoot root);
    bool RemoveRoot(string name);

    // Optional: Sync/Export support
    IEnumerable<Nut<T>> ExportChanges();
    void ImportChanges(IEnumerable<Nut<T>> incoming);

    // Capabilities metadata
    ITrunkCapabilities Capabilities { get; }
}
