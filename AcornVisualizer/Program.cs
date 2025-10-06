using AcornDB;
using AcornDB.Models;
using AcornDB.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSingleton<Grove>();

var app = builder.Build();

// Get Grove and plant some demo trees
var grove = app.Services.GetRequiredService<Grove>();

// Plant demo trees (users can customize this)
grove.Plant(new Tree<User>(new DocumentStoreTrunk<User>("data/visualizer/users")));
grove.Plant(new Tree<Product>(new DocumentStoreTrunk<Product>("data/visualizer/products")));

Console.WriteLine("🌰 AcornDB Visualizer");
Console.WriteLine("=====================");
Console.WriteLine($"🌳 Planted {grove.TreeCount} trees");
Console.WriteLine();

// Configure middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();
app.MapControllers();

// Health check endpoint
app.MapGet("/api/health", () => new
{
    service = "🌰 AcornDB Visualizer",
    status = "running",
    trees = grove.TreeCount
});

Console.WriteLine($"🌐 Visualizer running on: {builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5100"}");
Console.WriteLine("   Open your browser to view the Grove!");
Console.WriteLine();

app.Run();

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
