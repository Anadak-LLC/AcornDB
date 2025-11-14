using System;
using System.Linq;
using Xunit;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Storage.Roots;
using AcornDB.Indexing;
using AcornDB.Models;
using AcornDB.Query;

namespace AcornDB.Test
{
    public class IndexingTests
    {
        public class User
        {
            public string Id { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public int Age { get; set; }
            public string Department { get; set; } = string.Empty;
        }

        [Fact]
        public void WithIndex_CreatesIndexAndBuildsFromExistingData()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .Sprout();

            // Pre-populate before checking index
            tree.Stash("1", new User { Id = "1", Email = "alice@example.com", Name = "Alice", Age = 30 });
            tree.Stash("2", new User { Id = "2", Email = "bob@example.com", Name = "Bob", Age = 25 });

            // Act
            var indexes = tree.GetAllIndexes();
            var emailIndex = tree.GetScalarIndex<User, string>("IX_User_Email");

            // Assert
            Assert.Equal(2, indexes.Count); // Identity index + Email index
            Assert.NotNull(emailIndex);
            Assert.Equal("IX_User_Email", emailIndex.Name);
            Assert.Equal(IndexType.Scalar, emailIndex.IndexType);

            // Verify index contains data
            var aliceIds = emailIndex.Lookup("alice@example.com").ToList();
            Assert.Single(aliceIds);
            Assert.Equal("1", aliceIds[0]);
        }

        [Fact]
        public void Index_UpdatesAutomaticallyOnStash()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .Sprout();

            var emailIndex = tree.GetScalarIndex<User, string>("IX_User_Email");
            Assert.NotNull(emailIndex);

            // Act - Stash a user
            tree.Stash("1", new User { Id = "1", Email = "charlie@example.com", Name = "Charlie", Age = 35 });

            // Assert - Index should have the new entry
            var charlieIds = emailIndex.Lookup("charlie@example.com").ToList();
            Assert.Single(charlieIds);
            Assert.Equal("1", charlieIds[0]);
        }

