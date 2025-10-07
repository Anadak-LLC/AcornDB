namespace AcornDB.Conflict
{
    /// <summary>
    /// Version-based conflict resolution
    /// Keeps the nut with the higher version number
    /// Useful when you have explicit versioning in your application
    /// </summary>
    /// <typeparam name="T">The type of object stored in the nut</typeparam>
    public class VersionJudge<T> : IConflictJudge<T>
    {
        public Nut<T> Judge(Nut<T> local, Nut<T> incoming)
        {
            // Higher version wins
            if (incoming.Version > local.Version)
                return incoming;

            if (local.Version > incoming.Version)
                return local;

            // If versions are equal, fall back to timestamp
            return incoming.Timestamp > local.Timestamp ? incoming : local;
        }
    }
}
