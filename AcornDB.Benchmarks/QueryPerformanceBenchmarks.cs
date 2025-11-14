using BenchmarkDotNet.Attributes;
using AcornDB;
using AcornDB.Storage;
using AcornDB.Query;

namespace AcornDB.Benchmarks
{
    /// <summary>
    /// Benchmarks for query performance using FluentQuery API and LINQ.
    /// Measures filtering, sorting, aggregation, and pagination performance.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 5)]
    public class QueryPerformanceBenchmarks
    {
        private Tree<Product>? _tree;
        private Tree<Order>? _orderTree;

        public class Product
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public int StockQuantity { get; set; }
            public string[] Tags { get; set; } = Array.Empty<string>();
            public DateTime CreatedDate { get; set; }
            public bool IsActive { get; set; }
        }

        public class Order
        {
            public string Id { get; set; } = string.Empty;
            public string CustomerId { get; set; } = string.Empty;
            public DateTime OrderDate { get; set; }
            public decimal TotalAmount { get; set; }
            public string Status { get; set; } = string.Empty;
            public string[] ProductIds { get; set; } = Array.Empty<string>();
        }

        [Params(1_000, 10_000, 100_000)]
        public int ProductCount;

        [GlobalSetup]
        public void Setup()
        {
            // Setup Products
            var trunk = new MemoryTrunk<Product>();
            _tree = new Tree<Product>(trunk);
            _tree.TtlEnforcementEnabled = false;
            _tree.CacheEvictionEnabled = false;

            var random = new Random(42);
            var categories = new[] { "Electronics", "Clothing", "Books", "Home", "Sports" };
            var tags = new[] { "sale", "new", "featured", "clearance", "premium" };

            for (int i = 0; i < ProductCount; i++)
            {
                _tree.Stash(new Product
                {
                    Id = $"product-{i}",
                    Name = $"Product {i}",
                    Category = categories[i % categories.Length],
                    Price = (decimal)(random.NextDouble() * 1000),
                    StockQuantity = random.Next(0, 1000),
                    Tags = new[] { tags[i % tags.Length], tags[(i + 1) % tags.Length] },
                    CreatedDate = DateTime.UtcNow.AddDays(-random.Next(0, 365)),
                    IsActive = i % 10 != 0 // 90% active
                });
            }

            // Setup Orders
            var orderTrunk = new MemoryTrunk<Order>();
            _orderTree = new Tree<Order>(orderTrunk);
            _orderTree.TtlEnforcementEnabled = false;
            _orderTree.CacheEvictionEnabled = false;

            var statuses = new[] { "Pending", "Processing", "Shipped", "Delivered", "Cancelled" };

            for (int i = 0; i < ProductCount / 10; i++)
            {
                _orderTree.Stash(new Order
                {
                    Id = $"order-{i}",
                    CustomerId = $"customer-{random.Next(0, ProductCount / 100)}",
                    OrderDate = DateTime.UtcNow.AddDays(-random.Next(0, 90)),
                    TotalAmount = (decimal)(random.NextDouble() * 5000),
                    Status = statuses[i % statuses.Length],
                    ProductIds = Enumerable.Range(0, random.Next(1, 5))
                        .Select(_ => $"product-{random.Next(0, ProductCount)}")
                        .ToArray()
                });
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _tree = null;
            _orderTree = null;
        }

        // ===== Simple Filtering =====

        [Benchmark(Baseline = true)]
        public void Query_FullScan_NoFilter()
        {
            // Baseline: Full table scan
            var results = _tree!.NutShells().ToList();
        }

        [Benchmark]
        public void Query_SimpleFilter_Category()
        {
            // Single equality predicate
            var results = _tree!.NutShells()
                .Where(n => n.Payload.Category == "Electronics")
                .ToList();
        }

        [Benchmark]
        public void Query_SimpleFilter_PriceRange()
        {
            // Range predicate
            var results = _tree!.NutShells()
                .Where(n => n.Payload.Price >= 100 && n.Payload.Price <= 500)
                .ToList();
        }

        [Benchmark]
        public void Query_SimpleFilter_IsActive()
        {
            // Boolean predicate
            var results = _tree!.NutShells()
                .Where(n => n.Payload.IsActive)
                .ToList();
        }

        // ===== Complex Filtering =====

        [Benchmark]
        public void Query_ComplexFilter_MultipleConditions()
        {
            // Multiple AND conditions
            var results = _tree!.NutShells()
                .Where(n => n.Payload.Category == "Electronics" &&
                           n.Payload.Price < 500 &&
                           n.Payload.IsActive &&
                           n.Payload.StockQuantity > 0)
                .ToList();
        }

        [Benchmark]
        public void Query_ComplexFilter_OrConditions()
        {
            // OR conditions (harder to optimize)
            var results = _tree!.NutShells()
                .Where(n => n.Payload.Category == "Electronics" ||
                           n.Payload.Category == "Books" ||
                           n.Payload.Price < 50)
                .ToList();
        }

        [Benchmark]
        public void Query_ComplexFilter_StringContains()
        {
            // String search (expensive)
            var results = _tree!.NutShells()
                .Where(n => n.Payload.Name.Contains("Product 1"))
                .ToList();
        }

        [Benchmark]
        public void Query_ComplexFilter_ArrayContains()
        {
            // Array membership check
            var results = _tree!.NutShells()
                .Where(n => n.Payload.Tags.Contains("sale"))
                .ToList();
        }

        [Benchmark]
        public void Query_ComplexFilter_DateRange()
        {
            // Date range query
            var cutoffDate = DateTime.UtcNow.AddDays(-30);
            var results = _tree!.NutShells()
                .Where(n => n.Payload.CreatedDate >= cutoffDate)
                .ToList();
        }

        // ===== Sorting =====

        [Benchmark]
        public void Query_Sort_SingleField_Ascending()
        {
            // Sort by price ascending
            var results = _tree!.NutShells()
                .OrderBy(n => n.Payload.Price)
                .ToList();
        }

        [Benchmark]
        public void Query_Sort_SingleField_Descending()
        {
            // Sort by price descending
            var results = _tree!.NutShells()
                .OrderByDescending(n => n.Payload.Price)
                .ToList();
        }

        [Benchmark]
        public void Query_Sort_MultipleFields()
        {
            // Sort by category, then price
            var results = _tree!.NutShells()
                .OrderBy(n => n.Payload.Category)
                .ThenBy(n => n.Payload.Price)
                .ToList();
        }

        [Benchmark]
        public void Query_FilterAndSort()
        {
            // Combined filter + sort
            var results = _tree!.NutShells()
                .Where(n => n.Payload.IsActive && n.Payload.StockQuantity > 0)
                .OrderBy(n => n.Payload.Price)
                .ToList();
        }

        // ===== Pagination =====

        [Benchmark]
        public void Query_Pagination_FirstPage()
        {
            // First page (Skip 0, Take 20)
            var results = _tree!.NutShells()
                .OrderBy(n => n.Payload.Price)
                .Skip(0)
                .Take(20)
                .ToList();
        }

        [Benchmark]
        public void Query_Pagination_MiddlePage()
        {
            // Middle page (Skip 5000, Take 20)
            var results = _tree!.NutShells()
                .OrderBy(n => n.Payload.Price)
                .Skip(ProductCount / 2)
                .Take(20)
                .ToList();
        }

        [Benchmark]
        public void Query_Pagination_LastPage()
        {
            // Last page
            var results = _tree!.NutShells()
                .OrderBy(n => n.Payload.Price)
                .Skip(ProductCount - 20)
                .Take(20)
                .ToList();
        }

        [Benchmark]
        public void Query_Pagination_WithFilter()
        {
            // Paginated filtered query
            var results = _tree!.NutShells()
                .Where(n => n.Payload.Category == "Electronics")
                .OrderBy(n => n.Payload.Price)
                .Skip(100)
                .Take(20)
                .ToList();
        }

        // ===== Aggregations =====

        [Benchmark]
        public void Query_Aggregation_Count()
        {
            // Count matching records
            var count = _tree!.NutShells()
                .Count(n => n.Payload.IsActive);
        }

        [Benchmark]
        public void Query_Aggregation_Sum()
        {
            // Sum of prices
            var total = _tree!.NutShells()
                .Sum(n => n.Payload.Price);
        }

        [Benchmark]
        public void Query_Aggregation_Average()
        {
            // Average price
            var avg = _tree!.NutShells()
                .Average(n => n.Payload.Price);
        }

        [Benchmark]
        public void Query_Aggregation_MinMax()
        {
            // Min and max price
            var min = _tree!.NutShells().Min(n => n.Payload.Price);
            var max = _tree!.NutShells().Max(n => n.Payload.Price);
        }

        [Benchmark]
        public void Query_Aggregation_GroupBy_Simple()
        {
            // Group by category, count per group
            var grouped = _tree!.NutShells()
                .GroupBy(n => n.Payload.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Count = g.Count()
                })
                .ToList();
        }

