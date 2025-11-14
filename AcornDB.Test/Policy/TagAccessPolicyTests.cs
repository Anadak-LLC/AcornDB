using AcornDB.Policy;
using Xunit;

namespace AcornDB.Test.Policy
{
    /// <summary>
    /// Tests for tag-based access control policy
    /// </summary>
    public class TagAccessPolicyTests
    {
        [Fact]
        public void TagAccessPolicy_ShouldAllowAccess_WhenRoleHasPermission()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            engine.GrantTagAccess("confidential", "admin");

            var entity = new TaggedEntity
            {
                Name = "Secret Doc",
                Tags = new[] { "confidential" }
            };

            // Act
            var hasAccess = engine.ValidateAccess(entity, "admin");

            // Assert
            Assert.True(hasAccess);
        }

        [Fact]
        public void TagAccessPolicy_ShouldDenyAccess_WhenRoleHasNoPermission()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            engine.GrantTagAccess("confidential", "admin");

            var entity = new TaggedEntity
            {
                Name = "Secret Doc",
                Tags = new[] { "confidential" }
            };

            // Act
            var hasAccess = engine.ValidateAccess(entity, "guest");

            // Assert
            Assert.False(hasAccess);
        }

        [Fact]
        public void TagAccessPolicy_ShouldAllowAccess_WithWildcardRole()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            engine.GrantTagAccess("public", "*");

            var entity = new TaggedEntity
            {
                Name = "Public Doc",
                Tags = new[] { "public" }
            };

            // Act
            var guestAccess = engine.ValidateAccess(entity, "guest");
            var adminAccess = engine.ValidateAccess(entity, "admin");
            var anyAccess = engine.ValidateAccess(entity, "randomrole");

            // Assert
            Assert.True(guestAccess);
            Assert.True(adminAccess);
            Assert.True(anyAccess);
        }

        [Fact]
        public void TagAccessPolicy_ShouldAllowMultipleRoles_ForSameTag()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            engine.GrantTagAccess("document", "admin");
            engine.GrantTagAccess("document", "editor");

            var entity = new TaggedEntity
            {
                Name = "Doc",
                Tags = new[] { "document" }
            };

            // Act
            var adminAccess = engine.ValidateAccess(entity, "admin");
            var editorAccess = engine.ValidateAccess(entity, "editor");
            var guestAccess = engine.ValidateAccess(entity, "guest");

            // Assert
            Assert.True(adminAccess);
            Assert.True(editorAccess);
            Assert.False(guestAccess);
        }

        [Fact]
        public void TagAccessPolicy_ShouldGrantAccess_IfAnyTagMatches()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            engine.GrantTagAccess("public", "*");
            engine.GrantTagAccess("internal", "employee");

            var entity = new TaggedEntity
            {
                Name = "Mixed Doc",
                Tags = new[] { "internal", "confidential" }
            };

            // Act
            var employeeAccess = engine.ValidateAccess(entity, "employee");

            // Assert
            Assert.True(employeeAccess, "Should grant access if any tag matches");
        }

        [Fact]
        public void TagAccessPolicy_ShouldRevokeAccess()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            engine.GrantTagAccess("document", "editor");

            var entity = new TaggedEntity
            {
                Name = "Doc",
                Tags = new[] { "document" }
            };

            // Act - First verify access is granted
            var accessBefore = engine.ValidateAccess(entity, "editor");

            // Revoke access
            engine.RevokeTagAccess("document", "editor");

            var accessAfter = engine.ValidateAccess(entity, "editor");

            // Assert
            Assert.True(accessBefore);
            Assert.False(accessAfter);
        }

        [Fact]
        public void TagAccessPolicy_ShouldReturnRolesForTag()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            engine.GrantTagAccess("report", "admin");
            engine.GrantTagAccess("report", "manager");
            engine.GrantTagAccess("report", "analyst");

            // Act
            var roles = engine.GetRolesForTag("report");

            // Assert
            Assert.Equal(3, roles.Count);
            Assert.Contains("admin", roles);
            Assert.Contains("manager", roles);
            Assert.Contains("analyst", roles);
        }

        [Fact]
        public void TagAccessPolicy_ShouldReturnEmptySet_ForUnknownTag()
        {
            // Arrange
            var engine = new LocalPolicyEngine();

            // Act
            var roles = engine.GetRolesForTag("nonexistent");

            // Assert
            Assert.Empty(roles);
        }

        [Fact]
        public void TagAccessPolicy_ShouldAllowByDefault_WhenEntityHasNoTags()
        {
            // Arrange
            var engine = new LocalPolicyEngine(); // Default: DefaultAccessWhenNoTags = true
            var entity = new TaggedEntity
            {
                Name = "Untagged Doc",
                Tags = Array.Empty<string>()
            };

            // Act
            var hasAccess = engine.ValidateAccess(entity, "anyone");

            // Assert
            Assert.True(hasAccess);
        }

        [Fact]
        public void TagAccessPolicy_ShouldDenyByDefault_WhenConfigured()
        {
            // Arrange
            var options = new LocalPolicyEngineOptions { DefaultAccessWhenNoTags = false };
            var engine = new LocalPolicyEngine(options);
            var entity = new TaggedEntity
            {
                Name = "Untagged Doc",
                Tags = Array.Empty<string>()
            };

            // Act
            var hasAccess = engine.ValidateAccess(entity, "anyone");

            // Assert
            Assert.False(hasAccess);
        }

        [Fact]
        public void TagAccessPolicy_ShouldDenyAccess_ForEmptyUserRole()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            engine.GrantTagAccess("document", "admin");

            var entity = new TaggedEntity
            {
                Name = "Doc",
                Tags = new[] { "document" }
            };

            // Act
            var hasAccess = engine.ValidateAccess(entity, "");

            // Assert
            Assert.False(hasAccess);
        }

        [Fact]
        public void TagAccessPolicy_ShouldAllowAccess_ForNonTaggableEntity()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var entity = new NonTaggableEntity { Name = "Simple" };

            // Act
            var hasAccess = engine.ValidateAccess(entity, "anyone");

            // Assert
            Assert.True(hasAccess, "Non-taggable entities should allow access by default");
        }

        [Fact]
        public void TagAccessPolicy_ShouldBeThreadSafe()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var exceptions = new List<Exception>();

            // Act - Concurrent access grants and checks
            Parallel.For(0, 100, i =>
            {
                try
                {
                    engine.GrantTagAccess($"tag{i % 10}", $"role{i % 5}");

                    var entity = new TaggedEntity
                    {
                        Name = $"Doc{i}",
                        Tags = new[] { $"tag{i % 10}" }
                    };

                    engine.ValidateAccess(entity, $"role{i % 5}");
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            // Assert
            Assert.Empty(exceptions);
        }
    }

    // Test helper classes
    public class TaggedEntity : IPolicyTaggable
    {
        public string Name { get; set; } = string.Empty;
        public IEnumerable<string> Tags { get; set; } = Array.Empty<string>();

        public bool HasTag(string tag)
        {
            return Tags.Contains(tag);
        }
    }

    public class NonTaggableEntity
    {
        public string Name { get; set; } = string.Empty;
    }
}
