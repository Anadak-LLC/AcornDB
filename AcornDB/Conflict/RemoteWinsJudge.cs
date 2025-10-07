namespace AcornDB.Conflict
{
    /// <summary>
    /// Remote-always-wins conflict resolution
    /// Always keeps the incoming remote nut and discards local changes
    /// Useful for scenarios where remote source is authoritative
    /// </summary>
    /// <typeparam name="T">The type of object stored in the nut</typeparam>
    public class RemoteWinsJudge<T> : IConflictJudge<T>
    {
        public Nut<T> Judge(Nut<T> local, Nut<T> incoming)
        {
            // Remote always wins
            return incoming;
        }
    }
}
