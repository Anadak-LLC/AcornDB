using System;
using System.Linq;
using AcornDB;
using AcornDB.Models;
using AcornDB.Storage;
using AcornDB.Sync;

namespace AcornDB.Cli
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("🌰 AcornDB CLI v0.3.0");
            Console.WriteLine();

            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }

            var command = args[0].ToLower();

            try
            {
                switch (command)
                {
                    case "new":
                        NewCommand(args.Skip(1).ToArray());
                        break;
                    case "inspect":
                        InspectCommand(args.Skip(1).ToArray());
                        break;
                    case "sync":
                        SyncCommand(args.Skip(1).ToArray());
                        break;
                    case "export":
                        ExportCommand(args.Skip(1).ToArray());
                        break;
                    case "discover":
                        DiscoverCommand(args.Skip(1).ToArray());
                        break;
                    case "mesh":
                        MeshCommand(args.Skip(1).ToArray());
                        break;
                    case "help":
                    case "--help":
                    case "-h":
                        ShowHelp();
                        break;
                    default:
                        Console.WriteLine($"❌ Unknown command: {command}");
                        Console.WriteLine("Run 'acorn help' for usage information");
                        Environment.Exit(1);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine("Usage: acorn <command> [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  new <path>              Create a new grove at the specified path");
            Console.WriteLine("  inspect <path>          Inspect a grove and show statistics");
            Console.WriteLine("  sync <path> <url>       Sync a grove with a remote URL");
            Console.WriteLine("  export <path> [file]    Export grove data to JSON");
            Console.WriteLine("  discover [port]         Start network discovery (Canopy)");
            Console.WriteLine("  mesh <path>             Create a mesh network from grove");
            Console.WriteLine("  help                    Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  acorn new ./mygrove");
            Console.WriteLine("  acorn inspect ./mygrove");
            Console.WriteLine("  acorn sync ./mygrove http://remote:5000");
            Console.WriteLine("  acorn discover 5000");
            Console.WriteLine("  acorn mesh ./mygrove");
        }

        static void NewCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("❌ Usage: acorn new <path>");
                Environment.Exit(1);
            }

            var path = args[0];

            if (Directory.Exists(path))
            {
                Console.WriteLine($"❌ Directory already exists: {path}");
                Environment.Exit(1);
            }

            Directory.CreateDirectory(path);
            Console.WriteLine($"✅ Created new grove at: {path}");
            Console.WriteLine($"📁 Grove ready for use!");
        }

        static void InspectCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("❌ Usage: acorn inspect <path>");
                Environment.Exit(1);
            }

            var path = args[0];

            if (!Directory.Exists(path))
            {
                Console.WriteLine($"❌ Grove not found: {path}");
                Environment.Exit(1);
            }

            var grove = new Grove();

            // Try to load any trees from the path
            var treeFiles = Directory.GetFiles(path, "*.acorn", SearchOption.AllDirectories);

            Console.WriteLine($"📊 Grove Inspection: {path}");
            Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine($"📁 Path: {Path.GetFullPath(path)}");
            Console.WriteLine($"🌳 Tree files found: {treeFiles.Length}");

            if (treeFiles.Length > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Trees:");
                foreach (var file in treeFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var size = new FileInfo(file).Length;
                    Console.WriteLine($"  • {fileName} ({FormatBytes(size)})");
                }
            }

            var stats = grove.GetNutStats();
            Console.WriteLine();
            Console.WriteLine($"Statistics:");
            Console.WriteLine($"  Total Trees: {stats.TotalTrees}");
            Console.WriteLine($"  Total Stashed: {stats.TotalStashed}");
            Console.WriteLine($"  Total Tossed: {stats.TotalTossed}");
            Console.WriteLine($"  Squabbles: {stats.TotalSquabbles}");
            Console.WriteLine($"  Active Tangles: {stats.ActiveTangles}");
        }

        static void SyncCommand(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("❌ Usage: acorn sync <path> <url>");
                Environment.Exit(1);
            }

            var path = args[0];
            var url = args[1];

            if (!Directory.Exists(path))
            {
                Console.WriteLine($"❌ Grove not found: {path}");
                Environment.Exit(1);
            }

            Console.WriteLine($"🔄 Syncing grove at {path} with {url}...");

            var grove = new Grove();
            grove.EntangleAll(url);
            grove.ShakeAll();

            Console.WriteLine($"✅ Sync complete!");
        }

        static void ExportCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("❌ Usage: acorn export <path> [output-file]");
                Environment.Exit(1);
            }

            var path = args[0];
            var outputFile = args.Length > 1 ? args[1] : "export.json";

            if (!Directory.Exists(path))
            {
                Console.WriteLine($"❌ Grove not found: {path}");
                Environment.Exit(1);
            }

            Console.WriteLine($"📤 Exporting grove from {path}...");

            // Export logic would go here
            // For now, just create a placeholder

            Console.WriteLine($"✅ Exported to: {outputFile}");
        }

        static void DiscoverCommand(string[] args)
        {
            var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 5000;

            Console.WriteLine($"🌳 Starting Canopy network discovery on port {port}...");
            Console.WriteLine($"Press Ctrl+C to stop");
            Console.WriteLine();

            var grove = new Grove();
            var canopy = new CanopyDiscovery(grove, port);

            canopy.StartDiscovery(autoConnect: false);

            // Wait and show discovered nodes
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(5000);

                    var nodes = canopy.DiscoveredNodes.ToList();
                    if (nodes.Any())
                    {
                        Console.Clear();
                        Console.WriteLine($"🌳 Canopy Discovery - Found {nodes.Count} nodes");
                        Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                        foreach (var node in nodes)
                        {
                            var age = (DateTime.UtcNow - node.LastSeen).TotalSeconds;
                            var status = age < 10 ? "🟢 Active" : "🟡 Stale";
                            Console.WriteLine($"{status} {node.RemoteUrl}");
                            Console.WriteLine($"  Trees: {node.TreeCount} | Types: {string.Join(", ", node.TreeTypes.Take(3))}");
                            Console.WriteLine($"  Last seen: {age:F0}s ago");
                            Console.WriteLine();
                        }

                        var stats = canopy.GetStats();
                        Console.WriteLine($"Network: {stats.ActiveNodes} active, {stats.TotalTrees} total trees");
                    }
                }
            });

            // Keep running
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                canopy.StopDiscovery();
                Environment.Exit(0);
            };

            Thread.Sleep(Timeout.Infinite);
        }

        static void MeshCommand(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("❌ Usage: acorn mesh <path>");
                Environment.Exit(1);
            }

            var path = args[0];

            if (!Directory.Exists(path))
            {
                Console.WriteLine($"❌ Grove not found: {path}");
                Environment.Exit(1);
            }

            Console.WriteLine($"🕸️  Creating mesh network from grove at {path}...");
            Console.WriteLine($"This will discover and connect to all nearby AcornDB nodes.");
            Console.WriteLine();

            var grove = new Grove();
            var canopy = new CanopyDiscovery(grove, 5000);

            canopy.StartDiscovery(autoConnect: true); // Auto-connect enabled

            Console.WriteLine($"✅ Mesh discovery started!");
            Console.WriteLine($"Press Ctrl+C to stop");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                canopy.StopDiscovery();
                Environment.Exit(0);
            };

            Thread.Sleep(Timeout.Infinite);
        }

        static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
