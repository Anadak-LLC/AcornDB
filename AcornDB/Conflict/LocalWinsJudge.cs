namespace AcornDB.Conflict
{
    /// <summary>
    /// Local-always-wins conflict resolution
    /// Always keeps the local nut and rejects incoming changes
    /// Useful for read-only replicas or when local changes should take precedence
    /// </summary>
    /// <typeparam name="T">The type of object stored in the nut</typeparam>
    public class LocalWinsJudge<T> : IConflictJudge<T>
    {
        public Nut<T> Judge(Nut<T> local, Nut<T> incoming)
        {
            // Local always wins
            return local;
        }
    }
}
