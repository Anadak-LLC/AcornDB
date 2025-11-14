using AcornDB;
using AcornDB.Storage;
using Spectre.Console;

namespace AcornDB.SampleApps.Samples;

/// <summary>
/// Sample 1: Todo List Manager
///
/// Demonstrates:
/// - Basic CRUD operations (Create, Read, Update, Delete)
/// - Document storage with history tracking
/// - Beautiful Spectre.Console UI with AcornDB theme
/// </summary>
public static class TodoListApp
{
    private record TodoItem(
        string Title,
        string Description,
        bool IsCompleted,
        DateTime CreatedAt,
        DateTime? CompletedAt = null,
        int Priority = 0
    );

    public static async Task Run()
    {
        AnsiConsole.Clear();

        // AcornDB themed header
        var rule = new Rule("[tan bold]Todo List Manager[/]")
        {
            Justification = Justify.Left,
            Style = Style.Parse("tan")
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        // Setup: DocumentStore for full history tracking
        var trunk = new DocumentStoreTrunk<TodoItem>("data/sample-todos");
        var tree = new Tree<TodoItem>(trunk);

        AnsiConsole.MarkupLine("[dim]✓ Initialized with DocumentStore (full history tracking)[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[tan bold]What would you like to do?[/]")
                    .PageSize(10)
                    .AddChoices(new[] {
                        "Add Todo",
                        "List Todos",
                        "Complete Todo",
                        "Delete Todo",
                        "View History",
                        "Back to Main Menu"
                    }));

            AnsiConsole.WriteLine();

            switch (choice)
            {
                case "Add Todo":
                    AddTodo(tree);
                    break;
                case "List Todos":
                    ListTodos(tree);
                    break;
                case "Complete Todo":
                    CompleteTodo(tree);
                    break;
                case "Delete Todo":
                    DeleteTodo(tree);
                    break;
                case "View History":
                    ViewHistory(tree);
                    break;
                case "Back to Main Menu":
                    trunk.Dispose();
                    return;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static void AddTodo(Tree<TodoItem> tree)
    {
        AnsiConsole.Write(new Rule("[tan]Add New Todo[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var title = AnsiConsole.Ask<string>("[green]Title:[/]");
        var description = AnsiConsole.Ask<string>("[green]Description:[/]");
        var priority = AnsiConsole.Prompt(
            new SelectionPrompt<int>()
                .Title("[green]Priority:[/]")
                .AddChoices(new[] { 0, 1, 2, 3, 4, 5 })
                .UseConverter(p => $"{new string('*', p)} ({p})"));

        var id = $"todo-{Guid.NewGuid().ToString()[..8]}";
        var todo = new TodoItem(title, description, false, DateTime.UtcNow, null, priority);

        tree.Stash(id, todo);

        AnsiConsole.MarkupLine($"[green]✓[/] Todo created with ID: [yellow]{id}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ListTodos(Tree<TodoItem> tree)
    {
        AnsiConsole.Write(new Rule("[tan]Your Todo List[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var todos = tree.NutShells()
            .OrderBy(t => t.Payload.IsCompleted)
            .ThenByDescending(t => t.Payload.Priority)
            .ToList();

        if (!todos.Any())
        {
            AnsiConsole.MarkupLine("[dim]No todos yet. Add one to get started![/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Tan)
            .AddColumn(new TableColumn("[tan bold]Status[/]").Centered())
            .AddColumn(new TableColumn("[tan bold]ID[/]"))
            .AddColumn(new TableColumn("[tan bold]Title[/]"))
            .AddColumn(new TableColumn("[tan bold]Priority[/]").Centered())
            .AddColumn(new TableColumn("[tan bold]Description[/]"));

        foreach (var nut in todos)
        {
            var todo = nut.Payload;
            var status = todo.IsCompleted ? "[green]✓[/]" : "[grey]○[/]";
            var priorityStars = new string('*', todo.Priority);
            var titleColor = todo.IsCompleted ? "dim" : "white";
            var descPreview = todo.Description.Length > 50
                ? todo.Description.Substring(0, 50) + "..."
                : todo.Description;

            table.AddRow(
                status,
                $"[yellow]{nut.Id}[/]",
                $"[{titleColor}]{Markup.Escape(todo.Title)}[/]",
                priorityStars,
                $"[dim]{Markup.Escape(descPreview)}[/]");
        }

        AnsiConsole.Write(table);

        var activeCount = todos.Count(t => !t.Payload.IsCompleted);
        var completedCount = todos.Count(t => t.Payload.IsCompleted);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[tan]Total:[/] {todos.Count} todos " +
            $"([green]{activeCount} active[/], [dim]{completedCount} completed[/])");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void CompleteTodo(Tree<TodoItem> tree)
    {
        AnsiConsole.Write(new Rule("[tan]Complete Todo[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var id = AnsiConsole.Ask<string>("[green]Todo ID to complete:[/]");

        var todo = tree.Crack(id);
        if (todo == null)
        {
            AnsiConsole.MarkupLine("[red]✗ Todo not found.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        if (todo.IsCompleted)
        {
            AnsiConsole.MarkupLine("[yellow]⚠[/] Todo is already completed.");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var updated = todo with { IsCompleted = true, CompletedAt = DateTime.UtcNow };
        tree.Stash(id, updated);

        var panel = new Panel(
            new Markup($"[green]✓ Completed:[/] [white]{Markup.Escape(todo.Title)}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("green"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(panel);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void DeleteTodo(Tree<TodoItem> tree)
    {
        AnsiConsole.Write(new Rule("[tan]Delete Todo[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var id = AnsiConsole.Ask<string>("[green]Todo ID to delete:[/]");

        var todo = tree.Crack(id);
        if (todo == null)
        {
            AnsiConsole.MarkupLine("[red]✗ Todo not found.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var confirm = AnsiConsole.Confirm($"Are you sure you want to delete '[yellow]{Markup.Escape(todo.Title)}[/]'?");

        if (confirm)
        {
            tree.Toss(id);
            AnsiConsole.MarkupLine($"[red]✓ Deleted:[/] {Markup.Escape(todo.Title)}");
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ViewHistory(Tree<TodoItem> tree)
    {
        AnsiConsole.Write(new Rule("[tan]View Todo History[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var id = AnsiConsole.Ask<string>("[green]Todo ID to view history:[/]");

        var history = tree.GetHistory(id);
        if (!history.Any())
        {
            AnsiConsole.MarkupLine("[red]✗ No history found (todo doesn't exist or has no history).[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        AnsiConsole.MarkupLine($"[tan]History for:[/] [yellow]{id}[/]");
        AnsiConsole.WriteLine();

        var tree_visual = new Spectre.Console.Tree($"[tan bold]Version History ({history.Count} versions)[/]");

        for (int i = history.Count - 1; i >= 0; i--)
        {
            var nut = history[i];
            var version = history.Count - i;
            var versionNode = tree_visual.AddNode(
                $"[yellow]Version {version}[/] [dim]({nut.Timestamp:g})[/]");

            versionNode.AddNode($"[green]Title:[/] {Markup.Escape(nut.Value.Title)}");
            versionNode.AddNode($"[green]Description:[/] {Markup.Escape(nut.Value.Description)}");
            versionNode.AddNode($"[green]Status:[/] {(nut.Value.IsCompleted ? "[green]Completed[/]" : "[yellow]Active[/]")}");
            versionNode.AddNode($"[green]Priority:[/] {new string('*', nut.Value.Priority)} ({nut.Value.Priority})");

            if (nut.Value.CompletedAt.HasValue)
            {
                versionNode.AddNode($"[green]Completed At:[/] {nut.Value.CompletedAt.Value:g}");
            }
        }

        AnsiConsole.Write(tree_visual);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }
}
