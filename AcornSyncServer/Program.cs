using AcornDB;
using AcornDB.Models;
using AcornDB.Storage;

var builder = WebApplication.CreateBuilder(args);

// Register Grove as singleton
builder.Services.AddSingleton<Grove>();

var app = builder.Build();

// Get Grove from DI and plant default trees
var grove = app.Services.GetRequiredService<Grove>();

// Plant a User tree with DocumentStoreTrunk for full versioning
grove.Plant(new Tree<User>(new DocumentStoreTrunk<User>("data/server/users")));
Console.WriteLine("🌳 Planted Tree<User> with DocumentStoreTrunk");

// Plant a Product tree for demo purposes
grove.Plant(new Tree<Product>(new DocumentStoreTrunk<Product>("data/server/products")));
Console.WriteLine("🌳 Planted Tree<Product> with DocumentStoreTrunk\n");

// TreeBark API endpoints
var bark = app.MapGroup("/bark");

bark.MapPost("/{treeName}/stash", async (string treeName, HttpContext context) =>
{
    var json = await new StreamReader(context.Request.Body).ReadToEndAsync();

    // Try to stash to the appropriate tree
    if (grove.TryStash(treeName, ExtractId(json), json))
    {
        return Results.Ok(new { message = "Stashed!" });
    }

    return Results.BadRequest(new { message = $"Tree '{treeName}' not found in grove" });
});

bark.MapGet("/{treeName}/crack/{id}", (string treeName, string id, Grove grove) =>
{
    var result = grove.TryCrack(treeName, id);
    if (result != null)
    {
        return Results.Ok(result);
    }
    return Results.NotFound(new { message = $"Nut '{id}' not found in tree '{treeName}'" });
});

bark.MapDelete("/{treeName}/toss/{id}", (string treeName, string id, Grove grove) =>
{
    if (grove.TryToss(treeName, id))
    {
        return Results.Ok(new { message = "Tossed!" });
    }
    return Results.NotFound(new { message = $"Tree '{treeName}' not found or nut '{id}' not found" });
});

bark.MapGet("/{treeName}/export", (string treeName, Grove grove) =>
{
    var changes = grove.ExportChanges(treeName);
    if (!changes.Any())
    {
        return Results.NotFound(new { message = $"Tree '{treeName}' not found or has no data" });
    }

    return Results.Ok(changes);
});

bark.MapPost("/{treeName}/import", async (string treeName, HttpContext context, Grove grove) =>
{
    var tree = grove.GetTreeByTypeName(treeName);
    if (tree == null)
    {
        return Results.NotFound(new { message = $"Tree '{treeName}' not found" });
    }

    var json = await new StreamReader(context.Request.Body).ReadToEndAsync();
    var type = tree.GetType().GenericTypeArguments[0];
    var shellListType = typeof(List<>).MakeGenericType(typeof(NutShell<>).MakeGenericType(type));
    var changes = System.Text.Json.JsonSerializer.Deserialize(json, shellListType);

    if (changes == null)
    {
        return Results.BadRequest(new { message = "Invalid import data" });
    }

    grove.ImportChanges(treeName, (IEnumerable<object>)changes);

    return Results.Ok(new { message = "Imported!" });
});

// Health check
app.MapGet("/", () => new
{
    service = "🌰 AcornDB TreeBark Sync Server",
    status = "running",
    endpoints = new[]
    {
        "POST /bark/{treeName}/stash",
        "GET /bark/{treeName}/crack/{id}",
        "DELETE /bark/{treeName}/toss/{id}",
        "GET /bark/{treeName}/export",
        "POST /bark/{treeName}/import"
    }
});

Console.WriteLine("🌰 TreeBark Sync Server starting...");
Console.WriteLine($"🌐 Listening on: {builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5000"}");

app.Run();

// Helper to extract ID from JSON payload
static string ExtractId(string json)
{
    using var doc = System.Text.Json.JsonDocument.Parse(json);
    if (doc.RootElement.TryGetProperty("Id", out var idProp))
    {
        return idProp.GetString() ?? "unknown";
    }
    if (doc.RootElement.TryGetProperty("id", out var idPropLower))
    {
        return idPropLower.GetString() ?? "unknown";
    }
    return "unknown";
}

// Demo model classes
public class User
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class Product
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}