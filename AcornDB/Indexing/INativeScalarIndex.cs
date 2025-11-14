using System.Linq.Expressions;

namespace AcornDB.Indexing
{
    /// <summary>
    /// Native index that supports scalar property indexing via database engine.
    /// Combines INativeIndex capabilities with IScalarIndex query interface.
    /// </summary>
    public interface INativeScalarIndex<T, TProperty> : INativeIndex, IScalarIndex<T, TProperty>
        where T : class
    {
        /// <summary>
        /// The JSON path used to extract the indexed property from the JSON document.
        /// Example: "$.Email" for an email property
        /// </summary>
        string JsonPath { get; }
    }
}
