namespace AcornDB.Storage;

public interface ITrunk<T>
{
    // Core persistence operations
    void Save(string id, Nut<T> nut);
    Nut<T>? Load(string id);
    void Delete(string id);
    IEnumerable<Nut<T>> LoadAll();

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