        [Benchmark]
        public void Query_Aggregation_GroupBy_WithAggregates()
        {
            // Group by category with multiple aggregates
            var grouped = _tree!.NutShells()
                .GroupBy(n => n.Payload.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Count = g.Count(),
                    TotalValue = g.Sum(n => n.Payload.Price * n.Payload.StockQuantity),
                    AvgPrice = g.Average(n => n.Payload.Price),
                    MinPrice = g.Min(n => n.Payload.Price),
                    MaxPrice = g.Max(n => n.Payload.Price)
                })
                .ToList();
        }

        [Benchmark]
        public void Query_Aggregation_GroupBy_WithFilter()
        {
            // Filtered group by
            var grouped = _tree!.NutShells()
                .Where(n => n.Payload.IsActive)
                .GroupBy(n => n.Payload.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Count = g.Count(),
                    AvgPrice = g.Average(n => n.Payload.Price)
                })
                .ToList();
        }

        // ===== Projections =====

        [Benchmark]
        public void Query_Projection_SingleField()
        {
            // Select only names
            var names = _tree!.NutShells()
                .Select(n => n.Payload.Name)
                .ToList();
        }

        [Benchmark]
        public void Query_Projection_MultipleFields()
        {
            // Select subset of fields
            var results = _tree!.NutShells()
                .Select(n => new
                {
                    n.Payload.Name,
                    n.Payload.Price,
                    n.Payload.Category
                })
                .ToList();
        }

        [Benchmark]
        public void Query_Projection_WithCalculation()
        {
            // Computed fields
            var results = _tree!.NutShells()
                .Select(n => new
                {
                    n.Payload.Name,
                    n.Payload.Price,
                    TotalValue = n.Payload.Price * n.Payload.StockQuantity,
                    PriceCategory = n.Payload.Price < 100 ? "Budget" :
                                   n.Payload.Price < 500 ? "Mid-Range" : "Premium"
                })
                .ToList();
        }

        // ===== Distinct =====

        [Benchmark]
        public void Query_Distinct_Categories()
        {
            // Get unique categories
            var categories = _tree!.NutShells()
                .Select(n => n.Payload.Category)
                .Distinct()
                .ToList();
        }

        [Benchmark]
        public void Query_Distinct_WithFilter()
        {
            // Distinct active product categories
            var categories = _tree!.NutShells()
                .Where(n => n.Payload.IsActive)
                .Select(n => n.Payload.Category)
                .Distinct()
                .ToList();
        }

        // ===== Joins (Simulated) =====

        [Benchmark]
        public void Query_Join_InMemory()
        {
            // Simulate join by loading both datasets
            var products = _tree!.NutShells().ToList();
            var orders = _orderTree!.NutShells().ToList();

            var joined = from order in orders
                         from productId in order.Payload.ProductIds
                         join product in products on productId equals product.Id
                         select new
                         {
                             OrderId = order.Id,
                             ProductName = product.Payload.Name,
                             ProductPrice = product.Payload.Price
                         };

            var results = joined.ToList();
        }

        // ===== FluentQuery API Benchmarks =====

        [Benchmark]
        public void FluentQuery_Where_Category()
        {
            var query = _tree!.Query()
                .Where(p => p.Category == "Electronics");

            var results = query.ToList();
        }

        [Benchmark]
        public void FluentQuery_Where_PriceRange()
        {
            var query = _tree!.Query()
                .Where(p => p.Price >= 100 && p.Price <= 500);

            var results = query.ToList();
        }

        [Benchmark]
        public void FluentQuery_OrderBy_Price()
        {
            var query = _tree!.Query()
                .OrderBy(p => p.Price);

            var results = query.ToList();
        }

        [Benchmark]
        public void FluentQuery_Complex_Pipeline()
        {
            var query = _tree!.Query()
                .Where(p => p.IsActive && p.StockQuantity > 0)
                .OrderBy(p => p.Price)
                .Skip(100)
                .Take(20);

            var results = query.ToList();
        }

        // ===== Top-N Queries =====

        [Benchmark]
        public void Query_TopN_Cheapest10()
        {
            var results = _tree!.NutShells()
                .OrderBy(n => n.Payload.Price)
                .Take(10)
                .ToList();
        }

        [Benchmark]
        public void Query_TopN_MostExpensive10()
        {
            var results = _tree!.NutShells()
                .OrderByDescending(n => n.Payload.Price)
                .Take(10)
                .ToList();
        }

        [Benchmark]
        public void Query_TopN_WithFilter_Cheapest10Electronics()
        {
            var results = _tree!.NutShells()
                .Where(n => n.Payload.Category == "Electronics")
                .OrderBy(n => n.Payload.Price)
                .Take(10)
                .ToList();
        }

        // ===== Existence Checks =====

        [Benchmark]
        public void Query_Exists_Any()
        {
            var hasElectronics = _tree!.NutShells()
                .Any(n => n.Payload.Category == "Electronics");
        }

        [Benchmark]
        public void Query_Exists_All()
        {
            var allActive = _tree!.NutShells()
                .All(n => n.Payload.IsActive);
        }

        [Benchmark]
        public void Query_Exists_First()
        {
            var first = _tree!.NutShells()
                .Where(n => n.Payload.Category == "Electronics")
                .FirstOrDefault();
        }

        // ===== Query Materialization Strategies =====

        [Benchmark]
        public void Query_Materialization_ToList()
        {
            var results = _tree!.NutShells()
                .Where(n => n.Payload.IsActive)
                .ToList();
        }

        [Benchmark]
        public void Query_Materialization_ToArray()
        {
            var results = _tree!.NutShells()
                .Where(n => n.Payload.IsActive)
                .ToArray();
        }

        [Benchmark]
        public void Query_Materialization_ToDictionary()
        {
            var results = _tree!.NutShells()
                .Where(n => n.Payload.IsActive)
                .ToDictionary(n => n.Id, n => n.Payload);
        }
    }

    /// <summary>
    /// Expected Query Performance Results:
    ///
    /// Full Scan Baseline (1K docs):
    /// - No Filter: ~1ms
    /// - 10K docs: ~10ms
    /// - 100K docs: ~100ms
    /// - Query time scales linearly with dataset size
    ///
    /// Simple Filters:
    /// - Equality (Category): ~1.2ms for 1K docs (+20% vs baseline)
    /// - Range (Price): ~1.5ms for 1K docs (+50% vs baseline)
    /// - Boolean (IsActive): ~1.1ms for 1K docs (+10% vs baseline)
    ///
    /// Complex Filters:
    /// - Multiple AND: ~2ms for 1K docs (+100% vs baseline)
    /// - OR conditions: ~2.5ms for 1K docs (+150% vs baseline, harder to short-circuit)
    /// - String Contains: ~5ms for 1K docs (+400% vs baseline, expensive)
    /// - Array Contains: ~3ms for 1K docs (+200% vs baseline)
    /// - Date Range: ~1.5ms for 1K docs (+50% vs baseline)
    ///
    /// Sorting:
    /// - Single Field: ~2ms for 1K docs (O(n log n))
    /// - Multiple Fields: ~3ms for 1K docs (additional comparison overhead)
    /// - Filter + Sort: ~4ms for 1K docs (combined overhead)
    ///
    /// Pagination:
    /// - First Page: ~2ms (efficient - only materialize 20 items)
    /// - Middle Page: ~60ms for 100K docs (must skip 50K items first)
    /// - Last Page: ~120ms for 100K docs (worst case - scan entire dataset)
    /// - Recommendation: Use cursor-based pagination for large datasets
    ///
    /// Aggregations:
    /// - Count: ~1ms for 1K docs
    /// - Sum/Avg: ~2ms for 1K docs (numeric operations)
    /// - Min/Max: ~2ms for 1K docs (comparison operations)
    /// - GroupBy Simple: ~5ms for 1K docs (grouping overhead)
    /// - GroupBy With Aggregates: ~10ms for 1K docs (multiple passes)
    /// - GroupBy With Filter: ~12ms for 1K docs (filter + group + aggregate)
    ///
    /// Projections:
    /// - Single Field: ~0.8ms for 1K docs (less data copying)
    /// - Multiple Fields: ~1ms for 1K docs
    /// - With Calculation: ~2ms for 1K docs (computation overhead)
    ///
    /// Distinct:
    /// - Simple: ~2ms for 1K docs (hash set construction)
    /// - With Filter: ~3ms for 1K docs (filter + hash set)
    ///
    /// Joins:
    /// - In-Memory Join: ~50ms for 1K products × 100 orders (nested loops)
    /// - Recommendation: Pre-compute joins or use indexed lookups
    ///
    /// Top-N Queries:
    /// - Top 10 (sorted): ~2ms for 1K docs (sort + take 10)
    /// - Optimization: Use heap-based selection for very large datasets
    ///
    /// Existence Checks:
    /// - Any: ~0.1ms for 1K docs (short-circuits on first match)
    /// - All: ~1ms for 1K docs (must check every item)
    /// - First: ~0.1ms for 1K docs (short-circuits on first match)
    ///
    /// FluentQuery API:
    /// - Similar performance to LINQ (thin wrapper)
    /// - Slightly better for chained operations (query optimization)
    ///
    /// Key Insights:
    /// - All queries are O(n) full scans (no indexes in current implementation)
    /// - Adding indexes would improve filter/sort performance dramatically
    /// - String operations (Contains, StartsWith) are most expensive
    /// - Pagination with Skip is inefficient for large offsets
    /// - GroupBy with aggregates requires multiple passes over data
    /// - Consider caching query results for frequently-run queries
    /// </summary>
}
