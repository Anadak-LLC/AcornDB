# 📊 Dashboard & Visualization

**AcornVisualizer** is an interactive web dashboard for exploring, managing, and visualizing your AcornDB Groves and Trees.

## Overview

The AcornVisualizer provides a real-time view into your grove with:
- Live statistics (trees, nuts, operations)
- Interactive graph visualization
- Nut inspector with metadata
- Trunk capability detection
- Auto-refresh every 5 seconds

---

## 🚀 Quick Start

### Running the Visualizer

```bash
cd AcornVisualizer
dotnet run
```

Then open: **http://localhost:5100**

### Custom Port

```bash
dotnet run --urls "http://localhost:8080"
```

---

## 🎨 Features

### 1. **Live Grove Dashboard**

Real-time statistics panel showing:
- Total trees in the grove
- Total nuts stashed
- Total nuts tossed
- Squabbles resolved
- Active tangles
- Smushes performed

**Example:**

```
╔═══════════════════════════════════╗
║      Grove Statistics             ║
╠═══════════════════════════════════╣
║ Trees:           5                ║
║ Nuts Stashed:    1,234            ║
║ Nuts Tossed:     89               ║
║ Squabbles:       12               ║
║ Active Tangles:  3                ║
║ Smushes:         2                ║
╚═══════════════════════════════════╝
```

---

### 2. **Tree Explorer**

Browse all trees in your grove:

| Tree Type | Trunk Type | Nut Count | Capabilities |
|-----------|------------|-----------|--------------|
| User | DocumentStore | 150 | History, Sync, Durable |
| Product | File | 89 | Sync, Durable |
| Order | Memory | 42 | Sync |

**Trunk Capabilities:**
- 📚 **History** - Supports versioning and time-travel
- 🔄 **Sync** - Can export/import changes
- 💾 **Durable** - Persists data across restarts
- ⚡ **Async** - Supports async operations

---

### 3. **Interactive Graph View**

Visualize your grove as a circular node graph:

```
        User (150 nuts)
           │  [D]
           │
      ┌────┴────┐
      │         │
Product      Order
 [F]          [M]
(89 nuts)   (42 nuts)
```

**Trunk Type Badges:**
- **D** = DocumentStoreTrunk
- **F** = FileTrunk
- **M** = MemoryTrunk
- **A** = AzureTrunk

**Interactive Features:**
- Click nodes to navigate to tree details
- Hover for quick stats
- Circular layout auto-adjusts

---

### 4. **Nut Inspector**

View individual nut payloads with full metadata:

**Example:**

```json
{
  "Id": "alice",
  "Payload": {
    "Name": "Alice",
    "Email": "alice@woodland.io",
    "CreatedAt": "2025-10-06T12:00:00Z"
  },
  "Timestamp": "2025-10-06T12:05:30Z",
  "Version": 3,
  "ExpiresAt": null
}
```

**Features:**
- Syntax-highlighted JSON
- Timestamp display (local + UTC)
- Version history indicator
- TTL countdown (if ExpiresAt is set)

---

## 🏗️ Architecture

### Tech Stack

- **ASP.NET Core 8.0** - Web framework
- **Razor Pages** - Server-side rendering
- **Web API** - REST endpoints
- **Vanilla JavaScript** - Client-side logic
- **CSS Grid & Flexbox** - Responsive layout

### Project Structure

```
AcornVisualizer/
├── Controllers/
│   ├── GroveGraphController.cs    # Grove-wide data API
│   ├── TreeDataController.cs      # Tree-specific data API
│   └── GroveManagementController.cs
├── Models/
│   └── GroveGraphDto.cs            # Data transfer objects
├── Pages/
│   ├── Index.cshtml                # Main dashboard
│   ├── Visualizer.cshtml           # Graph view
│   └── TreeManager.cshtml          # Tree management
├── wwwroot/
│   ├── canopy.js                   # Client JavaScript
│   └── canopy.css                  # Styles
└── Program.cs                      # App startup
```

---

## 🔌 REST API Endpoints

The visualizer exposes REST APIs for programmatic access:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Health check |
| `/api/GroveGraph` | GET | Full grove graph data |
| `/api/GroveGraph/stats` | GET | Grove statistics |
| `/api/TreeData/{typeName}` | GET | Tree information |
| `/api/TreeData/{typeName}/nuts` | GET | All nuts in a tree |
| `/api/GroveManagement/stash` | POST | Stash a nut |
| `/api/GroveManagement/toss` | DELETE | Toss a nut |

### Example: Get Grove Stats

```bash
curl http://localhost:5100/api/GroveGraph/stats
```

**Response:**

```json
{
  "totalTrees": 3,
  "totalStashed": 1234,
  "totalTossed": 89,
  "totalSquabbles": 12,
  "totalSmushes": 2,
  "activeTangles": 3,
  "treeTypes": ["User", "Product", "Order"]
}
```

---

## 🌲 Customizing Your Grove

### Plant Custom Trees

Edit `Program.cs` to add your own trees:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Grove>();

var app = builder.Build();

// Retrieve the singleton Grove
var grove = app.Services.GetRequiredService<Grove>();

// Plant your custom trees
grove.Plant(new Tree<User>(new DocumentStoreTrunk<User>("data/users")));
grove.Plant(new Tree<Product>(new FileTrunk<Product>("data/products")));
grove.Plant(new Tree<Order>(new MemoryTrunk<Order>()));

