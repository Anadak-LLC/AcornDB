using AcornDB;
using AcornDB.Storage;
using AcornDB.Metrics;
using Spectre.Console;
using System.Diagnostics;

namespace AcornDB.SampleApps.Samples;

/// <summary>
/// Sample 6: Metrics Monitoring Dashboard
///
/// Demonstrates:
/// - MetricsCollector for operation tracking
/// - Real-time metrics visualization
/// - Prometheus and OpenTelemetry export formats
/// - Per-tree metrics tracking
/// - Live dashboard with statistics
/// </summary>
public static class MetricsMonitoringApp
{
    private record SensorReading(
        string SensorId,
        double Temperature,
        double Humidity,
        DateTime Timestamp
    );

    private record SystemEvent(
        string EventType,
        string Message,
        string Severity,
        DateTime Timestamp
    );

    public static async Task Run()
    {
        AnsiConsole.Clear();

        // AcornDB themed header
        var rule = new Rule("[tan bold]Metrics Monitoring Dashboard[/]")
        {
            Justification = Justify.Left,
            Style = Style.Parse("tan")
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        var infoPanel = new Panel(
            new Markup(
                "[dim]This demo showcases AcornDB's observability features:[/]\n\n" +
                "  [olive]•[/] [white]Real-time operation metrics[/]\n" +
                "  [olive]•[/] [white]Prometheus-compatible[/] [dim]export format[/]\n" +
                "  [olive]•[/] [white]Per-tree[/] [dim]performance tracking[/]\n" +
                "  [olive]•[/] [white]Live dashboard[/] [dim]visualization[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("olive"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(infoPanel);
        AnsiConsole.WriteLine();

        // Setup: Multiple trees with automatic metrics tracking
        var sensorTree = new Tree<SensorReading>(
            new DocumentStoreTrunk<SensorReading>("data/sample-sensors")
        );

        var eventTree = new Tree<SystemEvent>(
            new DocumentStoreTrunk<SystemEvent>("data/sample-events")
        );

        // Register trees for metrics tracking
        MetricsCollector.Instance.RegisterTree("SensorTree", typeof(SensorReading).Name);
        MetricsCollector.Instance.RegisterTree("EventTree", typeof(SystemEvent).Name);

        AnsiConsole.MarkupLine("[dim]✓ Initialized metrics tracking for 2 trees[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[tan bold]What would you like to do?[/]")
                    .PageSize(12)
                    .AddChoices(new[] {
                        "Simulate Sensor Data (generates metrics)",
                        "Simulate System Events (generates metrics)",
                        "View Live Dashboard",
                        "Export Metrics (Prometheus Format)",
                        "Export Metrics (JSON Format)",
                        "View Tree Statistics",
                        "Run Load Test (bulk operations)",
                        "Back to Main Menu"
                    }));

            AnsiConsole.WriteLine();

            switch (choice)
            {
                case "Simulate Sensor Data (generates metrics)":
                    await SimulateSensorData(sensorTree);
                    break;
                case "Simulate System Events (generates metrics)":
                    await SimulateSystemEvents(eventTree);
                    break;
                case "View Live Dashboard":
                    await ViewLiveDashboard(sensorTree, eventTree);
                    break;
                case "Export Metrics (Prometheus Format)":
                    ExportPrometheusFormat();
                    break;
                case "Export Metrics (JSON Format)":
                    ExportJsonFormat();
                    break;
                case "View Tree Statistics":
                    ViewTreeStats(sensorTree, eventTree);
                    break;
                case "Run Load Test (bulk operations)":
                    await RunLoadTest(sensorTree, eventTree);
                    break;
                case "Back to Main Menu":
                    return;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static async Task SimulateSensorData(Tree<SensorReading> tree)
    {
        AnsiConsole.Write(new Rule("[tan]Simulating Sensor Readings[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var random = new Random();
        var sensorIds = new[] { "sensor-01", "sensor-02", "sensor-03", "sensor-04" };

        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Recording sensor readings[/]", maxValue: 20);

                for (int i = 0; i < 20; i++)
                {
                    var sensorId = sensorIds[random.Next(sensorIds.Length)];
                    var reading = new SensorReading(
                        sensorId,
                        20 + random.NextDouble() * 15, // 20-35°C
                        30 + random.NextDouble() * 40, // 30-70% humidity
                        DateTime.UtcNow
                    );

                    var recordId = $"{sensorId}-{DateTime.UtcNow.Ticks}";

                    var sw = Stopwatch.StartNew();
                    tree.Stash(recordId, reading);
                    sw.Stop();

                    // Record metrics
                    MetricsCollector.Instance.RecordStash("SensorTree", sw.Elapsed.TotalMilliseconds);

                    task.Increment(1);
                    await Task.Delay(50);
                }
            });

        AnsiConsole.MarkupLine("[green]✓ Simulation complete - 20 sensor readings recorded[/]");
        AnsiConsole.MarkupLine("[dim]  Metrics have been updated with operation timings[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static async Task SimulateSystemEvents(Tree<SystemEvent> tree)
    {
        AnsiConsole.Write(new Rule("[tan]Simulating System Events[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var eventTypes = new[] { "UserLogin", "DataSync", "BackupComplete", "ErrorOccurred", "ConfigChange" };
        var severities = new[] { "Info", "Info", "Info", "Warning", "Error" };
        var random = new Random();

        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Recording system events[/]", maxValue: 15);

                for (int i = 0; i < 15; i++)
                {
                    var eventType = eventTypes[random.Next(eventTypes.Length)];
                    var severity = severities[random.Next(severities.Length)];

                    var systemEvent = new SystemEvent(
                        eventType,
                        $"Sample event message for {eventType}",
                        severity,
                        DateTime.UtcNow
                    );

                    var eventId = $"event-{Guid.NewGuid().ToString()[..8]}";

                    var sw = Stopwatch.StartNew();
                    tree.Stash(eventId, systemEvent);
                    sw.Stop();

                    // Record metrics
                    MetricsCollector.Instance.RecordStash("EventTree", sw.Elapsed.TotalMilliseconds);

                    task.Increment(1);
                    await Task.Delay(50);
                }
            });

        AnsiConsole.MarkupLine("[green]✓ Simulation complete - 15 system events recorded[/]");
        AnsiConsole.MarkupLine("[dim]  Metrics have been updated with operation timings[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static async Task ViewLiveDashboard(Tree<SensorReading> sensorTree, Tree<SystemEvent> eventTree)
    {
        AnsiConsole.Clear();

        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header"),
                new Layout("Body"));

        var startTime = DateTime.UtcNow;

        await AnsiConsole.Live(layout)
            .StartAsync(async ctx =>
            {
                while (!Console.KeyAvailable)
                {
                    var uptime = DateTime.UtcNow - startTime;

                    // Get metrics
                    var prometheusData = MetricsCollector.Instance.ExportPrometheus();
                    var lines = prometheusData.Split('\n');

                    long currentStashCount = 0;
                    long currentCrackCount = 0;
                    long currentTossCount = 0;

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("acorndb_stash_total{") && !line.Contains("HELP") && !line.Contains("TYPE"))
                        {
                            var parts = line.Split(' ');
                            if (parts.Length > 1 && long.TryParse(parts[1], out var count))
                                currentStashCount += count;
                        }
                        else if (line.StartsWith("acorndb_crack_total{"))
                        {
                            var parts = line.Split(' ');
                            if (parts.Length > 1 && long.TryParse(parts[1], out var count))
                                currentCrackCount += count;
                        }
                        else if (line.StartsWith("acorndb_toss_total{"))
                        {
                            var parts = line.Split(' ');
                            if (parts.Length > 1 && long.TryParse(parts[1], out var count))
                                currentTossCount += count;
                        }
                    }

                    // Header
                    layout["Header"].Update(
                        new Panel(
                            new Markup($"[tan bold]LIVE METRICS DASHBOARD[/] [dim]• Uptime: {uptime:hh\\:mm\\:ss} • Press any key to exit[/]"))
                        {
                            Border = BoxBorder.Rounded,
                            BorderStyle = Style.Parse("tan")
                        });

                    // Body - Create metrics tables
                    var opsTable = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Tan)
                        .AddColumn(new TableColumn("[tan bold]Operation[/]"))
                        .AddColumn(new TableColumn("[tan bold]Count[/]").RightAligned());

                    opsTable.AddRow("[dim]Stash (Write)[/]", $"[green]{currentStashCount}[/]");
                    opsTable.AddRow("[dim]Crack (Read)[/]", $"[olive]{currentCrackCount}[/]");
                    opsTable.AddRow("[dim]Toss (Delete)[/]", $"[red]{currentTossCount}[/]");
                    opsTable.AddRow("[tan bold]Total[/]", $"[yellow]{currentStashCount + currentCrackCount + currentTossCount}[/]");

                    var sensorCount = sensorTree.NutShells().Count();
                    var eventCount = eventTree.NutShells().Count();

                    var dataTable = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Tan)
                        .AddColumn(new TableColumn("[tan bold]Data Store[/]"))
                        .AddColumn(new TableColumn("[tan bold]Records[/]").RightAligned());

                    dataTable.AddRow("[dim]Sensor Readings[/]", $"[green]{sensorCount}[/]");
                    dataTable.AddRow("[dim]System Events[/]", $"[olive]{eventCount}[/]");

                    var totalOps = currentStashCount + currentCrackCount + currentTossCount;
                    var opsPerSecond = uptime.TotalSeconds > 0 ? totalOps / uptime.TotalSeconds : 0;

                    var throughputTable = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Tan)
                        .AddColumn(new TableColumn("[tan bold]Metric[/]"))
                        .AddColumn(new TableColumn("[tan bold]Value[/]").RightAligned());

                    throughputTable.AddRow("[dim]Operations/sec[/]", $"[yellow]{opsPerSecond:F2}[/]");

                    var bodyLayout = new Layout("BodyContent")
                        .SplitRows(
                            new Layout("Ops").Update(opsTable),
                            new Layout("Data").Update(dataTable),
                            new Layout("Throughput").Update(throughputTable));

                    layout["Body"].Update(bodyLayout);

                    ctx.Refresh();
                    await Task.Delay(1000);
                }
            });

        Console.ReadKey(true);
        AnsiConsole.Clear();
    }

    private static void ExportPrometheusFormat()
    {
        AnsiConsole.Write(new Rule("[tan]Metrics Export: Prometheus Format[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var prometheusText = MetricsCollector.Instance.ExportPrometheus();

        var panel = new Panel(
            new Markup(
                "[dim]# Prometheus Text Format[/]\n" +
                "[dim]# Suitable for /metrics HTTP endpoint[/]\n\n" +
                $"[white]{Markup.Escape(prometheusText)}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("olive"),
            Padding = new Padding(1, 0),
            Header = new PanelHeader("[olive]Prometheus Export[/]")
        };
        AnsiConsole.Write(panel);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]This format can be scraped by Prometheus for monitoring.[/]");
        AnsiConsole.MarkupLine("[dim]In production, expose via MetricsServer HTTP endpoint.[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ExportJsonFormat()
    {
        AnsiConsole.Write(new Rule("[tan]Metrics Export: JSON Format[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var jsonText = MetricsCollector.Instance.ExportJson();

        var panel = new Panel(
            new Markup(
                "[dim]# JSON/OpenTelemetry Compatible Format[/]\n" +
                "[dim]# Suitable for structured logging and telemetry[/]\n\n" +
                $"[white]{Markup.Escape(jsonText)}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("olive"),
            Padding = new Padding(1, 0),
            Header = new PanelHeader("[olive]JSON Export[/]")
        };
        AnsiConsole.Write(panel);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]This format can be sent to observability platforms like:[/]");
        AnsiConsole.MarkupLine("  [olive]•[/] [dim]Datadog[/]");
        AnsiConsole.MarkupLine("  [olive]•[/] [dim]New Relic[/]");
        AnsiConsole.MarkupLine("  [olive]•[/] [dim]Application Insights[/]");
        AnsiConsole.MarkupLine("  [olive]•[/] [dim]Elastic APM[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ViewTreeStats(Tree<SensorReading> sensorTree, Tree<SystemEvent> eventTree)
    {
        AnsiConsole.Write(new Rule("[tan]Tree Statistics[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        // Sensor Tree Stats
        var sensorReadings = sensorTree.Nuts.ToList();

        var sensorPanel = new Panel(
            new Markup(
                sensorReadings.Any()
                    ? $"[tan bold]Total Records:[/] [green]{sensorReadings.Count}[/]\n\n" +
                      $"[dim]Average Temperature:[/] [white]{sensorReadings.Average(s => s.Temperature):F2}°C[/]\n" +
                      $"[dim]Average Humidity:[/] [white]{sensorReadings.Average(s => s.Humidity):F2}%[/]\n" +
                      $"[dim]Time Range:[/] [dim]{sensorReadings.Min(s => s.Timestamp):g} to {sensorReadings.Max(s => s.Timestamp):g}[/]\n\n" +
                      $"[tan]Unique Sensors:[/] {sensorReadings.GroupBy(s => s.SensorId).Count()}\n" +
                      string.Join("\n", sensorReadings.GroupBy(s => s.SensorId).Select(g => $"  [olive]•[/] [yellow]{g.Key}[/]: {g.Count()} readings"))
                    : "[dim]No sensor data yet[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("tan"),
            Padding = new Padding(1, 0),
            Header = new PanelHeader("[tan]Sensor Readings Tree[/]")
        };
        AnsiConsole.Write(sensorPanel);
        AnsiConsole.WriteLine();

        // Events Tree Stats
        var events = eventTree.Nuts.ToList();

        var eventsPanel = new Panel(
            new Markup(
                events.Any()
                    ? $"[tan bold]Total Records:[/] [green]{events.Count}[/]\n\n" +
                      $"[dim]Time Range:[/] [dim]{events.Min(e => e.Timestamp):g} to {events.Max(e => e.Timestamp):g}[/]\n\n" +
                      $"[tan]By Severity:[/]\n" +
                      string.Join("\n", events.GroupBy(e => e.Severity).OrderBy(g => g.Key).Select(g => $"  [olive]•[/] [yellow]{g.Key}[/]: {g.Count()} events")) +
                      $"\n\n[tan]Top Event Types:[/]\n" +
                      string.Join("\n", events.GroupBy(e => e.EventType).OrderByDescending(g => g.Count()).Take(5).Select(g => $"  [olive]•[/] [yellow]{g.Key}[/]: {g.Count()} events"))
                    : "[dim]No event data yet[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("tan"),
            Padding = new Padding(1, 0),
            Header = new PanelHeader("[tan]System Events Tree[/]")
        };
        AnsiConsole.Write(eventsPanel);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static async Task RunLoadTest(Tree<SensorReading> sensorTree, Tree<SystemEvent> eventTree)
    {
        AnsiConsole.Write(new Rule("[tan]Load Test[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var count = AnsiConsole.Ask("[green]Number of operations to perform:[/]", 1000);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[olive]Running load test with {count} operations...[/]");
        AnsiConsole.WriteLine();

        // Get starting metrics
        var startPrometheus = MetricsCollector.Instance.ExportPrometheus();
        var startStashCount = ExtractMetricValue(startPrometheus, "acorndb_stash_total");
        var startCrackCount = ExtractMetricValue(startPrometheus, "acorndb_crack_total");

        var startTime = DateTime.UtcNow;
        var random = new Random();

        long stashOps = 0, crackOps = 0;

        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Performing operations[/]", maxValue: count);

                for (int i = 0; i < count; i++)
                {
                    var operation = random.Next(4);

                    switch (operation)
                    {
                        case 0: // Stash sensor reading
                            var reading = new SensorReading(
                                $"sensor-{random.Next(1, 11):D2}",
                                20 + random.NextDouble() * 15,
                                30 + random.NextDouble() * 40,
                                DateTime.UtcNow
                            );
                            var sw1 = Stopwatch.StartNew();
                            sensorTree.Stash($"load-sensor-{i}", reading);
                            sw1.Stop();
                            MetricsCollector.Instance.RecordStash("SensorTree", sw1.Elapsed.TotalMilliseconds);
                            stashOps++;
                            break;

                        case 1: // Stash event
                            var systemEvent = new SystemEvent(
                                "LoadTest",
                                $"Load test event {i}",
                                "Info",
                                DateTime.UtcNow
                            );
                            var sw2 = Stopwatch.StartNew();
                            eventTree.Stash($"load-event-{i}", systemEvent);
                            sw2.Stop();
                            MetricsCollector.Instance.RecordStash("EventTree", sw2.Elapsed.TotalMilliseconds);
                            stashOps++;
                            break;

                        case 2: // Crack (read)
                            if (i > 0)
                            {
                                var sw3 = Stopwatch.StartNew();
                                var result = sensorTree.Crack($"load-sensor-{random.Next(i)}");
                                sw3.Stop();
                                MetricsCollector.Instance.RecordCrack("SensorTree", sw3.Elapsed.TotalMilliseconds, result != null);
                                crackOps++;
                            }
                            break;

                        case 3: // GetAll (expensive)
                            if (i % 100 == 0) // Only do this occasionally
                            {
                                sensorTree.Nuts.Take(10).ToList();
                            }
                            break;
                    }

                    task.Increment(1);
                }
            });

        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;

        // Get ending metrics
        var endPrometheus = MetricsCollector.Instance.ExportPrometheus();
        var endStashCount = ExtractMetricValue(endPrometheus, "acorndb_stash_total");
        var endCrackCount = ExtractMetricValue(endPrometheus, "acorndb_crack_total");

        var resultsTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Tan)
            .AddColumn(new TableColumn("[tan bold]Metric[/]"))
            .AddColumn(new TableColumn("[tan bold]Value[/]").RightAligned());

        resultsTable.AddRow("[dim]Total Operations[/]", $"[green]{count}[/]");
        resultsTable.AddRow("[dim]Duration[/]", $"[yellow]{duration.TotalSeconds:F2}s[/]");
        resultsTable.AddRow("[dim]Throughput[/]", $"[olive]{count / duration.TotalSeconds:F2} ops/sec[/]");
        resultsTable.AddEmptyRow();
        resultsTable.AddRow("[tan bold]Operations Breakdown[/]", "");
        resultsTable.AddRow("  [dim]Stash (Write)[/]", $"[green]{endStashCount - startStashCount}[/]");
        resultsTable.AddRow("  [dim]Crack (Read)[/]", $"[olive]{endCrackCount - startCrackCount}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(resultsTable);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]✓ Load test complete - check metrics for performance data[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static long ExtractMetricValue(string prometheusText, string metricName)
    {
        var lines = prometheusText.Split('\n');
        long total = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith(metricName + "{"))
            {
                var parts = line.Split(' ');
                if (parts.Length > 1 && long.TryParse(parts[1], out var value))
                    total += value;
            }
        }
        return total;
    }
}
