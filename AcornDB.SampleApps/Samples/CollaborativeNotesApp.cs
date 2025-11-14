using AcornDB;
using AcornDB.Storage;
using AcornDB.Sync;
using Spectre.Console;

namespace AcornDB.SampleApps.Samples;

/// <summary>
/// Sample 4: Collaborative Notes
///
/// Demonstrates:
/// - Branch sync for multi-user collaboration
/// - Conflict resolution with judges
/// - Delta sync for efficient synchronization
/// - Real-time-ish collaboration (simulated)
/// </summary>
public static class CollaborativeNotesApp
{
    private record Note(
        string Title,
        string Content,
        string Author,
        DateTime ModifiedAt,
        int Version = 1
    );

    public static async Task Run()
    {
        AnsiConsole.Clear();

        // AcornDB themed header
        var rule = new Rule("[tan bold]Collaborative Notes[/]")
        {
            Justification = Justify.Left,
            Style = Style.Parse("tan")
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        var infoPanel = new Panel(
            new Markup(
                "[dim]This demo simulates collaborative note-taking with sync.[/]\n" +
                "[dim]In production, multiple users connect to an AcornSyncServer.[/]\n" +
                "[dim]Here we simulate it locally for demonstration purposes.[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("olive"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(infoPanel);
        AnsiConsole.WriteLine();

        // Setup: Local tree with simulated "remote" sync
        var localTree = new Tree<Note>(new DocumentStoreTrunk<Note>("data/sample-notes-local"));

        // Simulate another user's tree
        var remoteTree = new Tree<Note>(new DocumentStoreTrunk<Note>("data/sample-notes-remote"));

        // Configure sync with batching and delta sync
        var branch = new Branch("http://localhost:5000") // Would be real server in production
            .WithBatching(batchSize: 5, batchTimeoutMs: 100)
            .WithSyncMode(SyncMode.Bidirectional)
            .WithDeltaSync(true);

        AnsiConsole.MarkupLine("[dim]✓ Branch configured (local simulation mode)[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[tan bold]What would you like to do?[/]")
                    .PageSize(10)
                    .AddChoices(new[] {
                        "Create/Edit Note",
                        "List Notes",
                        "View Note",
                        "Simulate Collaboration (Create Conflict)",
                        "Resolve Conflicts",
                        "View Sync Stats",
                        "Back to Main Menu"
                    }));

            AnsiConsole.WriteLine();

            switch (choice)
            {
                case "Create/Edit Note":
                    EditNote(localTree);
                    break;
                case "List Notes":
                    ListNotes(localTree);
                    break;
                case "View Note":
                    ViewNote(localTree);
                    break;
                case "Simulate Collaboration (Create Conflict)":
                    SimulateCollaboration(localTree, remoteTree);
                    break;
                case "Resolve Conflicts":
                    ResolveConflicts(localTree);
                    break;
                case "View Sync Stats":
                    ViewSyncStats(branch);
                    break;
                case "Back to Main Menu":
                    branch.Dispose();
                    return;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static void EditNote(Tree<Note> tree)
    {
        AnsiConsole.Write(new Rule("[tan]Create/Edit Note[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var noteId = AnsiConsole.Ask<string>("[green]Note ID[/] [dim](leave empty for new):[/]");

        Note? existing = null;
        if (!string.IsNullOrEmpty(noteId))
        {
            existing = tree.Crack(noteId);
            if (existing == null)
            {
                AnsiConsole.MarkupLine("[red]✗ Note not found.[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
                Console.ReadKey(true);
                return;
            }
            AnsiConsole.MarkupLine($"[dim]Editing:[/] [white]{Markup.Escape(existing.Title)}[/] [olive](v{existing.Version})[/]");
            AnsiConsole.WriteLine();
        }
        else
        {
            noteId = $"note-{Guid.NewGuid().ToString()[..8]}";
        }

        var title = AnsiConsole.Ask<string>(
            $"[green]Title[/] [dim][{Markup.Escape(existing?.Title ?? "")}]:[/]",
            existing?.Title ?? "");

        var author = AnsiConsole.Ask<string>(
            $"[green]Author[/] [dim][{Markup.Escape(existing?.Author ?? Environment.UserName)}]:[/]",
            existing?.Author ?? Environment.UserName);

        AnsiConsole.MarkupLine("[green]Content[/] [dim](type 'END' on a new line to finish):[/]");
        var contentLines = new List<string>();
        while (true)
        {
            var line = Console.ReadLine();
            if (line == "END") break;
            contentLines.Add(line ?? "");
        }
        var content = contentLines.Any() ? string.Join("\n", contentLines) : (existing?.Content ?? "");

        var version = (existing?.Version ?? 0) + 1;
        var note = new Note(title, content, author, DateTime.UtcNow, version);

        tree.Stash(noteId, note);

        AnsiConsole.MarkupLine($"[green]✓ Note saved:[/] [yellow]{noteId}[/] [olive](v{version})[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ListNotes(Tree<Note> tree)
    {
        AnsiConsole.Write(new Rule("[tan]All Notes[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var notes = tree.NutShells().OrderByDescending(n => n.Payload.ModifiedAt).ToList();

        if (!notes.Any())
        {
            AnsiConsole.MarkupLine("[dim]No notes yet. Create one to get started![/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Tan)
            .AddColumn(new TableColumn("[tan bold]ID[/]"))
            .AddColumn(new TableColumn("[tan bold]Title[/]"))
            .AddColumn(new TableColumn("[tan bold]Author[/]"))
            .AddColumn(new TableColumn("[tan bold]Version[/]").Centered())
            .AddColumn(new TableColumn("[tan bold]Modified[/]"))
            .AddColumn(new TableColumn("[tan bold]Preview[/]"));

        foreach (var nut in notes)
        {
            var note = nut.Payload;
            var preview = note.Content.Length > 40
                ? note.Content.Substring(0, 40) + "..."
                : note.Content;

            table.AddRow(
                $"[yellow]{nut.Id}[/]",
                $"[white]{Markup.Escape(note.Title)}[/]",
                $"[dim]{Markup.Escape(note.Author)}[/]",
                $"[olive]v{note.Version}[/]",
                $"[dim]{note.ModifiedAt:g}[/]",
                $"[dim]{Markup.Escape(preview)}[/]");
        }

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[tan]Total notes:[/] {notes.Count}");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ViewNote(Tree<Note> tree)
    {
        AnsiConsole.Write(new Rule("[tan]View Note[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var noteId = AnsiConsole.Ask<string>("[green]Note ID:[/]");

        var note = tree.Crack(noteId);
        if (note == null)
        {
            AnsiConsole.MarkupLine("[red]✗ Note not found.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var notePanel = new Panel(
            new Markup(
                $"[tan bold]{Markup.Escape(note.Title)}[/] [olive](v{note.Version})[/]\n\n" +
                $"[dim]Author:[/] [green]{Markup.Escape(note.Author)}[/]\n" +
                $"[dim]Last Modified:[/] [dim]{note.ModifiedAt:g}[/]\n\n" +
                $"[white]{Markup.Escape(note.Content)}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("tan"),
            Padding = new Padding(2, 1)
        };
        AnsiConsole.Write(notePanel);
        AnsiConsole.WriteLine();

        // Show history
        var history = tree.GetHistory(noteId);
        if (history.Count > 1)
        {
            AnsiConsole.Write(new Rule($"[olive]Version History ({history.Count} versions)[/]") { Style = Style.Parse("dim") });
            AnsiConsole.WriteLine();

            var historyTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Olive)
                .AddColumn(new TableColumn("[olive bold]Version[/]").Centered())
                .AddColumn(new TableColumn("[olive bold]Timestamp[/]"))
                .AddColumn(new TableColumn("[olive bold]Author[/]"));

            foreach (var version in history.Reverse().Take(5))
            {
                historyTable.AddRow(
                    $"[yellow]v{version.Value.Version}[/]",
                    $"[dim]{version.Timestamp:g}[/]",
                    $"[green]{Markup.Escape(version.Value.Author)}[/]");
            }

            AnsiConsole.Write(historyTable);
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void SimulateCollaboration(Tree<Note> localTree, Tree<Note> remoteTree)
    {
        AnsiConsole.Write(new Rule("[tan]Simulate Collaboration[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[olive]Simulating concurrent edits from two users...[/]");
        AnsiConsole.WriteLine();

        // Create a note on both sides with different content
        var noteId = $"note-{Guid.NewGuid().ToString()[..8]}";

        // User 1 creates note
        var note1 = new Note(
            "Shared Document",
            "User 1's version of the content.\nThis was created first.",
            "User1",
            DateTime.UtcNow,
            1
        );
        localTree.Stash(noteId, note1);
        AnsiConsole.MarkupLine($"[green]✓[/] [dim]User 1 created note:[/] [yellow]{noteId}[/]");

        // Simulate delay
        System.Threading.Thread.Sleep(100);

        // User 2 creates conflicting note (same ID, different content)
        var note2 = new Note(
            "Shared Document",
            "User 2's version of the content.\nThis was created shortly after.",
            "User2",
            DateTime.UtcNow,
            1
        );
        remoteTree.Stash(noteId, note2);
        AnsiConsole.MarkupLine($"[green]✓[/] [dim]User 2 created conflicting note:[/] [yellow]{noteId}[/]");
        AnsiConsole.WriteLine();

        var conflictPanel = new Panel(
            new Markup(
                "[yellow]Conflict created![/] Both users edited the same note.\n\n" +
                "[dim]In a real sync scenario, AcornDB's conflict resolution handles this:[/]\n" +
                "  [olive]•[/] [white]LastWriteWins[/] [dim]- Most recent edit wins[/]\n" +
                "  [olive]•[/] [white]Custom Judge[/] [dim]- Business logic determines winner[/]\n" +
                "  [olive]•[/] [white]Manual[/] [dim]- User resolves conflict[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("yellow"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(conflictPanel);
        AnsiConsole.WriteLine();

        // Simulate conflict resolution with LastWriteWins
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("olive"))
            .Start("[dim]Resolving with LastWriteWins judge...[/]", ctx =>
            {
                System.Threading.Thread.Sleep(1000);

                var localVersion = localTree.Crack(noteId);
                var remoteVersion = remoteTree.Crack(noteId);

                if (localVersion != null && remoteVersion != null)
                {
                    if (remoteVersion.ModifiedAt > localVersion.ModifiedAt)
                    {
                        // Remote wins - create a Nut from the remote version
                        var remoteNut = new Nut<Note>
                        {
                            Id = noteId,
                            Payload = remoteVersion,
                            Timestamp = remoteVersion.ModifiedAt
                        };
                        localTree.Squabble(noteId, remoteNut, ConflictDirection.PreferRemote);
                        ctx.Status("[green]✓ Remote version won (newer timestamp)[/]");
                    }
                    else
                    {
                        // Local wins
                        ctx.Status("[green]✓ Local version won (newer timestamp)[/]");
                    }
                }
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✓[/] Note [yellow]{noteId}[/] now has consistent content.");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ResolveConflicts(Tree<Note> tree)
    {
        AnsiConsole.Write(new Rule("[tan]Resolve Conflicts[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var infoPanel = new Panel(
            new Markup(
                "[dim]In a real sync setup with[/] [white]Branch.ShakeAsync()[/][dim]:[/]\n\n" +
                "  [olive]1.[/] [dim]Pull changes from remote[/]\n" +
                "  [olive]2.[/] [dim]Detect conflicts automatically[/]\n" +
                "  [olive]3.[/] [dim]Apply conflict resolution judge[/]\n" +
                "  [olive]4.[/] [dim]Track resolution statistics[/]\n\n" +
                "[green]Current tree has no pending conflicts.[/]\n" +
                "[dim]Use 'Simulate Collaboration' to create test conflicts.[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("olive"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(infoPanel);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ViewSyncStats(Branch branch)
    {
        AnsiConsole.Write(new Rule("[tan]Sync Statistics[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var stats = branch.GetStats();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Tan)
            .AddColumn(new TableColumn("[tan bold]Setting[/]"))
            .AddColumn(new TableColumn("[tan bold]Value[/]"));

        table.AddRow("[dim]Remote URL[/]", $"[yellow]{stats.RemoteUrl}[/]");
        table.AddRow("[dim]Sync Mode[/]", $"[olive]{stats.SyncMode}[/]");
        table.AddRow("[dim]Conflict Direction[/]", $"[olive]{stats.ConflictDirection}[/]");
        table.AddEmptyRow();
        table.AddRow("[tan bold]Operations[/]", "");
        table.AddRow("  [dim]Total Pushed[/]", $"[green]{stats.TotalPushed}[/]");
        table.AddRow("  [dim]Total Deleted[/]", $"[red]{stats.TotalDeleted}[/]");
        table.AddRow("  [dim]Total Pulled[/]", $"[olive]{stats.TotalPulled}[/]");
        table.AddRow("  [dim]Total Conflicts[/]", $"[yellow]{stats.TotalConflicts}[/]");
        table.AddEmptyRow();
        table.AddRow("[dim]Delta Sync[/]", stats.DeltaSyncEnabled ? "[green]✓ Enabled[/]" : "[dim]Disabled[/]");
        table.AddRow("[dim]Last Sync[/]",
            stats.HasSynced ? $"[dim]{stats.LastSyncTimestamp:g}[/]" : "[dim]Never[/]");

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        var notePanel = new Panel(
            "[dim]Stats shown are for local branch configuration.[/]\n" +
            "[olive]Connect to AcornSyncServer for real collaboration![/]")
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("olive"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(notePanel);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }
}
