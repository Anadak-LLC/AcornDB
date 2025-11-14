using System;

namespace AcornDB.Policy.BuiltInRules
{
    /// <summary>
    /// CORE POLICY: Built-in TTL (Time-To-Live) enforcement policy.
    /// Checks for ExpiresAt or TTL properties and marks expired entities for deletion.
    /// Part of AcornDB.Core - lightweight, dependency-free enforcement.
    /// </summary>
    internal class TtlPolicyRule : IPolicyRule
    {
        public string Name => "TTL_Enforcement";
        public string Description => "Enforces Time-To-Live on entities with expiration timestamps";
        public int Priority => 100;

        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
        {
            if (entity == null)
                return PolicyEvaluationResult.Success();

            // Check if entity has TTL properties
            var type = typeof(T);
            var expiresAtProp = type.GetProperty("ExpiresAt") ?? type.GetProperty("TTL");

            if (expiresAtProp != null && expiresAtProp.PropertyType == typeof(DateTime?))
            {
                var expiresAt = (DateTime?)expiresAtProp.GetValue(entity);

                if (expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow)
                {
                    var result = PolicyEvaluationResult.Failure($"Entity expired at {expiresAt.Value}");
                    result.Actions.Add("DELETE:Expired");
                    return result;
                }
            }

            return PolicyEvaluationResult.Success();
        }
    }
}
