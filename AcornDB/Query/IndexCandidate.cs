using AcornDB.Indexing;

namespace AcornDB.Query
{
    /// <summary>
    /// Candidate index considered during planning
    /// </summary>
    public class IndexCandidate
    {
        /// <summary>
        /// The index being considered
        /// </summary>
        public IIndex Index { get; set; } = null!;

        /// <summary>
        /// Estimated cost if this index is used
        /// </summary>
        public double EstimatedCost { get; set; }

        /// <summary>
        /// Why this index was/wasn't selected
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Can this index satisfy the WHERE clause?
        /// </summary>
        public bool CanSatisfyWhere { get; set; }

        /// <summary>
        /// Can this index satisfy the ORDER BY clause?
        /// </summary>
        public bool CanSatisfyOrderBy { get; set; }
    }
}
