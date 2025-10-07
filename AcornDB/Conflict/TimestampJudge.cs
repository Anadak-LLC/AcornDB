namespace AcornDB.Conflict
{
    /// <summary>
    /// Last-write-wins conflict resolution based on timestamp
    /// This is the default conflict resolution strategy
    /// </summary>
    /// <typeparam name="T">The type of object stored in the nut</typeparam>
    public class TimestampJudge<T> : IConflictJudge<T>
    {
        public Nut<T> Judge(Nut<T> local, Nut<T> incoming)
        {
            // Last write wins - keep the nut with the most recent timestamp
            return incoming.Timestamp > local.Timestamp ? incoming : local;
        }
    }
}
