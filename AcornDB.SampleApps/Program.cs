using AcornDB.SampleApps.Samples;
using Spectre.Console;

namespace AcornDB.SampleApps;

class Program
{
    static async Task Main(string[] args)
    {
        AnsiConsole.Clear();

        // AcornDB themed header with ASCII art
        var figlet = new FigletText("AcornDB")
            .LeftJustified()
            .Color(Color.Tan);
        AnsiConsole.Write(figlet);

        var panel = new Panel(
            new Markup("[dim]Real-world examples demonstrating AcornDB with production features[/]\n" +
                      "[olive]Storage • Caching • Sync • Resilience • Metrics[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("tan"),
            Padding = new Padding(2, 0)
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[tan bold]Choose a Sample Application:[/]")
                    .PageSize(15)
                    .MoreChoicesText("[dim](Move up and down to reveal more samples)[/]")
                    .AddChoiceGroup("[olive]Basic Applications[/]", new[] {
                        "1. Todo List Manager",
                        "2. Blog Platform",
                        "3. E-Commerce System"
                    })
                    .AddChoiceGroup("[olive]Advanced Applications[/]", new[] {
                        "4. Collaborative Notes (with sync)",
                        "5. Resilient Cache Demo",
                        "6. Metrics Monitoring Dashboard"
                    })
                    .AddChoices(new[] { "Exit" }));

            AnsiConsole.WriteLine();

            try
            {
                switch (choice)
                {
                    case "1. Todo List Manager":
                        await TodoListApp.Run();
                        break;
                    case "2. Blog Platform":
                        await BlogApp.Run();
                        break;
                    case "3. E-Commerce System":
                        await ECommerceApp.Run();
                        break;
                    case "4. Collaborative Notes (with sync)":
                        await CollaborativeNotesApp.Run();
                        break;
                    case "5. Resilient Cache Demo":
                        await ResilientCacheApp.Run();
                        break;
                    case "6. Metrics Monitoring Dashboard":
                        await MetricsMonitoringApp.Run();
                        break;
                    case "Exit":
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine("[tan]Thank you for exploring AcornDB Sample Applications![/]");
                        AnsiConsole.MarkupLine("[dim]Visit github.com/anthropics/acorndb for more information[/]");
                        return;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Press any key to return to menu...[/]");
                Console.ReadKey();
            }

            AnsiConsole.Clear();
            AnsiConsole.Write(figlet);
            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
        }
    }
}
