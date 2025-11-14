using System;
using System.Linq;
using System.Linq.Expressions;
using Xunit;
using AcornDB.Indexing;

namespace AcornDB.Test
{
    public class CompositeIndexTests
    {
        public class Employee
        {
            public string Id { get; set; } = string.Empty;
            public string Department { get; set; } = string.Empty;
            public int Age { get; set; }
            public decimal Salary { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        [Fact]
        public void CompositeIndex_CreatesWithMultipleProperties()
        {
            // Arrange
            Expression<Func<Employee, object>> dept = e => e.Department;
            Expression<Func<Employee, object>> age = e => e.Age;

            var selectors = new[] { dept, age };

            // Act
            var index = new ManagedCompositeIndex<Employee>("IX_Dept_Age", selectors);

            // Assert
            Assert.Equal("IX_Dept_Age", index.Name);
            Assert.Equal(IndexType.Composite, index.IndexType);
            Assert.Equal(2, index.PropertyNames.Count);
            Assert.Contains("Department", index.PropertyNames);
            Assert.Contains("Age", index.PropertyNames);
        }

        [Fact]
        public void CompositeIndex_LookupFindsExactMatch()
        {
            // Arrange
            Expression<Func<Employee, object>> dept = e => e.Department;
            Expression<Func<Employee, object>> age = e => e.Age;

            var index = new ManagedCompositeIndex<Employee>("IX_Dept_Age", new[] { dept, age });

            var emp1 = new Nut<Employee>
            {
                Id = "1",
                Payload = new Employee { Id = "1", Department = "Engineering", Age = 30 },
                Timestamp = DateTime.UtcNow
            };

            var emp2 = new Nut<Employee>
            {
                Id = "2",
                Payload = new Employee { Id = "2", Department = "Engineering", Age = 25 },
                Timestamp = DateTime.UtcNow
            };

            var emp3 = new Nut<Employee>
            {
                Id = "3",
                Payload = new Employee { Id = "3", Department = "Sales", Age = 30 },
                Timestamp = DateTime.UtcNow
            };

            index.Build(new object[] { emp1, emp2, emp3 });

            // Act - Lookup exact composite key
            var results = index.Lookup("Engineering", 30).ToList();

            // Assert
            Assert.Single(results);
            Assert.Equal("1", results[0]);
        }

        [Fact]
        public void CompositeIndex_PrefixLookupFindsPartialMatch()
        {
            // Arrange
            Expression<Func<Employee, object>> dept = e => e.Department;
            Expression<Func<Employee, object>> age = e => e.Age;

            var index = new ManagedCompositeIndex<Employee>("IX_Dept_Age", new[] { dept, age });

            var emp1 = new Nut<Employee>
            {
                Id = "1",
                Payload = new Employee { Department = "Engineering", Age = 30 },
                Timestamp = DateTime.UtcNow
            };

            var emp2 = new Nut<Employee>
            {
                Id = "2",
                Payload = new Employee { Department = "Engineering", Age = 25 },
                Timestamp = DateTime.UtcNow
            };

            var emp3 = new Nut<Employee>
            {
                Id = "3",
                Payload = new Employee { Department = "Sales", Age = 30 },
                Timestamp = DateTime.UtcNow
            };

            index.Build(new object[] { emp1, emp2, emp3 });

            // Act - Prefix lookup on Department only
            var results = index.PrefixLookup("Engineering").ToList();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Contains("1", results);
            Assert.Contains("2", results);
        }

        [Fact]
        public void CompositeIndex_RangeOnLastPropertyWorks()
        {
            // Arrange
            Expression<Func<Employee, object>> dept = e => e.Department;
            Expression<Func<Employee, object>> age = e => e.Age;

            var index = new ManagedCompositeIndex<Employee>("IX_Dept_Age", new[] { dept, age });

            var emp1 = new Nut<Employee>
            {
                Id = "1",
                Payload = new Employee { Department = "Engineering", Age = 30 },
                Timestamp = DateTime.UtcNow
            };

            var emp2 = new Nut<Employee>
            {
                Id = "2",
                Payload = new Employee { Department = "Engineering", Age = 25 },
                Timestamp = DateTime.UtcNow
            };

            var emp3 = new Nut<Employee>
            {
                Id = "3",
                Payload = new Employee { Department = "Engineering", Age = 35 },
                Timestamp = DateTime.UtcNow
            };

            var emp4 = new Nut<Employee>
            {
                Id = "4",
                Payload = new Employee { Department = "Sales", Age = 28 },
                Timestamp = DateTime.UtcNow
            };

            index.Build(new object[] { emp1, emp2, emp3, emp4 });

            // Act - Range query: Department = "Engineering" AND Age BETWEEN 25 AND 30
            var results = index.RangeOnLastProperty(new object[] { "Engineering" }, 25, 30).ToList();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Contains("1", results); // Age 30
            Assert.Contains("2", results); // Age 25
            Assert.DoesNotContain("3", results); // Age 35 - out of range
            Assert.DoesNotContain("4", results); // Wrong department
        }

        [Fact]
        public void CompositeIndex_GetAllSortedReturnsInOrder()
        {
            // Arrange
            Expression<Func<Employee, object>> dept = e => e.Department;
            Expression<Func<Employee, object>> age = e => e.Age;

            var index = new ManagedCompositeIndex<Employee>("IX_Dept_Age", new[] { dept, age });

            var emp1 = new Nut<Employee>
            {
                Id = "1",
                Payload = new Employee { Department = "Engineering", Age = 30 },
                Timestamp = DateTime.UtcNow
            };

            var emp2 = new Nut<Employee>
            {
                Id = "2",
                Payload = new Employee { Department = "Engineering", Age = 25 },
                Timestamp = DateTime.UtcNow
            };

            var emp3 = new Nut<Employee>
            {
                Id = "3",
                Payload = new Employee { Department = "Sales", Age = 20 },
                Timestamp = DateTime.UtcNow
            };

            index.Build(new object[] { emp1, emp2, emp3 });

            // Act
            var results = index.GetAllSorted(ascending: true).ToList();

            // Assert - Should be sorted first by Department, then by Age
            Assert.Equal(3, results.Count);
            Assert.Equal("2", results[0]); // Engineering, 25
            Assert.Equal("1", results[1]); // Engineering, 30
            Assert.Equal("3", results[2]); // Sales, 20
        }

        [Fact]
        public void CompositeIndex_ThreePropertyIndex()
        {
            // Arrange - Three-property index: Department, Age, Salary
            Expression<Func<Employee, object>> dept = e => e.Department;
            Expression<Func<Employee, object>> age = e => e.Age;
            Expression<Func<Employee, object>> salary = e => e.Salary;

            var index = new ManagedCompositeIndex<Employee>("IX_Dept_Age_Salary", new[] { dept, age, salary });

            var emp1 = new Nut<Employee>
            {
                Id = "1",
                Payload = new Employee { Department = "Engineering", Age = 30, Salary = 100000 },
                Timestamp = DateTime.UtcNow
            };

            var emp2 = new Nut<Employee>
            {
                Id = "2",
                Payload = new Employee { Department = "Engineering", Age = 30, Salary = 120000 },
                Timestamp = DateTime.UtcNow
            };

            index.Build(new object[] { emp1, emp2 });

            // Act - Exact lookup on all three properties
            var results = index.Lookup("Engineering", 30, 100000m).ToList();

            // Assert
            Assert.Single(results);
            Assert.Equal("1", results[0]);
        }

        [Fact]
        public void CompositeIndex_GetStatisticsReturnsAccurateInfo()
        {
            // Arrange
            Expression<Func<Employee, object>> dept = e => e.Department;
            Expression<Func<Employee, object>> age = e => e.Age;

            var index = new ManagedCompositeIndex<Employee>("IX_Dept_Age", new[] { dept, age });

            var emp1 = new Nut<Employee>
            {
                Id = "1",
                Payload = new Employee { Department = "Engineering", Age = 30 },
                Timestamp = DateTime.UtcNow
            };

            var emp2 = new Nut<Employee>
            {
                Id = "2",
                Payload = new Employee { Department = "Engineering", Age = 30 }, // Duplicate key
                Timestamp = DateTime.UtcNow
            };

            var emp3 = new Nut<Employee>
            {
                Id = "3",
                Payload = new Employee { Department = "Sales", Age = 25 },
                Timestamp = DateTime.UtcNow
            };

            index.Build(new object[] { emp1, emp2, emp3 });

            // Act
            var stats = index.GetStatistics();

            // Assert
            Assert.Equal(3, stats.EntryCount); // 3 documents
            Assert.Equal(2, stats.UniqueValueCount); // 2 unique composite keys
        }

        [Fact]
        public void CompositeIndex_AddAndRemoveWork()
        {
            // Arrange
            Expression<Func<Employee, object>> dept = e => e.Department;
            Expression<Func<Employee, object>> age = e => e.Age;

            var index = new ManagedCompositeIndex<Employee>("IX_Dept_Age", new[] { dept, age });

            index.Build(Enumerable.Empty<object>());

            // Act - Add documents
            index.Add("1", new Employee { Department = "Engineering", Age = 30 });
            index.Add("2", new Employee { Department = "Sales", Age = 25 });

            var resultsAfterAdd = index.Lookup("Engineering", 30).ToList();

            // Remove one
            index.Remove("1");

            var resultsAfterRemove = index.Lookup("Engineering", 30).ToList();

            // Assert
            Assert.Single(resultsAfterAdd);
            Assert.Equal("1", resultsAfterAdd[0]);

            Assert.Empty(resultsAfterRemove);
        }
    }
}
