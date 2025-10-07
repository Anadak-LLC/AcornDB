namespace AcornDB.Conflict
{
    /// <summary>
    /// Interface for pluggable conflict resolution strategies
    /// Determines which nut to keep when two nuts with the same ID conflict during sync
    /// </summary>
    /// <typeparam name="T">The type of object stored in the nut</typeparam>
    public interface IConflictJudge<T>
    {
        /// <summary>
        /// Resolves a conflict between a local nut and an incoming remote nut
        /// </summary>
        /// <param name="local">The nut currently stored locally</param>
        /// <param name="incoming">The incoming nut from remote sync</param>
        /// <returns>The nut that should be kept (either local or incoming)</returns>
        Nut<T> Judge(Nut<T> local, Nut<T> incoming);
    }
}
