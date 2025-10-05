namespace AcornDB.Storage;

public interface ITrunk<T>
{
    void Save(string id, NutShell<T> shell);
    NutShell<T>? Load(string id);
    void Delete(string id);
    IEnumerable<NutShell<T>> LoadAll();
}