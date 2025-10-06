namespace AcornDB.Storage;

public interface ITrunk<T>
{
    // Core persistence operations
    void Save(string id, NutShell<T> shell);
    NutShell<T>? Load(string id);
    void Delete(string id);
    IEnumerable<NutShell<T>> LoadAll();

    // Optional: History support (time-travel)
    IReadOnlyList<NutShell<T>> GetHistory(string id);

    // Optional: Sync/Export support
    IEnumerable<NutShell<T>> ExportChanges();
    void ImportChanges(IEnumerable<NutShell<T>> incoming);
}