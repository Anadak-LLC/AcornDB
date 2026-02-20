```

BenchmarkDotNet v0.15.4, macOS 26.2 (25C56) [Darwin 25.2.0]
Apple M4 Max, 1 CPU, 14 logical and 14 physical cores
.NET SDK 9.0.305
  [Host]     : .NET 9.0.12 (9.0.12, 9.0.1225.60609), Arm64 RyuJIT armv8.0-a
  Job-NTRUNJ : .NET 9.0.12 (9.0.12, 9.0.1225.60609), Arm64 RyuJIT armv8.0-a

IterationCount=5  WarmupCount=3  

```
| Method                             | Mean         | Error       | StdDev      | Gen0      | Gen1     | Allocated |
|----------------------------------- |-------------:|------------:|------------:|----------:|---------:|----------:|
| Stash_MemoryTrunk_1000Items        |     1.291 ms |   0.0225 ms |   0.0058 ms |  482.4219 | 240.2344 |   3.85 MB |
| Stash_FileTrunk_1000Items          | 4,594.756 ms | 431.8767 ms | 112.1570 ms | 1000.0000 |        - |   8.39 MB |
| Crack_MemoryTrunk_1000Items        |     1.335 ms |   0.0301 ms |   0.0047 ms |  486.3281 | 242.1875 |   3.89 MB |
| Toss_MemoryTrunk_1000Items         |     1.420 ms |   0.0341 ms |   0.0053 ms |  533.2031 | 142.5781 |   4.26 MB |
| StashAndCrack_Mixed_1000Operations |     1.267 ms |   0.0197 ms |   0.0051 ms |  486.3281 | 242.1875 |   3.88 MB |
