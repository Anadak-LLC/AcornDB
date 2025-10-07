```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26100.6584/24H2/2024Update/HudsonValley)
AMD Ryzen 9 7900X 4.70GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 9.0.304
  [Host]     : .NET 9.0.9 (9.0.9, 9.0.925.41916), X64 RyuJIT x86-64-v4
  Job-ARDWEO : .NET 9.0.9 (9.0.9, 9.0.925.41916), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=5  UnrollFactor=1  
WarmupCount=2  

```
| Method                        | ConflictCount | Mean        | Error        | StdDev     | Allocated  |
|------------------------------ |-------------- |------------:|-------------:|-----------:|-----------:|
| **Squabble_LocalWins**            | **100**           |   **501.60 μs** |   **977.054 μs** | **151.200 μs** |   **44.98 KB** |
| Squabble_IncomingWins         | 100           |   405.64 μs |   393.553 μs | 102.205 μs |   45.05 KB |
| Squabble_MixedResults         | 100           |   461.14 μs |   430.263 μs | 111.738 μs |   45.02 KB |
| Squabble_WithHistoryRetrieval | 100           |   462.43 μs |   104.613 μs |  16.189 μs |  139.21 KB |
| NoConflict_DirectStash        | 100           |    40.06 μs |     4.860 μs |   1.262 μs |   52.49 KB |
| **Squabble_LocalWins**            | **500**           | **3,759.64 μs** |   **270.773 μs** |  **70.319 μs** |   **223.1 KB** |
| Squabble_IncomingWins         | 500           | 3,833.86 μs |   811.377 μs | 210.712 μs |   226.3 KB |
| Squabble_MixedResults         | 500           | 3,974.80 μs |   440.060 μs | 114.282 μs |   224.7 KB |
| Squabble_WithHistoryRetrieval | 500           | 4,046.25 μs |   372.732 μs |  57.681 μs |  681.51 KB |
| NoConflict_DirectStash        | 500           |   189.78 μs |    52.455 μs |  13.622 μs |  244.79 KB |
| **Squabble_LocalWins**            | **1000**          | **8,162.40 μs** |   **502.395 μs** | **130.470 μs** |  **445.76 KB** |
| Squabble_IncomingWins         | 1000          | 7,938.00 μs | 1,525.291 μs | 396.113 μs |  452.87 KB |
| Squabble_MixedResults         | 1000          | 7,827.36 μs | 1,229.297 μs | 319.245 μs |  449.31 KB |
| Squabble_WithHistoryRetrieval | 1000          | 8,485.68 μs | 1,197.879 μs | 311.085 μs | 1379.13 KB |
| NoConflict_DirectStash        | 1000          |   371.45 μs |   140.748 μs |  21.781 μs |  504.91 KB |
