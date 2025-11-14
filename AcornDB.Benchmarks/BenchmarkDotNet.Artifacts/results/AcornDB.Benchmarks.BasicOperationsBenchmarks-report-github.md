```

BenchmarkDotNet v0.15.4, Linux Linux Mint 22.2 (Zara)
AMD Ryzen 9 7900X 3.01GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 9.0.111
  [Host]     : .NET 9.0.10 (9.0.10, 9.0.1025.47515), X64 RyuJIT x86-64-v4
  Job-NTRUNJ : .NET 9.0.10 (9.0.10, 9.0.1025.47515), X64 RyuJIT x86-64-v4

IterationCount=5  WarmupCount=3  

```
| Method                             | Mean         | Error      | StdDev    | Gen0     | Gen1     | Allocated |
|----------------------------------- |-------------:|-----------:|----------:|---------:|---------:|----------:|
| Stash_MemoryTrunk_1000Items        |     1.863 ms |  0.0303 ms | 0.0047 ms | 242.1875 | 121.0938 |   3.86 MB |
| Stash_FileTrunk_1000Items          | 1,388.674 ms | 24.1529 ms | 6.2724 ms |        - |        - |   8.37 MB |
| Crack_MemoryTrunk_1000Items        |     1.951 ms |  0.0210 ms | 0.0054 ms | 242.1875 | 121.0938 |    3.9 MB |
| Toss_MemoryTrunk_1000Items         |     2.086 ms |  0.0328 ms | 0.0085 ms | 265.6250 | 167.9688 |   4.27 MB |
| StashAndCrack_Mixed_1000Operations |     1.925 ms |  0.0444 ms | 0.0069 ms | 242.1875 | 121.0938 |   3.89 MB |
