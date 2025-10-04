using AcornDB.Sync;

namespace AcornDB
{
    public interface ISyncableCollection<T>
    {
        ChangeSet<T> ExportChanges();
        void ImportChanges(ChangeSet<T> changes);
    }
}
