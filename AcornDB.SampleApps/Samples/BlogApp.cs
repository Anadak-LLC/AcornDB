using AcornDB;
using AcornDB.Storage;
using AcornDB.Models;
using Spectre.Console;

namespace AcornDB.SampleApps.Samples;

/// <summary>
/// Sample 2: Blog Platform
///
/// Demonstrates:
/// - Multiple related data types (Posts and Comments)
/// - Grove for managing multiple trees
/// - Relationships between entities
/// - Search and filtering
/// </summary>
public static class BlogApp
{
    private record BlogPost(
        string Title,
        string Content,
        string Author,
        DateTime CreatedAt,
        List<string> Tags,
        int ViewCount = 0
    );

    private record Comment(
        string PostId,
        string Author,
        string Content,
        DateTime CreatedAt
    );

    public static async Task Run()
    {
        AnsiConsole.Clear();

        // AcornDB themed header
        var rule = new Rule("[tan bold]Blog Platform[/]")
        {
            Justification = Justify.Left,
            Style = Style.Parse("tan")
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        // Setup: Use Grove to manage multiple trees
        var grove = new Grove();
        grove.Plant(new Tree<BlogPost>(new DocumentStoreTrunk<BlogPost>("data/sample-blog-posts")));
        grove.Plant(new Tree<Comment>(new DocumentStoreTrunk<Comment>("data/sample-blog-comments")));

        var postTree = grove.GetTree<BlogPost>()!;
        var commentTree = grove.GetTree<Comment>()!;

        AnsiConsole.MarkupLine("[dim]✓ Initialized with Grove (posts + comments)[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[tan bold]What would you like to do?[/]")
                    .PageSize(10)
                    .AddChoices(new[] {
                        "Create Post",
                        "List All Posts",
                        "View Post (with comments)",
                        "Add Comment",
                        "Search by Tag",
                        "Back to Main Menu"
                    }));

            AnsiConsole.WriteLine();

            switch (choice)
            {
                case "Create Post":
                    CreatePost(postTree);
                    break;
                case "List All Posts":
                    ListPosts(postTree);
                    break;
                case "View Post (with comments)":
                    ViewPost(postTree, commentTree);
                    break;
                case "Add Comment":
                    AddComment(postTree, commentTree);
                    break;
                case "Search by Tag":
                    SearchByTag(postTree);
                    break;
                case "Back to Main Menu":
                    return;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static void CreatePost(Tree<BlogPost> postTree)
    {
        AnsiConsole.Write(new Rule("[tan]Create New Post[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var title = AnsiConsole.Ask<string>("[green]Title:[/]");
        var author = AnsiConsole.Ask<string>("[green]Author:[/]");

        AnsiConsole.MarkupLine("[green]Content[/] [dim](type 'END' on a new line to finish):[/]");
        var contentLines = new List<string>();
        while (true)
        {
            var line = Console.ReadLine();
            if (line == "END") break;
            contentLines.Add(line ?? "");
        }
        var content = string.Join("\n", contentLines);

        var tagsInput = AnsiConsole.Ask<string>("[green]Tags[/] [dim](comma-separated):[/]");
        var tags = tagsInput.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .ToList();

        var postId = $"post-{Guid.NewGuid().ToString()[..8]}";
        var post = new BlogPost(title, content, author, DateTime.UtcNow, tags);

        postTree.Stash(postId, post);

        var panel = new Panel(
            new Markup($"[green]✓ Post created:[/] [white]{Markup.Escape(title)}[/]\n[yellow]ID: {postId}[/]"))
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

    private static void ListPosts(Tree<BlogPost> postTree)
    {
        AnsiConsole.Write(new Rule("[tan]All Blog Posts[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var posts = postTree.NutShells().OrderByDescending(p => p.Payload.CreatedAt).ToList();

        if (!posts.Any())
        {
            AnsiConsole.MarkupLine("[dim]No posts yet. Create one to get started![/]");
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
            .AddColumn(new TableColumn("[tan bold]Date[/]"))
            .AddColumn(new TableColumn("[tan bold]Views[/]").Centered())
            .AddColumn(new TableColumn("[tan bold]Tags[/]"))
            .AddColumn(new TableColumn("[tan bold]Preview[/]"));

        foreach (var nut in posts)
        {
            var post = nut.Payload;
            var preview = post.Content.Length > 50
                ? post.Content.Substring(0, 50) + "..."
                : post.Content;

            table.AddRow(
                $"[yellow]{nut.Id}[/]",
                $"[white]{Markup.Escape(post.Title)}[/]",
                $"[dim]{Markup.Escape(post.Author)}[/]",
                $"[dim]{post.CreatedAt:g}[/]",
                $"[olive]{post.ViewCount}[/]",
                $"[dim]{Markup.Escape(string.Join(", ", post.Tags))}[/]",
                $"[dim]{Markup.Escape(preview)}[/]");
        }

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[tan]Total:[/] {posts.Count} posts");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ViewPost(Tree<BlogPost> postTree, Tree<Comment> commentTree)
    {
        AnsiConsole.Write(new Rule("[tan]View Post[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var postId = AnsiConsole.Ask<string>("[green]Post ID:[/]");

        var post = postTree.Crack(postId);
        if (post == null)
        {
            AnsiConsole.MarkupLine("[red]✗ Post not found.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        // Increment view count
        var updatedPost = post with { ViewCount = post.ViewCount + 1 };
        postTree.Stash(postId, updatedPost);

        // Display post in a panel
        var postContent = new Panel(
            new Markup(
                $"[tan bold]{Markup.Escape(updatedPost.Title)}[/]\n\n" +
                $"[dim]By:[/] [white]{Markup.Escape(updatedPost.Author)}[/] [dim]|[/] " +
                $"[dim]{updatedPost.CreatedAt:g}[/] [dim]|[/] " +
                $"[olive]{updatedPost.ViewCount} views[/]\n" +
                $"[dim]Tags:[/] [yellow]{Markup.Escape(string.Join(", ", updatedPost.Tags))}[/]\n\n" +
                $"[white]{Markup.Escape(updatedPost.Content)}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("tan"),
            Padding = new Padding(2, 1)
        };
        AnsiConsole.Write(postContent);
        AnsiConsole.WriteLine();

        // Show comments
        var comments = commentTree.NutShells()
            .Where(c => c.Payload.PostId == postId)
            .OrderBy(c => c.Payload.CreatedAt)
            .Select(n => n.Payload)
            .ToList();

        AnsiConsole.Write(new Rule($"[olive]Comments ({comments.Count})[/]") { Style = Style.Parse("dim") });
        AnsiConsole.WriteLine();

        if (comments.Any())
        {
            foreach (var comment in comments)
            {
                var commentPanel = new Panel(
                    new Markup(
                        $"[green]{Markup.Escape(comment.Author)}[/] [dim]• {comment.CreatedAt:g}[/]\n" +
                        $"[white]{Markup.Escape(comment.Content)}[/]"))
                {
                    Border = BoxBorder.Rounded,
                    BorderStyle = Style.Parse("dim"),
                    Padding = new Padding(1, 0)
                };
                AnsiConsole.Write(commentPanel);
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]No comments yet. Be the first to comment![/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void AddComment(Tree<BlogPost> postTree, Tree<Comment> commentTree)
    {
        AnsiConsole.Write(new Rule("[tan]Add Comment[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var postId = AnsiConsole.Ask<string>("[green]Post ID:[/]");

        var post = postTree.Crack(postId);
        if (post == null)
        {
            AnsiConsole.MarkupLine("[red]✗ Post not found.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        AnsiConsole.MarkupLine($"[dim]Commenting on:[/] [white]{Markup.Escape(post.Title)}[/]");
        AnsiConsole.WriteLine();

        var author = AnsiConsole.Ask<string>("[green]Your name:[/]");
        var content = AnsiConsole.Ask<string>("[green]Comment:[/]");

        var commentId = $"comment-{Guid.NewGuid().ToString()[..8]}";
        var comment = new Comment(postId, author, content, DateTime.UtcNow);

        commentTree.Stash(commentId, comment);

        AnsiConsole.MarkupLine("[green]✓ Comment added successfully![/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void SearchByTag(Tree<BlogPost> postTree)
    {
        AnsiConsole.Write(new Rule("[tan]Search by Tag[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var searchTag = AnsiConsole.Ask<string>("[green]Tag to search:[/]").Trim().ToLower();

        var posts = postTree.NutShells()
            .Where(p => p.Payload.Tags.Any(t => t.ToLower().Contains(searchTag)))
            .OrderByDescending(p => p.Payload.CreatedAt)
            .ToList();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[tan]Posts tagged with[/] [yellow]'{Markup.Escape(searchTag)}'[/]:");
        AnsiConsole.WriteLine();

        if (!posts.Any())
        {
            AnsiConsole.MarkupLine($"[dim]No posts found with tag '{Markup.Escape(searchTag)}'[/]");
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
            .AddColumn(new TableColumn("[tan bold]Tags[/]"));

        foreach (var nut in posts)
        {
            var post = nut.Payload;
            table.AddRow(
                $"[yellow]{nut.Id}[/]",
                $"[white]{Markup.Escape(post.Title)}[/]",
                $"[dim]{Markup.Escape(post.Author)}[/]",
                $"[olive]{Markup.Escape(string.Join(", ", post.Tags))}[/]");
        }

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[tan]Found:[/] {posts.Count} post(s)");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }
}
