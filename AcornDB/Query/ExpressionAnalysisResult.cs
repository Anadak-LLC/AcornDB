using System.Collections.Generic;

namespace AcornDB.Query
{
    /// <summary>
    /// Result of analyzing a WHERE expression
    /// </summary>
    public class ExpressionAnalysisResult
    {
        public bool IsIndexable { get; set; }
        public List<IndexableCondition> Conditions { get; set; } = new List<IndexableCondition>();
    }
}
