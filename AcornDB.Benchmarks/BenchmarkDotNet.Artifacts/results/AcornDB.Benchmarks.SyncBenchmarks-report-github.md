```

BenchmarkDotNet v0.15.4, Linux Linux Mint 22.2 (Zara)
AMD Ryzen 9 7900X 3.01GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 9.0.111
  [Host]     : .NET 9.0.10 (9.0.10, 9.0.1025.47515), X64 RyuJIT x86-64-v4
  Job-SWDXLI : .NET 9.0.10 (9.0.10, 9.0.1025.47515), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=3  UnrollFactor=1  
WarmupCount=2  

```
| Method                         | ItemCount | Mean         | Error     | StdDev    | Allocated   |
|------------------------------- |---------- |-------------:|----------:|----------:|------------:|
| **InProcessSync_Push**             | **100**       |     **2.115 ms** |  **4.333 ms** | **0.2375 ms** |  **1139.02 KB** |
| InProcessSync_Bidirectional    | 100       |     2.597 ms |  4.682 ms | 0.2567 ms |  1341.78 KB |
| ExportChanges_Performance      | 100       |     1.492 ms |  3.280 ms | 0.1798 ms |   780.34 KB |
| SquabbleResolution_Performance | 100       |   106.965 ms |  9.096 ms | 0.4986 ms |   777.68 KB |
| **InProcessSync_Push**             | **500**       |    **10.007 ms** | **15.984 ms** | **0.8761 ms** |  **5683.17 KB** |
| InProcessSync_Bidirectional    | 500       |    12.263 ms | 18.256 ms | 1.0007 ms |  6711.84 KB |
| ExportChanges_Performance      | 500       |     7.713 ms | 16.174 ms | 0.8865 ms |   3956.7 KB |
| SquabbleResolution_Performance | 500       |   530.245 ms | 16.626 ms | 0.9113 ms |  3874.84 KB |
| **InProcessSync_Push**             | **1000**      |    **21.053 ms** | **29.679 ms** | **1.6268 ms** | **11526.05 KB** |
| InProcessSync_Bidirectional    | 1000      |    23.549 ms | 28.580 ms | 1.5666 ms |  13451.9 KB |
| ExportChanges_Performance      | 1000      |    14.233 ms | 15.705 ms | 0.8608 ms |  7948.23 KB |
| SquabbleResolution_Performance | 1000      | 1,059.595 ms | 17.350 ms | 0.9510 ms |  7788.09 KB |
