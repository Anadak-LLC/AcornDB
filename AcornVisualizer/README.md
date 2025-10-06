# 🌰 AcornDB Visualizer

A web-based interactive dashboard for exploring and managing your AcornDB Groves and Trees.

## 🚀 Features

- **Live Grove Dashboard** - Real-time stats on trees, nuts, and operations
- **Tree Explorer** - Browse all trees in your grove with detailed information
- **Interactive Graph View** - Visualize your grove as an interactive node graph
- **Nut Inspector** - View individual nut payloads with timestamps and metadata
- **Trunk Capabilities** - See which trunks support history, sync, and async operations
- **Auto-Refresh** - Dashboard updates every 5 seconds automatically

## 📸 Screenshots

### Main Dashboard
- Grove-wide statistics (trees, nuts, stashed, tossed, squabbles)
- List of all trees with trunk types and capabilities
- Click any tree to view its contents

### Graph Visualizer
- Circular layout of all trees in the grove
- Trunk type badges (D=DocumentStore, F=File, M=Memory, A=Azure)
- Nut counts displayed on each tree node
- Interactive - click nodes to navigate

## 🏃 Running the Visualizer

```bash
cd AcornVisualizer
dotnet run
```

Then open your browser to: **http://localhost:5100**

### Custom Port

```bash
dotnet run --urls "http://localhost:8080"
```

## 🌲 Customizing Your Grove

Edit `Program.cs` to plant your own trees:

```csharp
var grove = app.Services.GetRequiredService<Grove>();

// Plant your custom trees
grove.Plant(new Tree<MyModel>(new DocumentStoreTrunk<MyModel>("data/mymodels")));
grove.Plant(new Tree<AnotherModel>(new FileTrunk<AnotherModel>("data/another")));
```

## 📡 API Endpoints

The visualizer exposes REST APIs for programmatic access:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Health check |
| `/api/GroveGraph` | GET | Full grove graph data |
| `/api/GroveGraph/stats` | GET | Grove statistics |
| `/api/TreeData/{typeName}` | GET | Detailed tree information |
| `/api/TreeData/{typeName}/nuts` | GET | All nuts in a tree |

## 🏗️ Architecture

```
┌─────────────────────────────────────┐
│   Index.cshtml (Main Dashboard)    │
│   - Stats Panel                     │
│   - Tree List                       │
│   - Detail Panel                    │
└──────────────┬──────────────────────┘
               │
┌──────────────┴──────────────────────┐
│   Visualizer.cshtml (Graph View)   │
│   - Interactive Node Graph          │
│   - Circular Layout                 │
└──────────────┬──────────────────────┘
               │
┌──────────────┴──────────────────────┐
│   API Controllers                   │
│   - GroveGraphController            │
│   - TreeDataController              │
└──────────────┬──────────────────────┘
               │
┌──────────────┴──────────────────────┐
│   Grove (Singleton)                 │
│   - Manages all Trees               │
│   - Provides stats and metadata     │
└─────────────────────────────────────┘
```

## 🎨 Frontend

### canopy.js
JavaScript for:
- Fetching grove/tree data via REST API
- Updating UI dynamically
- Auto-refresh every 5 seconds
- Tree selection and detail display

### canopy.css
Styling with:
- Wood-themed color palette (#8B4513, #A0522D)
- Card-based responsive layout
- Smooth hover transitions
- Grid-based stats panel

## 🔧 Development

### Project Structure

```
AcornVisualizer/
├── Controllers/
│   ├── GroveGraphController.cs    # Grove-wide data API
│   └── TreeDataController.cs      # Tree-specific data API
├── Models/
│   └── GroveGraphDto.cs            # Data transfer objects
├── Pages/
│   ├── Index.cshtml                # Main dashboard page
│   ├── Index.cshtml.cs             # Page model
│   ├── Visualizer.cshtml           # Graph view page
│   └── Visualizer.cshtml.cs        # Page model
├── wwwroot/
│   ├── canopy.js                   # Client-side JavaScript
│   └── canopy.css                  # Styles
├── Program.cs                      # Application startup
└── AcornVisualizer.csproj          # Project file
```

### Tech Stack

- **ASP.NET Core 8.0** - Web framework
- **Razor Pages** - Server-side rendering
- **Web API** - REST endpoints
- **Vanilla JavaScript** - No frontend framework needed!
- **CSS Grid & Flexbox** - Responsive layout

## 🔌 Integration with AcornDB

The visualizer uses **dependency injection** to access a singleton `Grove`:

```csharp
builder.Services.AddSingleton<Grove>();
```

All trees planted in this grove are automatically visible in the visualizer.

## 🆚 Comparison with Other Tools

| Feature | AcornVisualizer | TreeBark Server | Canopy (SignalR) |
|---------|-----------------|-----------------|------------------|
| Web UI | ✅ Yes | ❌ No (REST only) | 🟡 Partial |
| Live Updates | ✅ 5sec polling | ❌ No | ✅ Real-time |
| Graph View | ✅ Yes | ❌ No | 🟡 Planned |
| Nut Inspector | ✅ Yes | ❌ No | 🟡 Basic |
| Setup | 🟢 Simple | 🟢 Simple | 🟡 Complex |
| Use Case | Local dev/debug | Remote sync | Production monitoring |

## 🧪 Use Cases

**Perfect For:**
- Local development and debugging
- Exploring grove contents during testing
- Visual demos and presentations
- Understanding trunk capabilities
- Monitoring nut operations

**Not Ideal For:**
- Production monitoring (use Canopy instead)
- Real-time collaboration (no WebSockets)
- Large groves (100+ trees may be slow)

## 🚦 Running with AcornSyncServer

You can run both the Visualizer and TreeBark server simultaneously:

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

Both will share the same `Grove` if configured correctly, or they can run independently.

## 🎯 Future Enhancements

- [ ] **Real-time Updates** - SignalR integration for live changes
- [ ] **History Timeline** - Visual timeline of DocumentStore changes
- [ ] **Diff Viewer** - Compare versions of nuts
- [ ] **Search & Filter** - Find nuts by ID, content, or timestamp
- [ ] **Export** - Download grove data as JSON/CSV
- [ ] **Dark Mode** - Toggle between light and dark themes
- [ ] **Custom Layouts** - Force-directed graph, tree layout, etc.

## 📝 Notes

- Requires .NET 8.0 or later
- Auto-refresh can be disabled by removing the `setInterval` in the Razor pages
- Runs on port 5100 by default (configurable)
- No database required - reads directly from Grove in memory

---

🌰 **Built with acorns and interactive visualizations!**
