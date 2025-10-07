```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26100.6584/24H2/2024Update/HudsonValley)
AMD Ryzen 9 7900X 4.70GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 9.0.304
  [Host]     : .NET 9.0.9 (9.0.9, 9.0.925.41916), X64 RyuJIT x86-64-v4
  Job-NTRUNJ : .NET 9.0.9 (9.0.9, 9.0.925.41916), X64 RyuJIT x86-64-v4

IterationCount=5  WarmupCount=3  

```
| Method                             | Mean           | Error           | StdDev        | Median         | Gen0    | Gen1   | Allocated  |
|----------------------------------- |---------------:|----------------:|--------------:|---------------:|--------:|-------:|-----------:|
| Stash_MemoryTrunk_1000Items        |       218.3 μs |         3.57 μs |       0.55 μs |       218.4 μs | 12.6953 | 6.1035 |  210.94 KB |
| Stash_FileTrunk_1000Items          | 1,642,575.7 μs | 2,309,442.55 μs | 599,754.90 μs | 2,061,451.1 μs |       - |      - | 4022.38 KB |
| Crack_MemoryTrunk_1000Items        |       294.7 μs |         1.47 μs |       0.38 μs |       294.7 μs | 15.1367 | 7.3242 |     250 KB |
| Toss_MemoryTrunk_1000Items         |       318.9 μs |         6.41 μs |       1.67 μs |       318.9 μs | 15.1367 | 4.3945 |     250 KB |
| StashAndCrack_Mixed_1000Operations |       250.1 μs |         6.00 μs |       1.56 μs |       249.4 μs | 14.1602 | 4.3945 |  234.38 KB |
