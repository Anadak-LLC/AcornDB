using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AcornDB.Policy.BuiltInRules
{
    /// <summary>
    /// CORE POLICY: Built-in tag-based access control policy.
    /// Validates entity access based on role-to-tag permissions.
    /// Part of AcornDB.Core - lightweight, dependency-free enforcement.
    /// </summary>
    internal class TagAccessPolicyRule : IPolicyRule
    {
        private readonly ConcurrentDictionary<string, HashSet<string>> _tagPermissions;

        public TagAccessPolicyRule(ConcurrentDictionary<string, HashSet<string>> tagPermissions)
        {
            _tagPermissions = tagPermissions;
        }

        public string Name => "Tag_Access_Control";
        public string Description => "Enforces role-based access control via entity tags";
        public int Priority => 90;

        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
        {
            if (entity == null || context.UserRole == null)
                return PolicyEvaluationResult.Success();

            if (entity is IPolicyTaggable taggable && taggable.Tags.Any())
            {
                foreach (var tag in taggable.Tags)
                {
                    if (_tagPermissions.TryGetValue(tag, out var allowedRoles))
                    {
                        if (allowedRoles.Contains(context.UserRole) || allowedRoles.Contains("*"))
                        {
                            return PolicyEvaluationResult.Success($"Access granted via tag: {tag}");
                        }
                    }
                }

                // Has tags but no matching permissions
                return PolicyEvaluationResult.Failure(
                    $"Role '{context.UserRole}' does not have access to any tags: {string.Join(", ", taggable.Tags)}");
            }

            return PolicyEvaluationResult.Success("No tags to enforce");
        }
    }
}
