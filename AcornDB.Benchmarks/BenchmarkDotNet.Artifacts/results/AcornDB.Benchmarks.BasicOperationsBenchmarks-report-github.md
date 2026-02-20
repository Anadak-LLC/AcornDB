```

BenchmarkDotNet v0.15.4, macOS 26.2 (25C56) [Darwin 25.2.0]
Apple M4 Max, 1 CPU, 14 logical and 14 physical cores
.NET SDK 9.0.305
  [Host]     : .NET 9.0.12 (9.0.12, 9.0.1225.60609), Arm64 RyuJIT armv8.0-a
  Job-NTRUNJ : .NET 9.0.12 (9.0.12, 9.0.1225.60609), Arm64 RyuJIT armv8.0-a

IterationCount=5  WarmupCount=3  

```
| Method                                       | Mean         | Error       | StdDev      | Gen0      | Gen1     | Allocated |
|--------------------------------------------- |-------------:|------------:|------------:|----------:|---------:|----------:|
| Stash_MemoryTrunk_1000Items                  |     1.249 ms |   0.0096 ms |   0.0025 ms |  482.4219 | 240.2344 |   3.85 MB |
| Stash_FileTrunk_1000Items                    | 4,043.841 ms |  80.7498 ms |  12.4961 ms | 1000.0000 |        - |   8.39 MB |
| Stash_BPlusTreeTrunk_1000Items               |   794.398 ms | 275.8038 ms |  71.6254 ms |         - |        - |   4.95 MB |
| Crack_MemoryTrunk_1000Items                  |     1.308 ms |   0.0158 ms |   0.0024 ms |  486.3281 | 242.1875 |   3.89 MB |
| Crack_BPlusTreeTrunk_1000Items               |   826.296 ms | 128.0766 ms |  19.8200 ms |  500.0000 |        - |   4.99 MB |
| Toss_MemoryTrunk_1000Items                   |     1.392 ms |   0.0256 ms |   0.0066 ms |  533.2031 | 146.4844 |   4.26 MB |
| Toss_BPlusTreeTrunk_1000Items                | 8,459.080 ms | 493.6455 ms | 128.1982 ms |         - |        - |   7.65 MB |
| StashAndCrack_Mixed_1000Operations           |     1.226 ms |   0.0081 ms |   0.0021 ms |  486.3281 | 242.1875 |   3.88 MB |
| StashAndCrack_BPlusTree_Mixed_1000Operations |           NA |          NA |          NA |        NA |       NA |        NA |

Benchmarks with issues:
  BasicOperationsBenchmarks.StashAndCrack_BPlusTree_Mixed_1000Operations: Job-NTRUNJ(IterationCount=5, WarmupCount=3)
