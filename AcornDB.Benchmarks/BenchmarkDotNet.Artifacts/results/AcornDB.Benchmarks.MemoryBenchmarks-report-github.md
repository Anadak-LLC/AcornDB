```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26100.6584/24H2/2024Update/HudsonValley)
AMD Ryzen 9 7900X 4.70GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 9.0.304
  [Host]     : .NET 9.0.9 (9.0.9, 9.0.925.41916), X64 RyuJIT x86-64-v4
  Job-WWFMMQ : .NET 9.0.9 (9.0.9, 9.0.925.41916), X64 RyuJIT x86-64-v4

IterationCount=3  WarmupCount=1  

```
| Method                             | ItemCount | Mean         | Error          | StdDev        | Median       | Gen0       | Gen1       | Gen2      | Allocated |
|----------------------------------- |---------- |-------------:|---------------:|--------------:|-------------:|-----------:|-----------:|----------:|----------:|
| **MemoryUsage_LRU_Cache**              | **10000**     |     **5.193 ms** |       **5.205 ms** |     **0.2853 ms** |     **5.045 ms** |   **750.0000** |   **742.1875** |         **-** |  **12.05 MB** |
| MemoryUsage_Unlimited_Cache        | 10000     |     5.239 ms |       3.307 ms |     0.1813 ms |     5.325 ms |   750.0000 |   742.1875 |         - |  12.05 MB |
| MemoryUsage_NoEviction_Strategy    | 10000     |     4.211 ms |       6.099 ms |     0.3343 ms |     4.389 ms |   750.0000 |   742.1875 |         - |  12.05 MB |
| LRU_EvictionPerformance_100k_Items | 10000     | 1,826.977 ms |  27,776.985 ms | 1,522.5503 ms |   964.009 ms |  9000.0000 |  8000.0000 |         - |  189.2 MB |
| **MemoryUsage_LRU_Cache**              | **50000**     |   **567.904 ms** |     **162.102 ms** |     **8.8854 ms** |   **563.712 ms** |  **5000.0000** |  **4000.0000** |         **-** | **114.25 MB** |
| MemoryUsage_Unlimited_Cache        | 50000     |    60.532 ms |       5.212 ms |     0.2857 ms |    60.422 ms |  4888.8889 |  4777.7778 | 1111.1111 |  60.57 MB |
| MemoryUsage_NoEviction_Strategy    | 50000     |    60.230 ms |      31.887 ms |     1.7478 ms |    59.394 ms |  4888.8889 |  4777.7778 | 1111.1111 |  60.57 MB |
| LRU_EvictionPerformance_100k_Items | 50000     |   952.695 ms |     402.980 ms |    22.0887 ms |   960.706 ms |  9000.0000 |  8000.0000 |         - |  189.2 MB |
| **MemoryUsage_LRU_Cache**              | **100000**    | **5,942.961 ms** | **152,546.753 ms** | **8,361.6023 ms** | **1,124.482 ms** | **11000.0000** | **10000.0000** | **1000.0000** |  **228.6 MB** |
| MemoryUsage_Unlimited_Cache        | 100000    |   123.789 ms |      59.758 ms |     3.2755 ms |   125.054 ms |  8800.0000 |  8600.0000 | 1200.0000 | 121.22 MB |
| MemoryUsage_NoEviction_Strategy    | 100000    |   114.676 ms |      71.761 ms |     3.9335 ms |   113.434 ms |  8800.0000 |  8600.0000 | 1200.0000 | 121.23 MB |
| LRU_EvictionPerformance_100k_Items | 100000    | 1,245.888 ms |   8,843.549 ms |   484.7448 ms |   971.803 ms |  9000.0000 |  8000.0000 |         - |  189.2 MB |
