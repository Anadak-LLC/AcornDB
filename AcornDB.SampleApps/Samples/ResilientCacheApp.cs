using AcornDB;
using AcornDB.Storage;
using Spectre.Console;

namespace AcornDB.SampleApps.Samples;

/// <summary>
/// Sample 5: Resilient Cache Demo
///
/// Demonstrates:
/// - ResilientTrunk with retry logic
/// - Fallback trunk for high availability
/// - Circuit breaker pattern
/// - Production resilience strategies
/// - Real-time statistics
/// </summary>
public static class ResilientCacheApp
{
    private record CacheEntry(
        string Key,
        string Value,
        DateTime CachedAt,
        int AccessCount = 0
    );

    public static async Task Run()
    {
        AnsiConsole.Clear();

        // AcornDB themed header
        var rule = new Rule("[tan bold]Resilient Cache Demo[/]")
        {
            Justification = Justify.Left,
            Style = Style.Parse("tan")
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        var infoPanel = new Panel(
            new Markup(
                "[dim]This demo showcases production resilience features:[/]\n\n" +
                "  [olive]•[/] [white]Automatic retry[/] [dim]with exponential backoff[/]\n" +
                "  [olive]•[/] [white]Fallback[/] [dim]to secondary storage[/]\n" +
                "  [olive]•[/] [white]Circuit breaker[/] [dim]pattern[/]\n" +
                "  [olive]•[/] [white]Health monitoring[/] [dim]and statistics[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("olive"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(infoPanel);
        AnsiConsole.WriteLine();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[tan bold]Choose a resilience demo:[/]")
                    .PageSize(10)
                    .AddChoices(new[] {
                        "Demo: Retry Logic (simulated failures)",
                        "Demo: Fallback Mechanism",
                        "Demo: Circuit Breaker",
                        "Interactive: Production Cache with Resilience",
                        "Back to Main Menu"
                    }));

            AnsiConsole.WriteLine();

            switch (choice)
            {
                case "Demo: Retry Logic (simulated failures)":
                    await DemoRetryLogic();
                    break;
                case "Demo: Fallback Mechanism":
                    await DemoFallback();
                    break;
                case "Demo: Circuit Breaker":
                    await DemoCircuitBreaker();
                    break;
                case "Interactive: Production Cache with Resilience":
                    await InteractiveCache();
                    break;
                case "Back to Main Menu":
                    return;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static async Task DemoRetryLogic()
    {
        AnsiConsole.Write(new Rule("[tan]Automatic Retry Logic[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]Creating unreliable trunk (30% failure rate)...[/]");
        AnsiConsole.WriteLine();

        var unreliableTrunk = new UnreliableTrunk<CacheEntry>(failureRate: 0.3);
        var resilientTrunk = unreliableTrunk.WithResilience(new ResilienceOptions
        {
            MaxRetries = 3,
            BaseRetryDelayMs = 50,
            RetryStrategy = RetryStrategy.ExponentialBackoff,
            EnableCircuitBreaker = false
        });

        var tree = new Tree<CacheEntry>(resilientTrunk);

        AnsiConsole.MarkupLine("[olive]Attempting 10 operations with automatic retry...[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Tan)
            .AddColumn(new TableColumn("[tan bold]#[/]").Centered())
            .AddColumn(new TableColumn("[tan bold]Operation[/]"))
            .AddColumn(new TableColumn("[tan bold]Result[/]"));

        int successCount = 0;
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var entry = new CacheEntry($"key-{i}", $"value-{i}", DateTime.UtcNow);
                tree.Stash($"entry-{i}", entry);
                table.AddRow($"[dim]{i + 1}[/]", $"[dim]Saved entry-{i}[/]", "[green]✓ Success[/]");
                successCount++;
            }
            catch (Exception ex)
            {
                table.AddRow($"[dim]{i + 1}[/]", $"[dim]Saved entry-{i}[/]", $"[red]✗ {Markup.Escape(ex.Message)}[/]");
            }

            await Task.Delay(50);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var stats = resilientTrunk.GetStats();

        var statsPanel = new Panel(
            new Markup(
                $"[tan bold]Resilience Statistics[/]\n\n" +
                $"[dim]Success Rate:[/] [green]{successCount}/10 ({successCount * 10}%)[/]\n" +
                $"[dim]Total Retries:[/] [yellow]{stats.TotalRetries}[/]\n" +
                $"[dim]Circuit State:[/] [olive]{stats.CircuitState}[/]\n\n" +
                $"[green]✓ With retry logic, most operations succeed despite failures![/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("green"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(statsPanel);

        resilientTrunk.Dispose();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static async Task DemoFallback()
    {
        AnsiConsole.Write(new Rule("[tan]Fallback Mechanism[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]Scenario: Primary storage fails, fallback to in-memory cache...[/]");
        AnsiConsole.WriteLine();

        // Primary trunk configured to always fail
        var primaryTrunk = new UnreliableTrunk<CacheEntry>(failureRate: 1.0);

        // Fallback to reliable in-memory trunk
        var fallbackTrunk = new MemoryTrunk<CacheEntry>();

        var resilientTrunk = primaryTrunk.WithFallback(
            fallbackTrunk,
            ResilienceOptions.Conservative
        );

        var tree = new Tree<CacheEntry>(resilientTrunk);

        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("olive"))
            .Start("[dim]Writing 5 entries (primary will fail, fallback will succeed)...[/]", async ctx =>
            {
                await Task.Delay(500);

                for (int i = 0; i < 5; i++)
                {
                    var entry = new CacheEntry($"key-{i}", $"value-{i}", DateTime.UtcNow);
                    tree.Stash($"entry-{i}", entry);
                    ctx.Status($"[green]✓ Saved entry-{i} (via fallback)[/]");
                    await Task.Delay(200);
                }
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[olive]Reading entries back...[/]");

        var readTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Tan)
            .AddColumn(new TableColumn("[tan bold]Entry[/]"))
            .AddColumn(new TableColumn("[tan bold]Value[/]"));

        for (int i = 0; i < 5; i++)
        {
            var entry = tree.Crack($"entry-{i}");
            readTable.AddRow($"[yellow]entry-{i}[/]", $"[green]{Markup.Escape(entry?.Value ?? "null")}[/]");
        }

        AnsiConsole.Write(readTable);
        AnsiConsole.WriteLine();

        var stats = resilientTrunk.GetStats();

        var statsPanel = new Panel(
            new Markup(
                $"[tan bold]Resilience Statistics[/]\n\n" +
                $"[dim]Total Fallbacks:[/] [yellow]{stats.TotalFallbacks}[/]\n" +
                $"[dim]System Healthy:[/] {(stats.IsHealthy ? "[green]✓ Yes[/]" : "[red]✗ No[/]")}\n\n" +
                $"[green]✓ Application continues functioning despite primary failure![/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("green"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(statsPanel);

        resilientTrunk.Dispose();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static async Task DemoCircuitBreaker()
    {
        AnsiConsole.Write(new Rule("[tan]Circuit Breaker Pattern[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]Scenario: High failure rate triggers circuit breaker...[/]");
        AnsiConsole.WriteLine();

        var primaryTrunk = new UnreliableTrunk<CacheEntry>(failureRate: 0.8);
        var fallbackTrunk = new MemoryTrunk<CacheEntry>();

        var resilientTrunk = primaryTrunk.WithFallback(
            fallbackTrunk,
            new ResilienceOptions
            {
                MaxRetries = 1,
                EnableCircuitBreaker = true,
                CircuitBreakerThreshold = 3,
                CircuitBreakerTimeout = TimeSpan.FromSeconds(3)
            }
        );

        var tree = new Tree<CacheEntry>(resilientTrunk);

        AnsiConsole.MarkupLine("[olive]Performing operations with 80% failure rate...[/]");
        AnsiConsole.MarkupLine("[dim]Circuit breaker will open after 3 failures.[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Tan)
            .AddColumn(new TableColumn("[tan bold]#[/]").Centered())
            .AddColumn(new TableColumn("[tan bold]Circuit State[/]"))
            .AddColumn(new TableColumn("[tan bold]Failures[/]").Centered());

        for (int i = 0; i < 10; i++)
        {
            var entry = new CacheEntry($"key-{i}", $"value-{i}", DateTime.UtcNow);
            tree.Stash($"entry-{i}", entry);

            var stats = resilientTrunk.GetStats();
            var stateColor = stats.CircuitState == CircuitBreakerState.Open ? "red" :
                            stats.CircuitState == CircuitBreakerState.HalfOpen ? "yellow" : "green";
            var status = stats.CircuitState == CircuitBreakerState.Open ? "OPEN (using fallback)" :
                        stats.CircuitState == CircuitBreakerState.HalfOpen ? "HALF-OPEN (testing)" :
                        "CLOSED (normal)";

            table.AddRow(
                $"[dim]{i + 1}[/]",
                $"[{stateColor}]{status}[/]",
                $"[yellow]{stats.FailureCount}[/]");

            await Task.Delay(100);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var finalStats = resilientTrunk.GetStats();

        var stateColor2 = finalStats.CircuitState == CircuitBreakerState.Open ? "red" :
                         finalStats.CircuitState == CircuitBreakerState.HalfOpen ? "yellow" : "green";

        var statsPanel = new Panel(
            new Markup(
                $"[tan bold]Final Circuit Breaker State[/]\n\n" +
                $"[dim]State:[/] [{stateColor2}]{finalStats.CircuitState}[/]\n" +
                $"[dim]Failures:[/] [yellow]{finalStats.FailureCount}[/]\n" +
                $"[dim]Trips:[/] [red]{finalStats.CircuitBreakerTrips}[/]\n" +
                $"[dim]Fallbacks:[/] [olive]{finalStats.TotalFallbacks}[/]\n\n" +
                (finalStats.CircuitState == CircuitBreakerState.Open
                    ? "[red]Circuit OPEN - Automatically using fallback to prevent cascading failures![/]"
                    : "[green]✓ Circuit operational[/]")))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(stateColor2),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(statsPanel);

        resilientTrunk.Dispose();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static async Task InteractiveCache()
    {
        AnsiConsole.Write(new Rule("[tan]Interactive Production Cache[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var failureRate = AnsiConsole.Ask("[green]Choose failure rate[/] [dim](0.0-1.0, e.g., 0.2 for 20%):[/]", 0.2);

        var primaryTrunk = new UnreliableTrunk<CacheEntry>(failureRate);
        var fallbackTrunk = new MemoryTrunk<CacheEntry>();
        var resilientTrunk = primaryTrunk.WithFallback(fallbackTrunk, ResilienceOptions.Default);
        var tree = new Tree<CacheEntry>(resilientTrunk);

        var initPanel = new Panel(
            new Markup(
                $"[green]✓ Cache initialized[/] [dim]with {failureRate * 100:F0}% simulated failure rate[/]\n" +
                $"[green]✓ Resilience:[/] [dim]3 retries, fallback enabled, circuit breaker active[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("green"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(initPanel);
        AnsiConsole.WriteLine();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[tan bold]Cache Operations:[/]")
                    .PageSize(10)
                    .AddChoices(new[] {
                        "Set Value",
                        "Get Value",
                        "List All",
                        "View Resilience Stats",
                        "Reset Circuit Breaker",
                        "Back"
                    }));

            AnsiConsole.WriteLine();

            switch (choice)
            {
                case "Set Value":
                    var key = AnsiConsole.Ask<string>("[green]Key:[/]");
                    var value = AnsiConsole.Ask<string>("[green]Value:[/]");
                    try
                    {
                        tree.Stash(key, new CacheEntry(key, value, DateTime.UtcNow));
                        AnsiConsole.MarkupLine("[green]✓ Cached successfully[/]");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]✗ Failed: {Markup.Escape(ex.Message)}[/]");
                    }
                    break;

                case "Get Value":
                    var getKey = AnsiConsole.Ask<string>("[green]Key:[/]");
                    try
                    {
                        var entry = tree.Crack(getKey);
                        if (entry != null)
                        {
                            var resultPanel = new Panel(
                                new Markup(
                                    $"[tan bold]Value:[/] [white]{Markup.Escape(entry.Value)}[/]\n" +
                                    $"[dim]Cached at: {entry.CachedAt:g}[/]"))
                            {
                                Border = BoxBorder.Rounded,
                                BorderStyle = Style.Parse("green"),
                                Padding = new Padding(1, 0)
                            };
                            AnsiConsole.Write(resultPanel);
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[yellow]Not found[/]");
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]✗ Failed: {Markup.Escape(ex.Message)}[/]");
                    }
                    break;

                case "List All":
                    var entries = tree.NutShells().ToList();

                    if (entries.Any())
                    {
                        var listTable = new Table()
                            .Border(TableBorder.Rounded)
                            .BorderColor(Color.Tan)
                            .AddColumn(new TableColumn("[tan bold]Key[/]"))
                            .AddColumn(new TableColumn("[tan bold]Value[/]"))
                            .AddColumn(new TableColumn("[tan bold]Cached At[/]"));

                        foreach (var nut in entries)
                        {
                            var e = nut.Payload;
                            listTable.AddRow(
                                $"[yellow]{Markup.Escape(e.Key)}[/]",
                                $"[white]{Markup.Escape(e.Value)}[/]",
                                $"[dim]{e.CachedAt:g}[/]");
                        }

                        AnsiConsole.Write(listTable);
                        AnsiConsole.MarkupLine($"\n[tan]Total:[/] {entries.Count} entries");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[dim]No cached entries[/]");
                    }
                    break;

                case "View Resilience Stats":
                    var stats = resilientTrunk.GetStats();
                    var stateColor = stats.CircuitState == CircuitBreakerState.Open ? "red" :
                                    stats.CircuitState == CircuitBreakerState.HalfOpen ? "yellow" : "green";

                    var statsTable = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Tan)
                        .AddColumn(new TableColumn("[tan bold]Metric[/]"))
                        .AddColumn(new TableColumn("[tan bold]Value[/]"));

                    statsTable.AddRow("[dim]Circuit State[/]", $"[{stateColor}]{stats.CircuitState}[/]");
                    statsTable.AddRow("[dim]Total Retries[/]", $"[yellow]{stats.TotalRetries}[/]");
                    statsTable.AddRow("[dim]Total Fallbacks[/]", $"[olive]{stats.TotalFallbacks}[/]");
                    statsTable.AddRow("[dim]Circuit Trips[/]", $"[red]{stats.CircuitBreakerTrips}[/]");
                    statsTable.AddRow("[dim]Failure Count[/]", $"[yellow]{stats.FailureCount}[/]");
                    statsTable.AddRow("[dim]Healthy[/]", stats.IsHealthy ? "[green]✓ Yes[/]" : "[red]✗ No[/]");

                    AnsiConsole.Write(statsTable);
                    break;

                case "Reset Circuit Breaker":
                    resilientTrunk.ResetCircuitBreaker();
                    AnsiConsole.MarkupLine("[green]✓ Circuit breaker reset to CLOSED state[/]");
                    break;

                case "Back":
                    resilientTrunk.Dispose();
                    return;
            }

            AnsiConsole.WriteLine();
        }
    }

    // Simulated unreliable trunk for demo purposes
    private class UnreliableTrunk<T> : ITrunk<T>, IDisposable where T : class
    {
        private readonly MemoryTrunk<T> _inner = new();
        private readonly double _failureRate;
        private readonly Random _random = new();

        public UnreliableTrunk(double failureRate)
        {
            _failureRate = Math.Clamp(failureRate, 0.0, 1.0);
        }

        private void SimulateFailure()
        {
            if (_random.NextDouble() < _failureRate)
            {
                throw new IOException("Simulated transient failure");
            }
        }

        public void Stash(string id, Nut<T> nut)
        {
            SimulateFailure();
            _inner.Stash(id, nut);
        }

        [Obsolete("Use Stash() instead. This method will be removed in a future version.")]
        public void Save(string id, Nut<T> nut) => Stash(id, nut);

        public Nut<T>? Crack(string id)
        {
            SimulateFailure();
            return _inner.Crack(id);
        }

        [Obsolete("Use Crack() instead. This method will be removed in a future version.")]
        public Nut<T>? Load(string id) => Crack(id);

        public void Toss(string id)
        {
            SimulateFailure();
            _inner.Toss(id);
        }

        [Obsolete("Use Toss() instead. This method will be removed in a future version.")]
        public void Delete(string id) => Toss(id);

        public IEnumerable<Nut<T>> CrackAll()
        {
            SimulateFailure();
            return _inner.CrackAll();
        }

        [Obsolete("Use CrackAll() instead. This method will be removed in a future version.")]
        public IEnumerable<Nut<T>> LoadAll() => CrackAll();

        public IReadOnlyList<Nut<T>> GetHistory(string id) => _inner.GetHistory(id);

        public IReadOnlyList<IRoot> Roots => _inner.Roots;
        public void AddRoot(IRoot root) => _inner.AddRoot(root);
        public bool RemoveRoot(string name) => _inner.RemoveRoot(name);
        public IEnumerable<Nut<T>> ExportChanges() => _inner.ExportChanges();
        public void ImportChanges(IEnumerable<Nut<T>> incoming) => _inner.ImportChanges(incoming);
        public ITrunkCapabilities Capabilities { get; } = new TrunkCapabilities
        {
            SupportsHistory = false,
            SupportsSync = true,
            IsDurable = false,
            SupportsAsync = false,
            SupportsNativeIndexes = false,
            SupportsFullTextSearch = false,
            SupportsComputedIndexes = false,
            TrunkType = "UnreliableTrunk (Demo)"
        };

        public bool SupportsHistory => false;
        public bool SupportsSync => true;
        public bool IsDurable => false;
        public bool SupportsAsync => false;
        public bool SupportsNativeIndexes => false;
        public bool SupportsFullTextSearch => false;
        public bool SupportsComputedIndexes => false;
        public string TrunkType => "UnreliableTrunk (Demo)";

        public void Dispose() { }
    }
}
