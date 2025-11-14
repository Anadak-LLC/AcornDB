using AcornDB.Policy;
using Xunit;

namespace AcornDB.Test.Policy
{
    /// <summary>
    /// Tests for TTL (Time-To-Live) policy enforcement
    /// </summary>
    public class TtlPolicyTests
    {
        [Fact]
        public void TtlPolicy_ShouldPass_ForEntityWithoutTtl()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var entity = new SimpleEntity { Name = "Test" };

            // Act
            var result = engine.Validate(entity);

            // Assert
            Assert.True(result.IsValid);
        }

        [Fact]
        public void TtlPolicy_ShouldPass_ForNonExpiredEntity()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var entity = new TtlEntity
            {
                Name = "Test",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            // Act
            var result = engine.Validate(entity);

            // Assert
            Assert.True(result.IsValid);
        }

        [Fact]
        public void TtlPolicy_ShouldFail_ForExpiredEntity()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var entity = new TtlEntity
            {
                Name = "Test",
                ExpiresAt = DateTime.UtcNow.AddHours(-1) // Expired 1 hour ago
            };

            // Act
            var result = engine.Validate(entity);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("expired", result.FailureReason ?? "", StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TtlPolicy_ShouldGenerateDeleteAction_ForExpiredEntity()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var entity = new TtlEntity
            {
                Name = "Test",
                ExpiresAt = DateTime.UtcNow.AddSeconds(-1)
            };

            PolicyEvaluationResult? capturedResult = null;
            engine.PolicyEvaluated += (result) =>
            {
                if (!result.Passed)
                    capturedResult = result;
            };

            // Act
            engine.ApplyPolicies(entity);

            // Assert
            Assert.NotNull(capturedResult);
            Assert.False(capturedResult.Passed);
            Assert.Contains(capturedResult.Actions, a => a.StartsWith("DELETE:"));
        }

        [Fact]
        public void TtlPolicy_ShouldHandleNullExpiresAt()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var entity = new TtlEntity
            {
                Name = "Test",
                ExpiresAt = null // No expiration
            };

            // Act
            var result = engine.Validate(entity);

            // Assert
            Assert.True(result.IsValid);
        }

        [Fact]
        public void EnforceTTL_ShouldProcessMultipleEntities()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var entities = new[]
            {
                new TtlEntity { Name = "Valid1", ExpiresAt = DateTime.UtcNow.AddHours(1) },
                new TtlEntity { Name = "Expired1", ExpiresAt = DateTime.UtcNow.AddHours(-1) },
                new TtlEntity { Name = "Valid2", ExpiresAt = null },
                new TtlEntity { Name = "Expired2", ExpiresAt = DateTime.UtcNow.AddMinutes(-5) }
            };

            var failedEntities = new List<string>();
            engine.PolicyEvaluated += (result) =>
            {
                if (!result.Passed && result.Reason?.Contains("expired") == true)
                {
                    failedEntities.Add(result.Reason);
                }
            };

            // Act
            engine.EnforceTTL(entities);

            // Assert
            Assert.Equal(2, failedEntities.Count);
        }

        [Fact]
        public void TtlPolicy_ShouldWork_WithTTLPropertyName()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var entity = new AlternativeTtlEntity
            {
                Name = "Test",
                TTL = DateTime.UtcNow.AddHours(-1) // Using TTL instead of ExpiresAt
            };

            // Act
            var result = engine.Validate(entity);

            // Assert
            Assert.False(result.IsValid);
        }

        [Fact]
        public void TtlPolicy_ShouldBePrecise_AtExpirationBoundary()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var justExpired = DateTime.UtcNow.AddMilliseconds(-100);
            var entity = new TtlEntity
            {
                Name = "Test",
                ExpiresAt = justExpired
            };

            // Act
            var result = engine.Validate(entity);

            // Assert
            Assert.False(result.IsValid, "Entity should be expired even by milliseconds");
        }
    }

    // Test helper classes
    public class SimpleEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    public class TtlEntity
    {
        public string Name { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
    }

    public class AlternativeTtlEntity
    {
        public string Name { get; set; } = string.Empty;
        public DateTime? TTL { get; set; }
    }
}
