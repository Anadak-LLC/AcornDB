```

BenchmarkDotNet v0.15.4, Linux Linux Mint 22.2 (Zara)
AMD Ryzen 9 7900X 3.01GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 9.0.111
  [Host]     : .NET 9.0.10 (9.0.10, 9.0.1025.47515), X64 RyuJIT x86-64-v4
  Job-ARDWEO : .NET 9.0.10 (9.0.10, 9.0.1025.47515), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=5  UnrollFactor=1  
WarmupCount=2  

```
| Method                        | ConflictCount | Mean         | Error        | StdDev     | Allocated  |
|------------------------------ |-------------- |-------------:|-------------:|-----------:|-----------:|
| **Squabble_LocalWins**            | **100**           |     **60.21 μs** |     **2.057 μs** |   **0.318 μs** |   **30.47 KB** |
| Squabble_IncomingWins         | 100           |    552.76 μs |   101.657 μs |  26.400 μs |  372.19 KB |
| Squabble_MixedResults         | 100           |    317.00 μs |    35.640 μs |   9.256 μs |  201.33 KB |
| Squabble_WithHistoryRetrieval | 100           |  1,776.11 μs |   792.294 μs | 205.756 μs |   842.9 KB |
| NoConflict_DirectStash        | 100           |    661.98 μs |   258.026 μs |  67.009 μs |   429.8 KB |
| **Squabble_LocalWins**            | **500**           |    **308.87 μs** |     **6.869 μs** |   **1.784 μs** |  **152.34 KB** |
| Squabble_IncomingWins         | 500           |  2,621.77 μs |    54.864 μs |   8.490 μs | 1865.41 KB |
| Squabble_MixedResults         | 500           |  1,440.02 μs |   133.391 μs |  20.642 μs | 1008.93 KB |
| Squabble_WithHistoryRetrieval | 500           |  8,040.69 μs |   262.025 μs |  40.549 μs | 4246.85 KB |
| NoConflict_DirectStash        | 500           |  2,750.42 μs |    28.850 μs |   4.465 μs | 2178.53 KB |
| **Squabble_LocalWins**            | **1000**          |    **600.77 μs** |     **7.173 μs** |   **1.110 μs** |  **304.69 KB** |
| Squabble_IncomingWins         | 1000          |  5,116.22 μs |   952.531 μs | 147.405 μs | 3735.73 KB |
| Squabble_MixedResults         | 1000          |  2,846.06 μs |    37.692 μs |   5.833 μs | 2020.27 KB |
| Squabble_WithHistoryRetrieval | 1000          | 17,224.06 μs | 4,781.340 μs | 739.918 μs | 8643.24 KB |
| NoConflict_DirectStash        | 1000          |  5,862.67 μs | 1,523.053 μs | 235.694 μs | 4385.23 KB |
