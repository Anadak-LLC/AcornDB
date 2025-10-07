using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace AcornDB.Benchmarks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("🌰 AcornDB Performance Benchmarks");
            Console.WriteLine("==================================\n");

            if (args.Length > 0 && args[0] == "--help")
            {
                ShowHelp();
                return;
            }

            // Run specific benchmark if specified
            if (args.Length > 0)
            {
                switch (args[0].ToLower())
                {
                    case "basic":
                        BenchmarkRunner.Run<BasicOperationsBenchmarks>();
                        break;
                    case "memory":
                        BenchmarkRunner.Run<MemoryBenchmarks>();
                        break;
                    case "sync":
                        BenchmarkRunner.Run<SyncBenchmarks>();
                        break;
                    case "conflict":
                        BenchmarkRunner.Run<ConflictResolutionBenchmarks>();
                        break;
                    case "all":
                        RunAllBenchmarks();
                        break;
                    default:
                        Console.WriteLine($"Unknown benchmark: {args[0]}");
                        ShowHelp();
                        break;
                }
            }
            else
            {
                // Default: run all benchmarks
                RunAllBenchmarks();
            }
        }

        private static void RunAllBenchmarks()
        {
            Console.WriteLine("Running all benchmarks...\n");

            var summary1 = BenchmarkRunner.Run<BasicOperationsBenchmarks>();
            var summary2 = BenchmarkRunner.Run<MemoryBenchmarks>();
            var summary3 = BenchmarkRunner.Run<SyncBenchmarks>();
            var summary4 = BenchmarkRunner.Run<ConflictResolutionBenchmarks>();

            Console.WriteLine("\n✅ All benchmarks completed!");
            Console.WriteLine("\nResults saved to: ./BenchmarkDotNet.Artifacts/results/");
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Usage: dotnet run [benchmark-name]");
            Console.WriteLine("\nAvailable benchmarks:");
            Console.WriteLine("  basic     - Basic operations (Stash/Crack/Toss)");
            Console.WriteLine("  memory    - Memory usage and cache eviction");
            Console.WriteLine("  sync      - Sync performance (in-process)");
            Console.WriteLine("  conflict  - Conflict resolution (Squabble)");
            Console.WriteLine("  all       - Run all benchmarks (default)");
            Console.WriteLine("\nExamples:");
            Console.WriteLine("  dotnet run");
            Console.WriteLine("  dotnet run basic");
            Console.WriteLine("  dotnet run memory");
        }
    }
}
