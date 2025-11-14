using AcornDB.Policy;
using Xunit;

namespace AcornDB.Test.Policy
{
    /// <summary>
    /// Core tests for LocalPolicyEngine - basic policy enforcement, registration, and validation
    /// </summary>
    public class LocalPolicyEngineTests
    {
        [Fact]
        public void LocalPolicyEngine_ShouldInitialize_WithDefaultPolicies()
        {
            // Arrange & Act
            var engine = new LocalPolicyEngine();

            // Assert
            var policies = engine.GetPolicies();
            Assert.NotNull(policies);
            Assert.True(policies.Count >= 2, "Should have at least TTL and TagAccess policies");
            Assert.Contains(policies, p => p.Name == "TTL_Enforcement");
            Assert.Contains(policies, p => p.Name == "Tag_Access_Control");
        }

        [Fact]
        public void LocalPolicyEngine_ShouldRegisterCustomPolicy()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var customPolicy = new TestCustomPolicy();

            // Act
            engine.RegisterPolicy(customPolicy);

            // Assert
            var policies = engine.GetPolicies();
            Assert.Contains(policies, p => p.Name == "Test_Custom_Policy");
        }

        [Fact]
        public void LocalPolicyEngine_ShouldUnregisterPolicy()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var customPolicy = new TestCustomPolicy();
            engine.RegisterPolicy(customPolicy);

            // Act
            var result = engine.UnregisterPolicy("Test_Custom_Policy");

            // Assert
            Assert.True(result);
            var policies = engine.GetPolicies();
            Assert.DoesNotContain(policies, p => p.Name == "Test_Custom_Policy");
        }

        [Fact]
        public void LocalPolicyEngine_ShouldReturnFalse_WhenUnregisteringNonExistentPolicy()
        {
            // Arrange
            var engine = new LocalPolicyEngine();

            // Act
            var result = engine.UnregisterPolicy("NonExistent_Policy");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void LocalPolicyEngine_ShouldEmitPolicyEvaluatedEvent()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            PolicyEvaluationResult? capturedResult = null;
            engine.PolicyEvaluated += (result) => capturedResult = result;

            var entity = new TestEntity { Value = "test" };

            // Act
            engine.ApplyPolicies(entity);

            // Assert
            Assert.NotNull(capturedResult);
            Assert.True(capturedResult.Passed);
        }

        [Fact]
        public void LocalPolicyEngine_ShouldValidateEntity_Successfully()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var entity = new TestEntity { Value = "valid" };

            // Act
            var result = engine.Validate(entity);

            // Assert
            Assert.True(result.IsValid);
            Assert.Null(result.FailureReason);
        }

        [Fact]
        public void LocalPolicyEngine_ShouldFailValidation_ForNullEntity()
        {
            // Arrange
            var engine = new LocalPolicyEngine();

            // Act
            var result = engine.Validate<TestEntity>(null!);

            // Assert
            Assert.False(result.IsValid);
            Assert.NotNull(result.FailureReason);
            Assert.Contains("null", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LocalPolicyEngine_ShouldEnforceAllPolicies_WhenOptionEnabled()
        {
            // Arrange
            var options = new LocalPolicyEngineOptions { EnforceAllPolicies = true };
            var engine = new LocalPolicyEngine(options);
            var failingPolicy = new AlwaysFailPolicy();
            engine.RegisterPolicy(failingPolicy);

            var entity = new TestEntity { Value = "test" };

            // Act & Assert
            var ex = Assert.Throws<PolicyViolationException>(() => engine.ApplyPolicies(entity));
            Assert.Contains("AlwaysFailPolicy", ex.Message);
        }

        [Fact]
        public void LocalPolicyEngine_ShouldNotThrow_WhenPolicyFailsAndEnforcementDisabled()
        {
            // Arrange
            var options = new LocalPolicyEngineOptions { EnforceAllPolicies = false };
            var engine = new LocalPolicyEngine(options);
            var failingPolicy = new AlwaysFailPolicy();
            engine.RegisterPolicy(failingPolicy);

            var entity = new TestEntity { Value = "test" };

            // Act & Assert (should not throw)
            engine.ApplyPolicies(entity);
        }

        [Fact]
        public void LocalPolicyEngine_ShouldExecutePolicies_InPriorityOrder()
        {
            // Arrange
            var engine = new LocalPolicyEngine();
            var executionOrder = new List<string>();

            var highPriorityPolicy = new OrderTrackingPolicy("High", 100, executionOrder);
            var medPriorityPolicy = new OrderTrackingPolicy("Med", 50, executionOrder);
            var lowPriorityPolicy = new OrderTrackingPolicy("Low", 10, executionOrder);

            engine.RegisterPolicy(lowPriorityPolicy);
            engine.RegisterPolicy(medPriorityPolicy);
            engine.RegisterPolicy(highPriorityPolicy);

            var entity = new TestEntity { Value = "test" };

            // Act
            engine.ApplyPolicies(entity);

            // Assert
            Assert.Equal(3, executionOrder.Count);
            Assert.Equal("High", executionOrder[0]);
            Assert.Equal("Med", executionOrder[1]);
            Assert.Equal("Low", executionOrder[2]);
        }
    }

    // Test helper classes
    public class TestEntity
    {
        public string Value { get; set; } = string.Empty;
    }

    public class TestCustomPolicy : IPolicyRule
    {
        public string Name => "Test_Custom_Policy";
        public string Description => "A custom test policy";
        public int Priority => 50;

        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
        {
            return PolicyEvaluationResult.Success("Test policy passed");
        }
    }

    public class AlwaysFailPolicy : IPolicyRule
    {
        public string Name => "AlwaysFailPolicy";
        public string Description => "Policy that always fails";
        public int Priority => 50;

        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
        {
            return PolicyEvaluationResult.Failure("AlwaysFailPolicy failed intentionally");
        }
    }

    public class OrderTrackingPolicy : IPolicyRule
    {
        private readonly string _name;
        private readonly int _priority;
        private readonly List<string> _executionOrder;

        public OrderTrackingPolicy(string name, int priority, List<string> executionOrder)
        {
            _name = name;
            _priority = priority;
            _executionOrder = executionOrder;
        }

        public string Name => $"OrderTracking_{_name}";
        public string Description => $"Tracks execution order for {_name}";
        public int Priority => _priority;

        public PolicyEvaluationResult Evaluate<T>(T entity, PolicyContext context)
        {
            _executionOrder.Add(_name);
            return PolicyEvaluationResult.Success($"{_name} executed");
        }
    }
}