        [Fact]
        public void Index_UpdatesAutomaticallyOnToss()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "dave@example.com", Name = "Dave", Age = 40 });

            var emailIndex = tree.GetScalarIndex<User, string>("IX_User_Email");
            Assert.NotNull(emailIndex);

            // Verify it's in the index
            var daveIdsBefore = emailIndex.Lookup("dave@example.com").ToList();
            Assert.Single(daveIdsBefore);

            // Act - Remove the user
            tree.Toss("1");

            // Assert - Index should no longer have the entry
            var daveIdsAfter = emailIndex.Lookup("dave@example.com").ToList();
            Assert.Empty(daveIdsAfter);
        }

        [Fact]
        public void Index_SupportsRangeQueries()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, int>(u => u.Age)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "user1@example.com", Name = "User1", Age = 20 });
            tree.Stash("2", new User { Id = "2", Email = "user2@example.com", Name = "User2", Age = 30 });
            tree.Stash("3", new User { Id = "3", Email = "user3@example.com", Name = "User3", Age = 40 });
            tree.Stash("4", new User { Id = "4", Email = "user4@example.com", Name = "User4", Age = 50 });

            var ageIndex = tree.GetScalarIndex<User, int>("IX_User_Age");
            Assert.NotNull(ageIndex);

            // Act - Get users aged 25-45
            var middleAgedIds = ageIndex.Range(25, 45).ToList();

            // Assert
            Assert.Equal(2, middleAgedIds.Count);
            Assert.Contains("2", middleAgedIds); // Age 30
            Assert.Contains("3", middleAgedIds); // Age 40
        }

        [Fact]
        public void Index_SupportsSortedRetrieval()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, int>(u => u.Age)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "user1@example.com", Name = "User1", Age = 40 });
            tree.Stash("2", new User { Id = "2", Email = "user2@example.com", Name = "User2", Age = 20 });
            tree.Stash("3", new User { Id = "3", Email = "user3@example.com", Name = "User3", Age = 30 });

            var ageIndex = tree.GetScalarIndex<User, int>("IX_User_Age");
            Assert.NotNull(ageIndex);

            // Act - Get all IDs sorted by age ascending
            var sortedIdsAsc = ageIndex.GetAllSorted(ascending: true).ToList();

            // Assert - Should be ordered: 20, 30, 40
            Assert.Equal(3, sortedIdsAsc.Count);
            Assert.Equal("2", sortedIdsAsc[0]); // Age 20
            Assert.Equal("3", sortedIdsAsc[1]); // Age 30
            Assert.Equal("1", sortedIdsAsc[2]); // Age 40

            // Act - Get all IDs sorted by age descending
            var sortedIdsDesc = ageIndex.GetAllSorted(ascending: false).ToList();

            // Assert - Should be reversed
            Assert.Equal("1", sortedIdsDesc[0]); // Age 40
            Assert.Equal("3", sortedIdsDesc[1]); // Age 30
            Assert.Equal("2", sortedIdsDesc[2]); // Age 20
        }

        [Fact]
        public void Index_WithUniqueConstraint_EnforcesUniqueness()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email, cfg => cfg.Unique())
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "unique@example.com", Name = "First", Age = 25 });

            // Act & Assert - Attempting to add duplicate email should throw
            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                tree.Stash("2", new User { Id = "2", Email = "unique@example.com", Name = "Second", Age = 30 });
            });

            Assert.Contains("Unique index violation", exception.Message);
            Assert.Contains("unique@example.com", exception.Message);
        }

        [Fact]
        public void Index_WithCaseInsensitiveComparison_MatchesCaseInsensitively()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email, cfg => cfg.WithCaseInsensitiveComparison())
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "Alice@Example.COM", Name = "Alice", Age = 30 });

            var emailIndex = tree.GetScalarIndex<User, string>("IX_User_Email");
            Assert.NotNull(emailIndex);

            // Act - Lookup with different casing
            var ids1 = emailIndex.Lookup("alice@example.com").ToList();
            var ids2 = emailIndex.Lookup("ALICE@EXAMPLE.COM").ToList();
            var ids3 = emailIndex.Lookup("Alice@Example.COM").ToList();

            // Assert - All should find the same user
            Assert.Single(ids1);
            Assert.Single(ids2);
            Assert.Single(ids3);
            Assert.Equal("1", ids1[0]);
            Assert.Equal("1", ids2[0]);
            Assert.Equal("1", ids3[0]);
        }

        [Fact]
        public void Index_WithCustomName_UsesProvidedName()
        {
            // Arrange & Act
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email, cfg => cfg.WithName("CustomEmailIndex"))
                .Sprout();

            // Assert
            var index = tree.GetIndex("CustomEmailIndex");
            Assert.NotNull(index);
            Assert.Equal("CustomEmailIndex", index.Name);
        }

        [Fact]
        public void Index_GetStatistics_ReturnsAccurateStats()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Department)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "user1@example.com", Name = "User1", Department = "Engineering" });
            tree.Stash("2", new User { Id = "2", Email = "user2@example.com", Name = "User2", Department = "Engineering" });
            tree.Stash("3", new User { Id = "3", Email = "user3@example.com", Name = "User3", Department = "Sales" });

            var deptIndex = tree.GetIndex("IX_User_Department");
            Assert.NotNull(deptIndex);

            // Act
            var stats = deptIndex.GetStatistics();

            // Assert
            Assert.Equal(3, stats.EntryCount); // 3 users total
            Assert.Equal(2, stats.UniqueValueCount); // 2 unique departments (Engineering, Sales)
            Assert.True(stats.Selectivity > 0 && stats.Selectivity < 1); // Should be 2/3 = 0.666...
        }

        [Fact]
        public void Index_RebuildAllIndexes_ReconstructsFromCache()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "rebuild@example.com", Name = "Test", Age = 25 });

            var emailIndex = tree.GetScalarIndex<User, string>("IX_User_Email");
            Assert.NotNull(emailIndex);

            // Simulate index corruption by clearing it
            emailIndex.Clear();
            Assert.Empty(emailIndex.Lookup("rebuild@example.com"));

            // Act - Rebuild all indexes
            tree.RebuildAllIndexes();

            // Assert - Index should be restored
            var ids = emailIndex.Lookup("rebuild@example.com").ToList();
            Assert.Single(ids);
            Assert.Equal("1", ids[0]);
        }

        [Fact]
        public void MultipleIndexes_CanCoexistOnSameTree()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .WithIndex<User, int>(u => u.Age)
                .WithIndex<User, string>(u => u.Department)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "multi@example.com", Name = "Multi", Age = 30, Department = "IT" });

            // Act
            var allIndexes = tree.GetAllIndexes();

            // Assert
            Assert.Equal(4, allIndexes.Count); // Identity + Email + Age + Department

            var emailIndex = tree.GetScalarIndex<User, string>("IX_User_Email");
            var ageIndex = tree.GetScalarIndex<User, int>("IX_User_Age");
            var deptIndex = tree.GetScalarIndex<User, string>("IX_User_Department");

            Assert.NotNull(emailIndex);
            Assert.NotNull(ageIndex);
            Assert.NotNull(deptIndex);

            // Verify each index works independently
            Assert.Single(emailIndex.Lookup("multi@example.com"));
            Assert.Single(ageIndex.Lookup(30));
            Assert.Single(deptIndex.Lookup("IT"));
        }

        [Fact]
        public void Index_GetMinMax_ReturnsCorrectValues()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, int>(u => u.Age)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "user1@example.com", Name = "User1", Age = 25 });
            tree.Stash("2", new User { Id = "2", Email = "user2@example.com", Name = "User2", Age = 45 });
            tree.Stash("3", new User { Id = "3", Email = "user3@example.com", Name = "User3", Age = 35 });

            var ageIndex = tree.GetScalarIndex<User, int>("IX_User_Age");
            Assert.NotNull(ageIndex);

            // Act
            var minAge = ageIndex.GetMin();
            var maxAge = ageIndex.GetMax();

            // Assert
            Assert.Equal(25, minAge);
            Assert.Equal(45, maxAge);
        }

        [Fact]
        public void ManagedIndexRoot_TracksStashAndCrackOperations()
        {
            // Arrange
            var indexRoot = new ManagedIndexRoot(sequence: 50);
            var context = new RootProcessingContext
            {
                DocumentId = "test-doc-123"
            };
            var testData = System.Text.Encoding.UTF8.GetBytes("test document content");

            // Act - Stash operation
            var stashResult = indexRoot.OnStash(testData, context);

            // Assert - Stash tracking
            Assert.Equal(testData, stashResult); // Pass-through unchanged
            Assert.Equal(1, indexRoot.Metrics.TotalStashes);
            Assert.Equal(0, indexRoot.Metrics.TotalCracks);
            Assert.Contains("managed-index:v1", context.TransformationSignatures);
            Assert.Equal("test-doc-123", context.Metadata["IndexedDocumentId"]);

            // Act - Crack operation
            var crackContext = new RootProcessingContext { DocumentId = "test-doc-123" };
            crackContext.Metadata["IndexedDocumentId"] = "test-doc-123";
            var crackResult = indexRoot.OnCrack(testData, crackContext);

            // Assert - Crack tracking
            Assert.Equal(testData, crackResult); // Pass-through unchanged
            Assert.Equal(1, indexRoot.Metrics.TotalStashes);
            Assert.Equal(1, indexRoot.Metrics.TotalCracks);
            Assert.Equal("test-doc-123", crackContext.Metadata["RecoveredDocumentId"]);
        }

        [Fact]
        public void ManagedIndexRoot_HandlesErrorsGracefully()
        {
            // Arrange
            var indexRoot = new ManagedIndexRoot();
            var context = new RootProcessingContext
            {
                DocumentId = null // Null document ID
            };
            var testData = System.Text.Encoding.UTF8.GetBytes("test");

            // Act - Should not throw even with null document ID
            var result = indexRoot.OnStash(testData, context);

            // Assert
            Assert.Equal(testData, result);
            Assert.Equal(1, indexRoot.Metrics.TotalStashes);
            Assert.Equal(0, indexRoot.Metrics.TotalErrors); // Should succeed with null ID
        }

        [Fact]
        public void IdentityIndex_AutomaticallyCreatedForAllTrees()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .Sprout();

            // Act
            var indexes = tree.GetAllIndexes();
            var identityIndex = tree.GetIndex("IX_Identity");

            // Assert
            Assert.Single(indexes); // Should have identity index
            Assert.NotNull(identityIndex);
            Assert.Equal("IX_Identity", identityIndex.Name);
            Assert.Equal(IndexType.Identity, identityIndex.IndexType);
            Assert.True(identityIndex.IsUnique);
            Assert.Equal(IndexState.Ready, identityIndex.State);
        }

        [Fact]
        public void IdentityIndex_TracksDocumentCount()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "user1@example.com", Name = "User1" });
            tree.Stash("2", new User { Id = "2", Email = "user2@example.com", Name = "User2" });
            tree.Stash("3", new User { Id = "3", Email = "user3@example.com", Name = "User3" });

            // Act
            var identityIndex = tree.GetIndex("IX_Identity") as IdentityIndex<User>;
            Assert.NotNull(identityIndex);

            var stats = identityIndex.GetStatistics();
            var count = identityIndex.Count();
            var allIds = identityIndex.GetAllIds().ToList();

            // Assert
            Assert.Equal(3, stats.EntryCount);
            Assert.Equal(3, stats.UniqueValueCount);
            Assert.Equal(3, count);
            Assert.Equal(3, allIds.Count);
            Assert.Contains("1", allIds);
            Assert.Contains("2", allIds);
            Assert.Contains("3", allIds);
        }

        [Fact]
        public void IdentityIndex_SupportsLookupAndExists()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .Sprout();

            tree.Stash("user-123", new User { Id = "user-123", Email = "test@example.com", Name = "Test User" });

            var identityIndex = tree.GetIndex("IX_Identity") as IdentityIndex<User>;
            Assert.NotNull(identityIndex);

            // Act - Lookup existing ID
            var foundIds = identityIndex.Lookup("user-123").ToList();
            var exists = identityIndex.Exists("user-123");

            // Assert - Should find it
            Assert.Single(foundIds);
            Assert.Equal("user-123", foundIds[0]);
            Assert.True(exists);

            // Act - Lookup non-existent ID
            var notFoundIds = identityIndex.Lookup("nonexistent").ToList();
            var notExists = identityIndex.Exists("nonexistent");

            // Assert - Should not find it
            Assert.Empty(notFoundIds);
            Assert.False(notExists);
        }

        [Fact]
        public void IdentityIndex_UpdatesWithStashAndToss()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .Sprout();

            var identityIndex = tree.GetIndex("IX_Identity") as IdentityIndex<User>;
            Assert.NotNull(identityIndex);

            // Act - Stash documents
            tree.Stash("1", new User { Id = "1", Email = "user1@example.com" });
            tree.Stash("2", new User { Id = "2", Email = "user2@example.com" });

            // Assert - Count should be 2
            Assert.Equal(2, identityIndex.Count());
            Assert.True(identityIndex.Exists("1"));
            Assert.True(identityIndex.Exists("2"));

            // Act - Toss one document
            tree.Toss("1");

            // Assert - Count should be 1
            Assert.Equal(1, identityIndex.Count());
            Assert.False(identityIndex.Exists("1"));
            Assert.True(identityIndex.Exists("2"));
        }

        [Fact]
        public void QueryPlanner_CreatesExecutionPlan()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .WithIndex<User, int>(u => u.Age)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "user1@example.com", Age = 25 });
            tree.Stash("2", new User { Id = "2", Email = "user2@example.com", Age = 30 });

            // Act - Get execution plan for a query
            var plan = tree.Query()
                .Where(u => u.Age > 20)
                .Take(10)
                .Explain();

            // Assert
            Assert.NotNull(plan);
            Assert.NotNull(plan.Strategy);
            Assert.True(plan.EstimatedCost >= 0);
            Assert.NotEmpty(plan.Explanation);
        }

        [Fact]
        public void QueryExplain_FormatsReadablePlan()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "test@example.com" });

            // Act
            var explainText = tree.Query()
                .Where(u => u.Email == "test@example.com")
                .ExplainString();

            // Assert
            Assert.Contains("Query Execution Plan", explainText);
            Assert.Contains("Strategy:", explainText);
            Assert.Contains("Estimated Cost:", explainText);
        }

        [Fact]
        public void QueryWithIndexHint_UsesSpecifiedIndex()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .WithIndex<User, int>(u => u.Age)
                .Sprout();

            // Act - Use index hint
            var plan = tree.Query()
                .Where(u => u.Age > 20)
                .UseIndex("IX_User_Email")  // Hint to use email index (not optimal)
                .Explain();

            // Assert - Should respect the hint
            Assert.Equal("IX_User_Email", plan.SelectedIndex?.Name);
            Assert.Equal(QueryStrategy.IndexSeek, plan.Strategy);
            Assert.Contains("hinted index", plan.Explanation.ToLower());
        }

        [Fact]
        public void QueryPlanner_ConsidersMultipleIndexes()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .WithIndex<User, int>(u => u.Age)
                .WithIndex<User, string>(u => u.Department)
                .Sprout();

            // Act
            var plan = tree.Query()
                .Where(u => u.Age > 25)
                .Explain();

            // Assert - Should have considered all indexes
            Assert.True(plan.Candidates.Count >= 3); // At least Identity + Email + Age + Department
            Assert.All(plan.Candidates, c => Assert.NotNull(c.Index));
            Assert.All(plan.Candidates, c => Assert.NotEmpty(c.Reason));
        }

        // ======= Expression Analyzer Tests =======

        [Fact]
        public void ExpressionAnalyzer_ExtractsEqualityCondition()
        {
            // Arrange
            var analyzer = new AcornDB.Query.ExpressionAnalyzer<User>();

            // Act
            var result = analyzer.Analyze(u => u.Email == "test@example.com");

            // Assert
            Assert.True(result.IsIndexable);
            Assert.Single(result.Conditions);

            var condition = result.Conditions[0];
            Assert.Equal("Email", condition.PropertyName);
            Assert.Equal(AcornDB.Query.ComparisonOperator.Equal, condition.Operator);
            Assert.Equal("test@example.com", condition.Value);
            Assert.True(condition.IsConstantValue);
        }

        [Fact]
        public void ExpressionAnalyzer_ExtractsGreaterThanCondition()
        {
            // Arrange
            var analyzer = new AcornDB.Query.ExpressionAnalyzer<User>();

            // Act
            var result = analyzer.Analyze(u => u.Age > 25);

            // Assert
            Assert.True(result.IsIndexable);
            Assert.Single(result.Conditions);

            var condition = result.Conditions[0];
            Assert.Equal("Age", condition.PropertyName);
            Assert.Equal(AcornDB.Query.ComparisonOperator.GreaterThan, condition.Operator);
            Assert.Equal(25, condition.Value);
            Assert.True(condition.IsConstantValue);
        }

        [Fact]
        public void ExpressionAnalyzer_ExtractsLessThanOrEqualCondition()
        {
            // Arrange
            var analyzer = new AcornDB.Query.ExpressionAnalyzer<User>();

            // Act
            var result = analyzer.Analyze(u => u.Age <= 30);

            // Assert
            Assert.True(result.IsIndexable);
            Assert.Single(result.Conditions);

            var condition = result.Conditions[0];
            Assert.Equal("Age", condition.PropertyName);
            Assert.Equal(AcornDB.Query.ComparisonOperator.LessThanOrEqual, condition.Operator);
            Assert.Equal(30, condition.Value);
        }

        [Fact]
        public void ExpressionAnalyzer_HandlesSwappedComparison()
        {
            // Arrange
            var analyzer = new AcornDB.Query.ExpressionAnalyzer<User>();

            // Act - Value on left, property on right (25 < Age means Age > 25)
            var result = analyzer.Analyze(u => 25 < u.Age);

            // Assert
            Assert.True(result.IsIndexable);
            Assert.Single(result.Conditions);

            var condition = result.Conditions[0];
            Assert.Equal("Age", condition.PropertyName);
            Assert.Equal(AcornDB.Query.ComparisonOperator.GreaterThan, condition.Operator);
            Assert.Equal(25, condition.Value);
        }

        [Fact]
        public void ExpressionAnalyzer_ExtractsCapturedVariable()
        {
            // Arrange
            var analyzer = new AcornDB.Query.ExpressionAnalyzer<User>();
            var searchEmail = "captured@example.com";

            // Act - Use captured variable
            var result = analyzer.Analyze(u => u.Email == searchEmail);

            // Assert
            Assert.True(result.IsIndexable);
            Assert.Single(result.Conditions);

            var condition = result.Conditions[0];
            Assert.Equal("Email", condition.PropertyName);
            Assert.Equal("captured@example.com", condition.Value);
            Assert.True(condition.IsConstantValue);
        }

        [Fact]
        public void ExpressionAnalyzer_AnalyzesOrderByExpression()
        {
            // Arrange
            var analyzer = new AcornDB.Query.ExpressionAnalyzer<User>();

            // Act
            var result = analyzer.AnalyzeOrderBy(u => u.Age);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Age", result.PropertyName);
            Assert.Equal(typeof(int), result.PropertyType);
        }

        [Fact]
        public void ExpressionAnalyzer_AnalyzesOrderByStringProperty()
        {
            // Arrange
            var analyzer = new AcornDB.Query.ExpressionAnalyzer<User>();

            // Act
            var result = analyzer.AnalyzeOrderBy(u => u.Name);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Name", result.PropertyName);
            Assert.Equal(typeof(string), result.PropertyType);
        }

        [Fact]
        public void QueryPlanner_SelectsCorrectIndexForWhereClause()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .WithIndex<User, int>(u => u.Age)
                .WithIndex<User, string>(u => u.Department)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "user1@example.com", Age = 25, Department = "Engineering" });
            tree.Stash("2", new User { Id = "2", Email = "user2@example.com", Age = 30, Department = "Sales" });

            // Act - Query by Age should select Age index
            var plan = tree.Query()
                .Where(u => u.Age > 25)
                .Explain();

            // Assert
            Assert.Equal("IX_User_Age", plan.SelectedIndex?.Name);
            Assert.Contains("matches WHERE", plan.Explanation);
        }

        [Fact]
        public void QueryPlanner_SelectsCorrectIndexForOrderBy()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .WithIndex<User, int>(u => u.Age)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "alice@example.com", Age = 25 });
            tree.Stash("2", new User { Id = "2", Email = "bob@example.com", Age = 30 });

            // Act - OrderBy Age should prefer Age index
            var plan = tree.Query()
                .OrderBy(u => u.Age)
                .Explain();

            // Assert
            // Should select the Age index because it provides sorted results
            Assert.Contains("Age", plan.SelectedIndex?.Name ?? "");
            Assert.Contains("sorted", plan.Explanation.ToLower());
        }

        // ======= Index-Accelerated Execution Tests =======

        [Fact]
        public void Query_UsesIndexForEqualitySearch()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "alice@example.com", Name = "Alice", Age = 30 });
            tree.Stash("2", new User { Id = "2", Email = "bob@example.com", Name = "Bob", Age = 25 });
            tree.Stash("3", new User { Id = "3", Email = "charlie@example.com", Name = "Charlie", Age = 35 });

            // Act - Query by email (indexed property)
            var results = tree.Query()
                .Where(u => u.Email == "bob@example.com")
                .ToList();

            // Assert - Should find exactly one result via index lookup
            Assert.Single(results);
            Assert.Equal("Bob", results[0].Name);
            Assert.Equal(25, results[0].Age);
        }

        [Fact]
        public void Query_UsesIndexForRangeSearch()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, int>(u => u.Age)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "user1@example.com", Name = "User1", Age = 20 });
            tree.Stash("2", new User { Id = "2", Email = "user2@example.com", Name = "User2", Age = 25 });
            tree.Stash("3", new User { Id = "3", Email = "user3@example.com", Name = "User3", Age = 30 });
            tree.Stash("4", new User { Id = "4", Email = "user4@example.com", Name = "User4", Age = 35 });
            tree.Stash("5", new User { Id = "5", Email = "user5@example.com", Name = "User5", Age = 40 });

            // Act - Range query via index
            var results = tree.Query()
                .Where(u => u.Age > 25)
                .ToList();

            // Assert - Should find users with Age > 25 (30, 35, 40)
            Assert.True(results.Count >= 3, $"Expected at least 3 results, got {results.Count}");
            Assert.All(results, u => Assert.True(u.Age > 25));
            Assert.Contains(results, u => u.Name == "User3");
            Assert.Contains(results, u => u.Name == "User4");
            Assert.Contains(results, u => u.Name == "User5");
        }

        [Fact]
        public void Query_UsesIndexForOrderedResults()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, int>(u => u.Age)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Name = "Charlie", Age = 35 });
            tree.Stash("2", new User { Id = "2", Name = "Alice", Age = 25 });
            tree.Stash("3", new User { Id = "3", Name = "Bob", Age = 30 });

            // Act - OrderBy using index
            var results = tree.Query()
                .OrderBy(u => u.Age)
                .ToList();

            // Assert - Should be sorted by age (ascending)
            Assert.Equal(3, results.Count);
            Assert.Equal("Alice", results[0].Name);
            Assert.Equal(25, results[0].Age);
            Assert.Equal("Bob", results[1].Name);
            Assert.Equal(30, results[1].Age);
            Assert.Equal("Charlie", results[2].Name);
            Assert.Equal(35, results[2].Age);
        }

        [Fact]
        public void Query_UsesIndexForDescendingOrder()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, int>(u => u.Age)
                .Sprout();

            tree.Stash("1", new User { Id = "1", Name = "Alice", Age = 25 });
            tree.Stash("2", new User { Id = "2", Name = "Bob", Age = 30 });
            tree.Stash("3", new User { Id = "3", Name = "Charlie", Age = 35 });

            // Act - OrderByDescending using index
            var results = tree.Query()
                .OrderByDescending(u => u.Age)
                .ToList();

            // Assert - Should be sorted by age (descending)
            Assert.Equal(3, results.Count);
            Assert.Equal("Charlie", results[0].Name);
            Assert.Equal(35, results[0].Age);
            Assert.Equal("Bob", results[1].Name);
            Assert.Equal(30, results[1].Age);
            Assert.Equal("Alice", results[2].Name);
            Assert.Equal(25, results[2].Age);
        }

        [Fact]
        public void Query_CombinesIndexWithLimitAndSkip()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, int>(u => u.Age)
                .Sprout();

            for (int i = 1; i <= 10; i++)
            {
                tree.Stash($"{i}", new User { Id = $"{i}", Name = $"User{i}", Age = 20 + i });
            }

            // Act - Use index with Skip and Take
            var results = tree.Query()
                .OrderBy(u => u.Age)
                .Skip(3)
                .Take(3)
                .ToList();

            // Assert - Should skip first 3 and take next 3
            Assert.Equal(3, results.Count);
            Assert.Equal(24, results[0].Age);  // 4th item (21, 22, 23, *24*)
            Assert.Equal(25, results[1].Age);
            Assert.Equal(26, results[2].Age);
        }

        [Fact]
        public void Query_FallsBackToFullScanWhenNoMatchingIndex()
        {
            // Arrange
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, string>(u => u.Email)  // Only email index
                .Sprout();

            tree.Stash("1", new User { Id = "1", Email = "user1@example.com", Name = "User1", Age = 25 });
            tree.Stash("2", new User { Id = "2", Email = "user2@example.com", Name = "User2", Age = 30 });

            // Act - Query by Age (no Age index exists)
            var results = tree.Query()
                .Where(u => u.Age > 25)
                .ToList();

            // Assert - Should still work via full scan
            Assert.Single(results);
            Assert.Equal("User2", results[0].Name);
            Assert.Equal(30, results[0].Age);
        }

        [Fact]
        public void QueryPlanner_PrefersNativeIndexOverManagedIndex()
        {
            // This test verifies that when both native and managed indexes exist,
            // the query planner prefers the native index due to lower estimated cost

            // Arrange - Create a tree with a managed index
            var tree = new Acorn<User>()
                .InMemory()
                .WithIndex<User, int>(u => u.Age) // Managed index
                .Sprout();

            tree.Stash("1", new User { Id = "1", Age = 25 });
            tree.Stash("2", new User { Id = "2", Age = 30 });

            // Create a mock native index and add it
            var nativeIndex = new MockNativeIndex<User, int>(u => u.Age);
            tree.AddIndex(nativeIndex);

            // Act - Create a query plan for Age filter
            var planner = new DefaultQueryPlanner<User>(tree);
            var queryContext = new QueryContext<User>
            {
                WhereExpression = u => u.Age > 20,
                WherePredicate = nut => nut.Payload.Age > 20
            };

            var plan = planner.CreatePlan(queryContext);

            // Assert - The selected index should be the native one
            Assert.NotNull(plan.SelectedIndex);
            Assert.True(plan.SelectedIndex is MockNativeIndex<User, int>,
                "Query planner should prefer native index over managed index");
            Assert.Contains("native DB index", plan.Explanation);
        }

        // Mock native index for testing purposes
        private class MockNativeIndex<T, TProperty> : IScalarIndex<T, TProperty>, INativeIndex
            where T : class
        {
            private readonly ManagedScalarIndex<T, TProperty> _inner;

            public MockNativeIndex(System.Linq.Expressions.Expression<Func<T, TProperty>> propertySelector)
            {
                var config = new IndexConfiguration().WithName("IX_Mock_Native");
                _inner = new ManagedScalarIndex<T, TProperty>(propertySelector, config);
            }

            public string Name => _inner.Name + "_Native";
            public IndexType IndexType => IndexType.Scalar;
            public bool IsUnique => false;
            public IndexState State => IndexState.Ready;
            public System.Linq.Expressions.Expression<Func<T, TProperty>> PropertySelector => _inner.PropertySelector;
            public string CreateIndexDdl => "CREATE INDEX mock_native_index";
            public string DropIndexDdl => "DROP INDEX mock_native_index";
            public bool IsCreated => true;

            public void Build(System.Collections.Generic.IEnumerable<object> documents) => _inner.Build(documents);
            public void Add(string id, object document) => _inner.Add(id, document);
            public void Remove(string id) => _inner.Remove(id);
            public void Clear() => _inner.Clear();
            public System.Collections.Generic.IEnumerable<string> Lookup(TProperty value) => _inner.Lookup(value);
            public System.Collections.Generic.IEnumerable<string> Range(TProperty min, TProperty max) => _inner.Range(min, max);
            public System.Collections.Generic.IEnumerable<string> GetAllSorted(bool ascending = true) => _inner.GetAllSorted(ascending);
            public TProperty? GetMin() => _inner.GetMin();
            public TProperty? GetMax() => _inner.GetMax();
            public IndexStatistics GetStatistics() => _inner.GetStatistics();
            public void CreateInDatabase() { }
            public void DropFromDatabase() { }
            public bool VerifyInDatabase() => true;
        }
    }
}
