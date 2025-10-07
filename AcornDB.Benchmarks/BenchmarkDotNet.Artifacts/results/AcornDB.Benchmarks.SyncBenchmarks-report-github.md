```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26100.6584/24H2/2024Update/HudsonValley)
AMD Ryzen 9 7900X 4.70GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 9.0.304
  [Host]     : .NET 9.0.9 (9.0.9, 9.0.925.41916), X64 RyuJIT x86-64-v4
  Job-SWDXLI : .NET 9.0.9 (9.0.9, 9.0.925.41916), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=3  UnrollFactor=1  
WarmupCount=2  

```
| Method                         | ItemCount | Mean             | Error         | StdDev        | Allocated |
|------------------------------- |---------- |-----------------:|--------------:|--------------:|----------:|
| **InProcessSync_Push**             | **100**       |        **627.57 μs** |   **7,938.52 μs** |    **435.137 μs** |  **81.36 KB** |
| InProcessSync_Bidirectional    | 100       |      1,003.00 μs |   1,616.04 μs |     88.581 μs |  90.57 KB |
| ExportChanges_Performance      | 100       |         35.73 μs |      50.11 μs |      2.747 μs |  52.13 KB |
| SquabbleResolution_Performance | 100       |  1,546,689.87 μs |  74,941.17 μs |  4,107.778 μs |  94.91 KB |
| **InProcessSync_Push**             | **500**       |      **3,469.03 μs** |   **1,461.80 μs** |     **80.126 μs** | **391.44 KB** |
| InProcessSync_Bidirectional    | 500       |      5,906.10 μs |   1,926.05 μs |    105.573 μs |  434.9 KB |
| ExportChanges_Performance      | 500       |        164.97 μs |     107.94 μs |      5.916 μs | 250.68 KB |
| SquabbleResolution_Performance | 500       |  7,770,619.20 μs | 161,222.09 μs |  8,837.127 μs | 468.46 KB |
| **InProcessSync_Push**             | **1000**      |      **7,575.20 μs** |   **7,298.93 μs** |    **400.079 μs** | **811.95 KB** |
| InProcessSync_Bidirectional    | 1000      |     12,927.07 μs |   2,455.58 μs |    134.599 μs | 899.79 KB |
| ExportChanges_Performance      | 1000      |        322.67 μs |      92.42 μs |      5.066 μs | 518.61 KB |
| SquabbleResolution_Performance | 1000      | 15,521,967.20 μs | 728,021.98 μs | 39,905.342 μs | 955.68 KB |
