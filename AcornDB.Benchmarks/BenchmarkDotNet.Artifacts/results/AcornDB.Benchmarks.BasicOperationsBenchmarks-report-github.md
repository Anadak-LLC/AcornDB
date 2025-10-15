```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26100.6584/24H2/2024Update/HudsonValley)
AMD Ryzen 9 7900X 4.70GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 9.0.304
  [Host]     : .NET 9.0.9 (9.0.9, 9.0.925.41916), X64 RyuJIT x86-64-v4
  Job-NTRUNJ : .NET 9.0.9 (9.0.9, 9.0.925.41916), X64 RyuJIT x86-64-v4

IterationCount=5  WarmupCount=3  

```
| Method                             | Mean           | Error         | StdDev       | Gen0    | Gen1    | Allocated |
|----------------------------------- |---------------:|--------------:|-------------:|--------:|--------:|----------:|
| Stash_MemoryTrunk_1000Items        |       299.1 μs |      17.02 μs |      4.42 μs | 33.2031 | 16.1133 | 546.88 KB |
| Stash_FileTrunk_1000Items          | 1,239,405.6 μs | 588,646.58 μs | 91,093.71 μs |       - |       - |   4454 KB |
| Crack_MemoryTrunk_1000Items        |       364.0 μs |       7.31 μs |      1.90 μs | 35.6445 | 17.5781 | 585.94 KB |
| Toss_MemoryTrunk_1000Items         |       477.9 μs |      11.06 μs |      2.87 μs | 55.6641 | 17.0898 | 914.06 KB |
| StashAndCrack_Mixed_1000Operations |       323.2 μs |       9.62 μs |      2.50 μs | 34.6680 | 11.2305 | 570.31 KB |
