namespace AcornDB.Policy
{
    /// <summary>
    /// Represents a custom policy rule that can be applied to entities
    /// </summary>
    public interface IPolicyRule
    {
        /// <summary>
        /// Unique name of the policy
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of what the policy enforces
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Priority for policy execution (higher = earlier execution)
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Evaluate the policy against an entity
        /// </summary>
        /// <typeparam name="T">Entity type</typeparam>
        /// <param name="entity">Entity to evaluate</param>
        /// <param name="context">Policy execution context</param>
        /// <returns>Result of policy evaluation</returns>
        PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context);
    }
}
