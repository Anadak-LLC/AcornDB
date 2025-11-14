using AcornDB;
using AcornDB.Storage;
using AcornDB.Cache;
using AcornDB.Models;
using Spectre.Console;

namespace AcornDB.SampleApps.Samples;

/// <summary>
/// Sample 3: E-Commerce System
///
/// Demonstrates:
/// - Product catalog management
/// - Shopping cart functionality
/// - Order processing
/// - LRU caching for frequently accessed products
/// - Multi-tree management with Grove
/// </summary>
public static class ECommerceApp
{
    private record Product(
        string Name,
        string Description,
        decimal Price,
        int Stock,
        string Category
    );

    private record CartItem(
        string ProductId,
        int Quantity
    );

    private record Order(
        string CustomerId,
        List<CartItem> Items,
        decimal TotalAmount,
        DateTime OrderedAt,
        string Status // Pending, Processing, Shipped, Delivered
    );

    public static async Task Run()
    {
        AnsiConsole.Clear();

        // AcornDB themed header
        var rule = new Rule("[tan bold]E-Commerce System[/]")
        {
            Justification = Justify.Left,
            Style = Style.Parse("tan")
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        // Setup: Grove with caching for products
        var grove = new Grove();

        // Products tree with LRU cache (hot products stay in memory)
        var productTree = new Tree<Product>(new DocumentStoreTrunk<Product>("data/sample-products"));
        productTree.CacheStrategy = new LRUCacheStrategy<Product>(maxSize: 20);
        grove.Plant(productTree);

        // Orders tree
        grove.Plant(new Tree<Order>(new DocumentStoreTrunk<Order>("data/sample-orders")));

        // In-memory cart (doesn't need persistence)
        var cart = new List<CartItem>();

        AnsiConsole.MarkupLine("[dim]✓ Initialized with Grove + LRU caching[/]");
        AnsiConsole.WriteLine();

        // Seed some sample products if empty
        if (!productTree.NutShells().Any())
        {
            SeedProducts(productTree);
        }

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[tan bold]What would you like to do?[/]")
                    .PageSize(12)
                    .AddChoices(new[] {
                        "Browse Products",
                        "View Product Details",
                        "Add to Cart",
                        "View Cart",
                        "Checkout",
                        "View Orders",
                        "Manage Inventory",
                        "Back to Main Menu"
                    }));

            AnsiConsole.WriteLine();

            switch (choice)
            {
                case "Browse Products":
                    BrowseProducts(productTree);
                    break;
                case "View Product Details":
                    ViewProduct(productTree);
                    break;
                case "Add to Cart":
                    AddToCart(productTree, cart);
                    break;
                case "View Cart":
                    ViewCart(productTree, cart);
                    break;
                case "Checkout":
                    Checkout(productTree, grove.GetTree<Order>()!, cart);
                    break;
                case "View Orders":
                    ViewOrders(grove.GetTree<Order>()!);
                    break;
                case "Manage Inventory":
                    ManageInventory(productTree);
                    break;
                case "Back to Main Menu":
                    return;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static void SeedProducts(Tree<Product> tree)
    {
        var products = new[]
        {
            new Product("Laptop Pro 15", "High-performance laptop", 1299.99m, 15, "Electronics"),
            new Product("Wireless Mouse", "Ergonomic wireless mouse", 29.99m, 50, "Electronics"),
            new Product("Mechanical Keyboard", "RGB mechanical keyboard", 89.99m, 30, "Electronics"),
            new Product("USB-C Hub", "7-in-1 USB-C hub", 49.99m, 25, "Electronics"),
            new Product("Desk Lamp", "LED desk lamp with USB charging", 39.99m, 40, "Office"),
            new Product("Office Chair", "Ergonomic office chair", 299.99m, 10, "Furniture"),
            new Product("Standing Desk", "Adjustable standing desk", 449.99m, 8, "Furniture"),
            new Product("Notebook Set", "Pack of 3 premium notebooks", 19.99m, 100, "Office"),
        };

        for (int i = 0; i < products.Length; i++)
        {
            tree.Stash($"prod-{i + 1:D3}", products[i]);
        }

        AnsiConsole.MarkupLine("[dim]  ✓ Seeded 8 sample products[/]");
    }

    private static void BrowseProducts(Tree<Product> tree)
    {
        AnsiConsole.Write(new Rule("[tan]Product Catalog[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var products = tree.NutShells().OrderBy(p => p.Payload.Category).ThenBy(p => p.Payload.Name).ToList();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Tan)
            .AddColumn(new TableColumn("[tan bold]ID[/]"))
            .AddColumn(new TableColumn("[tan bold]Product[/]"))
            .AddColumn(new TableColumn("[tan bold]Category[/]"))
            .AddColumn(new TableColumn("[tan bold]Price[/]").RightAligned())
            .AddColumn(new TableColumn("[tan bold]Stock[/]").Centered());

        foreach (var nut in products)
        {
            var product = nut.Payload;
            var stockColor = product.Stock > 10 ? "green" : product.Stock > 0 ? "yellow" : "red";
            var stockText = product.Stock > 0 ? $"{product.Stock}" : "OUT";

            table.AddRow(
                $"[yellow]{nut.Id}[/]",
                $"[white]{Markup.Escape(product.Name)}[/]",
                $"[olive]{Markup.Escape(product.Category)}[/]",
                $"[green]${product.Price:F2}[/]",
                $"[{stockColor}]{stockText}[/]");
        }

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[tan]Total products:[/] {products.Count}");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ViewProduct(Tree<Product> tree)
    {
        AnsiConsole.Write(new Rule("[tan]Product Details[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var productId = AnsiConsole.Ask<string>("[green]Product ID:[/]");

        var product = tree.Crack(productId);
        if (product == null)
        {
            AnsiConsole.MarkupLine("[red]✗ Product not found.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var stockColor = product.Stock > 10 ? "green" : product.Stock > 0 ? "yellow" : "red";
        var stockText = product.Stock > 0 ? $"{product.Stock} in stock" : "OUT OF STOCK";

        var panel = new Panel(
            new Markup(
                $"[tan bold]{Markup.Escape(product.Name)}[/]\n\n" +
                $"[dim]Category:[/] [olive]{Markup.Escape(product.Category)}[/]\n" +
                $"[dim]Price:[/] [green]${product.Price:F2}[/]\n" +
                $"[dim]Stock:[/] [{stockColor}]{stockText}[/]\n\n" +
                $"[white]{Markup.Escape(product.Description)}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("tan"),
            Padding = new Padding(2, 1)
        };
        AnsiConsole.Write(panel);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void AddToCart(Tree<Product> tree, List<CartItem> cart)
    {
        AnsiConsole.Write(new Rule("[tan]Add to Cart[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var productId = AnsiConsole.Ask<string>("[green]Product ID:[/]");

        var product = tree.Crack(productId);
        if (product == null)
        {
            AnsiConsole.MarkupLine("[red]✗ Product not found.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        if (product.Stock <= 0)
        {
            AnsiConsole.MarkupLine("[red]✗ Product is out of stock.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var quantity = AnsiConsole.Ask<int>($"[green]Quantity[/] [dim](max {product.Stock}):[/]");

        if (quantity <= 0)
        {
            AnsiConsole.MarkupLine("[red]✗ Invalid quantity.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        if (quantity > product.Stock)
        {
            AnsiConsole.MarkupLine($"[red]✗ Only {product.Stock} available.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var existingItem = cart.FirstOrDefault(c => c.ProductId == productId);
        if (existingItem != null)
        {
            cart.Remove(existingItem);
            quantity += existingItem.Quantity;
        }

        cart.Add(new CartItem(productId, quantity));
        AnsiConsole.MarkupLine($"[green]✓ Added {quantity}x {Markup.Escape(product.Name)} to cart[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ViewCart(Tree<Product> tree, List<CartItem> cart)
    {
        AnsiConsole.Write(new Rule("[tan]Shopping Cart[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        if (!cart.Any())
        {
            AnsiConsole.MarkupLine("[dim]Cart is empty. Start shopping![/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Tan)
            .AddColumn(new TableColumn("[tan bold]Product[/]"))
            .AddColumn(new TableColumn("[tan bold]Price[/]").RightAligned())
            .AddColumn(new TableColumn("[tan bold]Qty[/]").Centered())
            .AddColumn(new TableColumn("[tan bold]Subtotal[/]").RightAligned());

        decimal total = 0;
        foreach (var item in cart)
        {
            var product = tree.Crack(item.ProductId);
            if (product != null)
            {
                var itemTotal = product.Price * item.Quantity;
                total += itemTotal;
                table.AddRow(
                    $"[white]{Markup.Escape(product.Name)}[/]",
                    $"[dim]${product.Price:F2}[/]",
                    $"[olive]{item.Quantity}[/]",
                    $"[green]${itemTotal:F2}[/]");
            }
        }

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        var totalPanel = new Panel($"[tan bold]Total:[/] [green bold]${total:F2}[/]")
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("green"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(totalPanel);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void Checkout(Tree<Product> tree, Tree<Order> orderTree, List<CartItem> cart)
    {
        AnsiConsole.Write(new Rule("[tan]Checkout[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        if (!cart.Any())
        {
            AnsiConsole.MarkupLine("[red]✗ Cart is empty.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var customerId = AnsiConsole.Ask<string>("[green]Customer ID (email):[/]");

        // Calculate total and verify stock
        decimal total = 0;
        foreach (var item in cart)
        {
            var product = tree.Crack(item.ProductId);
            if (product == null)
            {
                AnsiConsole.MarkupLine($"[red]✗ Error: Product {item.ProductId} not found.[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
                Console.ReadKey(true);
                return;
            }

            if (product.Stock < item.Quantity)
            {
                AnsiConsole.MarkupLine($"[red]✗ Error: Insufficient stock for {Markup.Escape(product.Name)}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
                Console.ReadKey(true);
                return;
            }

            total += product.Price * item.Quantity;
        }

        // Create order
        var orderId = $"order-{Guid.NewGuid().ToString()[..8]}";
        var order = new Order(customerId, new List<CartItem>(cart), total, DateTime.UtcNow, "Pending");
        orderTree.Stash(orderId, order);

        // Update stock
        foreach (var item in cart)
        {
            var product = tree.Crack(item.ProductId)!;
            var updated = product with { Stock = product.Stock - item.Quantity };
            tree.Stash(item.ProductId, updated);
        }

        cart.Clear();

        var successPanel = new Panel(
            new Markup(
                $"[green bold]✓ Order placed successfully![/]\n\n" +
                $"[dim]Order ID:[/] [yellow]{orderId}[/]\n" +
                $"[dim]Total:[/] [green]${total:F2}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("green"),
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(successPanel);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ViewOrders(Tree<Order> orderTree)
    {
        AnsiConsole.Write(new Rule("[tan]All Orders[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var orders = orderTree.NutShells().OrderByDescending(o => o.Payload.OrderedAt).ToList();

        if (!orders.Any())
        {
            AnsiConsole.MarkupLine("[dim]No orders yet.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Tan)
            .AddColumn(new TableColumn("[tan bold]Order ID[/]"))
            .AddColumn(new TableColumn("[tan bold]Customer[/]"))
            .AddColumn(new TableColumn("[tan bold]Status[/]"))
            .AddColumn(new TableColumn("[tan bold]Amount[/]").RightAligned())
            .AddColumn(new TableColumn("[tan bold]Items[/]").Centered())
            .AddColumn(new TableColumn("[tan bold]Date[/]"));

        foreach (var nut in orders)
        {
            var order = nut.Payload;
            var statusColor = order.Status switch
            {
                "Pending" => "yellow",
                "Processing" => "olive",
                "Shipped" => "blue",
                "Delivered" => "green",
                _ => "dim"
            };

            table.AddRow(
                $"[yellow]{nut.Id}[/]",
                $"[dim]{Markup.Escape(order.CustomerId)}[/]",
                $"[{statusColor}]{order.Status}[/]",
                $"[green]${order.TotalAmount:F2}[/]",
                $"[olive]{order.Items.Count}[/]",
                $"[dim]{order.OrderedAt:g}[/]");
        }

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[tan]Total orders:[/] {orders.Count}");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static void ManageInventory(Tree<Product> tree)
    {
        AnsiConsole.Write(new Rule("[tan]Manage Inventory[/]") { Style = Style.Parse("olive") });
        AnsiConsole.WriteLine();

        var productId = AnsiConsole.Ask<string>("[green]Product ID[/] [dim](leave empty for new):[/]");

        Product? existing = null;
        if (!string.IsNullOrEmpty(productId))
        {
            existing = tree.Crack(productId);
            if (existing == null)
            {
                AnsiConsole.MarkupLine("[red]✗ Product not found.[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
                Console.ReadKey(true);
                return;
            }
            AnsiConsole.MarkupLine($"[dim]Editing:[/] [white]{Markup.Escape(existing.Name)}[/]");
            AnsiConsole.WriteLine();
        }
        else
        {
            productId = $"prod-{Guid.NewGuid().ToString()[..8]}";
        }

        var name = AnsiConsole.Ask<string>(
            $"[green]Name[/] [dim][{Markup.Escape(existing?.Name ?? "")}]:[/]",
            existing?.Name ?? "");

        var desc = AnsiConsole.Ask<string>(
            $"[green]Description[/] [dim][{Markup.Escape(existing?.Description ?? "")}]:[/]",
            existing?.Description ?? "");

        var price = AnsiConsole.Ask<decimal>(
            $"[green]Price[/] [dim][{existing?.Price:F2}]:[/]",
            existing?.Price ?? 0);

        var stock = AnsiConsole.Ask<int>(
            $"[green]Stock[/] [dim][{existing?.Stock}]:[/]",
            existing?.Stock ?? 0);

        var category = AnsiConsole.Ask<string>(
            $"[green]Category[/] [dim][{Markup.Escape(existing?.Category ?? "")}]:[/]",
            existing?.Category ?? "");

        var product = new Product(name, desc, price, stock, category);
        tree.Stash(productId, product);

        AnsiConsole.MarkupLine($"[green]✓ Product {(existing == null ? "created" : "updated")}:[/] [yellow]{productId}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }
}
