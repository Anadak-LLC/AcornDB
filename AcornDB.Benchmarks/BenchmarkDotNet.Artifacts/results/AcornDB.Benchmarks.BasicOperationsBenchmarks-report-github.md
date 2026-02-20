```

BenchmarkDotNet v0.15.4, macOS 26.2 (25C56) [Darwin 25.2.0]
Apple M4 Max, 1 CPU, 14 logical and 14 physical cores
.NET SDK 9.0.305
  [Host]     : .NET 9.0.12 (9.0.12, 9.0.1225.60609), Arm64 RyuJIT armv8.0-a
  Job-MJLQTR : .NET 9.0.12 (9.0.12, 9.0.1225.60609), Arm64 RyuJIT armv8.0-a

InvocationCount=1  IterationCount=5  UnrollFactor=1  
WarmupCount=3  

```
| Method                                    | ItemCount | Case    | Mean            | Error          | StdDev         | Gen0      | Allocated |
|------------------------------------------ |---------- |-------- |----------------:|---------------:|---------------:|----------:|----------:|
| **&#39;Stash: insert N new items&#39;**               | **1000**      | **Bitcask** |    **23,683.51 μs** |  **12,314.119 μs** |   **3,197.938 μs** |         **-** | **4924240 B** |
| &#39;Crack: read N items by id&#39;               | 1000      | Bitcask |        52.55 μs |       7.281 μs |       1.891 μs |         - |         - |
| &#39;Toss: delete N items&#39;                    | 1000      | Bitcask |       394.30 μs |      46.222 μs |       7.153 μs |         - |  328000 B |
| &#39;Mixed: stash+crack+update for N/2 items&#39; | 1000      | Bitcask |    23,434.29 μs |   4,267.610 μs |     660.417 μs |         - | 4803104 B |
| **&#39;Stash: insert N new items&#39;**               | **1000**      | **File**    | **4,903,659.37 μs** | **721,623.244 μs** | **187,403.265 μs** | **1000.0000** | **8946888 B** |
| &#39;Crack: read N items by id&#39;               | 1000      | File    |       114.92 μs |     148.668 μs |      38.609 μs |         - |         - |
| &#39;Toss: delete N items&#39;                    | 1000      | File    |    74,941.77 μs |  12,679.735 μs |   3,292.887 μs |         - |  823200 B |
| &#39;Mixed: stash+crack+update for N/2 items&#39; | 1000      | File    | 4,677,407.64 μs | 813,848.462 μs | 211,353.862 μs | 1000.0000 | 8868088 B |
| **&#39;Stash: insert N new items&#39;**               | **1000**      | **Memory**  |     **5,016.28 μs** |     **959.077 μs** |     **249.069 μs** |         **-** | **4298664 B** |
| &#39;Crack: read N items by id&#39;               | 1000      | Memory  |        59.22 μs |      26.849 μs |       4.155 μs |         - |         - |
| &#39;Toss: delete N items&#39;                    | 1000      | Memory  |       408.40 μs |      82.975 μs |      12.840 μs |         - |  328000 B |
| &#39;Mixed: stash+crack+update for N/2 items&#39; | 1000      | Memory  |     4,662.87 μs |   1,381.911 μs |     358.878 μs |         - | 4021016 B |