app.Run();
```

---

## 🎨 Frontend Details

### canopy.js

JavaScript for:
- Fetching grove/tree data via REST
- Updating UI dynamically
- Auto-refresh every 5 seconds
- Tree selection and detail display

**Key Functions:**

```javascript
// Fetch and display grove stats
async function loadGroveStats() {
    const response = await fetch('/api/GroveGraph/stats');
    const stats = await response.json();
    document.getElementById('totalTrees').innerText = stats.totalTrees;
    document.getElementById('totalStashed').innerText = stats.totalStashed;
}

// Auto-refresh every 5 seconds
setInterval(loadGroveStats, 5000);
```

### canopy.css

Wood-themed styling:
- Color palette: `#8B4513` (Saddle Brown), `#A0522D` (Sienna)
- Card-based responsive layout
- Smooth hover transitions
- Grid-based stats panel

---

## 📈 Visualizer vs Other Tools

| Feature | AcornVisualizer | TreeBark | Canopy (SignalR) |
|---------|-----------------|----------|------------------|
| Web UI | ✅ Yes | ❌ No | 🟡 Partial |
| Live Updates | ✅ Polling (5sec) | ❌ No | ✅ Real-time (SignalR) |
| Graph View | ✅ Yes | ❌ No | 🟡 Planned |
| Nut Inspector | ✅ Yes | ❌ No | 🟡 Basic |
| Setup Complexity | 🟢 Simple | 🟢 Simple | 🟡 Complex |
| Use Case | Local dev/debug | Remote sync | Production monitoring |

---

## 🔄 Auto-Refresh

The dashboard auto-refreshes every 5 seconds to show live updates.

### Disable Auto-Refresh

Edit the Razor page to remove the interval:

```html
<script>
    // Remove this line to disable auto-refresh
    // setInterval(loadGroveStats, 5000);
</script>
```

### Custom Refresh Interval

```javascript
// Refresh every 10 seconds instead
setInterval(loadGroveStats, 10000);
```

---

## 🧪 Use Cases

### Perfect For:
- ✅ Local development and debugging
- ✅ Exploring grove contents during testing
- ✅ Visual demos and presentations
- ✅ Understanding trunk capabilities
- ✅ Monitoring nut operations

### Not Ideal For:
- ❌ Production monitoring (use Canopy instead)
- ❌ Real-time collaboration (no WebSockets)
- ❌ Large groves (100+ trees may be slow)

---

## 🚦 Running with TreeBark Server

Run both the Visualizer and TreeBark simultaneously:

**Terminal 1 - TreeBark Server:**

```bash
cd AcornSyncServer
dotnet run
```

**Terminal 2 - Visualizer:**

```bash
cd AcornVisualizer
dotnet run --urls "http://localhost:5100"
```

Both share the same `Grove` if configured correctly.

---

## 🔮 Future Enhancements

**Planned Features:**

- [ ] **Real-time Updates** - SignalR integration for live changes
- [ ] **History Timeline** - Visual timeline of DocumentStore changes
- [ ] **Diff Viewer** - Compare versions of nuts
- [ ] **Search & Filter** - Find nuts by ID, content, or timestamp
- [ ] **Export** - Download grove data as JSON/CSV
- [ ] **Dark Mode** - Toggle between light/dark themes
- [ ] **Custom Layouts** - Force-directed graph, tree layout
- [ ] **Tangle Monitor** - Visualize sync activity
- [ ] **Performance Metrics** - Charts for stash/crack/toss rates

---

## 🛠️ Development

### Adding a New Page

1. Create a new Razor page in `Pages/`:

```bash
dotnet new page -n MyFeature -o Pages
```

2. Add navigation link in `_Layout.cshtml`:

```html
<nav>
    <a href="/MyFeature">My Feature</a>
</nav>
```

### Adding a New API Endpoint

1. Create a controller in `Controllers/`:

```csharp
[ApiController]
[Route("api/[controller]")]
public class MyController : ControllerBase
{
    private readonly Grove _grove;

    public MyController(Grove grove)
    {
        _grove = grove;
    }

    [HttpGet("custom")]
    public IActionResult GetCustomData()
    {
        return Ok(new { message = "Hello from AcornDB!" });
    }
}
```

2. Call from JavaScript:

```javascript
const response = await fetch('/api/My/custom');
const data = await response.json();
console.log(data.message);
```

---

## 🎯 Keyboard Shortcuts (Future)

**Coming Soon:**

- `Ctrl+R` - Refresh dashboard
- `Ctrl+F` - Focus search bar
- `Ctrl+E` - Export grove data
- `Ctrl+D` - Toggle dark mode
- `Ctrl+G` - Open graph visualizer

---

## 📝 Configuration

### appsettings.json

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5100"
      }
    }
  },
  "AcornVisualizer": {
    "RefreshInterval": 5000,
    "MaxTreesDisplayed": 100,
    "EnableGraphView": true
  }
}
```

---

## 🧭 Best Practices

### ✅ Do:
- Use the visualizer for local development
- Customize the grove in `Program.cs`
- Check trunk capabilities before using features
- Use auto-refresh for monitoring
- Explore graph view for grove structure

### ❌ Don't:
- Expose the visualizer in production (no auth)
- Rely on it for critical monitoring
- Display sensitive data in nut inspector
- Use with extremely large groves (1000+ trees)

---

## 🧭 Navigation

- **Previous:** [[Cluster & Mesh]] - Multi-grove forests and mesh networking
- **Home:** [[Home]] - Return to wiki home
- **Related:** [[Getting Started]] - Basic setup and usage

🌰 *Your grove is now visible in all its glory. Monitor wisely!*
