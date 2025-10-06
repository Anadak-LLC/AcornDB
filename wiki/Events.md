# 🔔 Events & Subscriptions

AcornDB is built on **Reactive Extensions (Rx.NET)**, allowing you to subscribe to changes and react to data updates in real-time.

## Event Architecture

```
Tree<T> ──► Stash/Toss ──► EventManager<T> ──► Subscribers
                                │
                                ├──► Callback 1
                                ├──► Callback 2
                                └──► Callback N
```

---

## 📡 EventManager\<T\>

The `EventManager<T>` class uses Rx.NET's `Subject<T>` to broadcast changes.

### Structure

```csharp
public class EventManager<T>
{
    private readonly Subject<T> _subject = new Subject<T>();

    public void Subscribe(Action<T> callback)
    {
        _subject.Subscribe(callback);
    }

    public void RaiseChanged(T document)
    {
        _subject.OnNext(document);
    }
}
```

---

## 🎧 Subscribing to Changes

### Basic Subscription

```csharp
var tree = new Tree<User>(new FileTrunk<User>("data/users"));

tree.EventManager.Subscribe(user =>
{
    Console.WriteLine($"User changed: {user.Name}");
});

tree.Stash("alice", new User { Name = "Alice" });
// Output: User changed: Alice
```

### Multiple Subscribers

```csharp
// Subscriber 1: Log to console
tree.EventManager.Subscribe(user =>
{
    Console.WriteLine($"[LOG] {user.Name} updated");
});

// Subscriber 2: Send email notification
tree.EventManager.Subscribe(user =>
{
    SendEmail($"{user.Name} was updated");
});

// Subscriber 3: Update cache
tree.EventManager.Subscribe(user =>
{
    cache.Set(user.Id, user);
});

tree.Stash("bob", new User { Name = "Bob" });
// All 3 subscribers are notified
```

---

## 🔥 Common Event Patterns

### Audit Logging

```csharp
var auditLog = new List<string>();

tree.EventManager.Subscribe(user =>
{
    auditLog.Add($"{DateTime.UtcNow}: User {user.Name} modified");
});

tree.Stash("alice", new User { Name = "Alice" });
tree.Stash("bob", new User { Name = "Bob" });

foreach (var entry in auditLog)
{
    Console.WriteLine(entry);
}
// Output:
// 2025-10-06 12:00:00: User Alice modified
// 2025-10-06 12:00:05: User Bob modified
```

### Real-Time Dashboard Updates

```csharp
tree.EventManager.Subscribe(user =>
{
    // Push update to SignalR hub
    hubContext.Clients.All.SendAsync("UserUpdated", user);
});
```

### Cache Invalidation

```csharp
tree.EventManager.Subscribe(user =>
{
    cache.Remove($"user-{user.Id}");
    Console.WriteLine($"Cache invalidated for {user.Id}");
});
```

### Webhook Triggers

```csharp
tree.EventManager.Subscribe(async user =>
{
    var client = new HttpClient();
    var json = JsonSerializer.Serialize(user);
    await client.PostAsync("https://webhook.site/xyz", new StringContent(json));
});
```

---

## 🎯 Filtering Events with Rx Operators

Since `EventManager` uses Rx.NET, you can leverage powerful operators.

### Filter by Condition

```csharp
tree.EventManager._subject
    .Where(user => user.Name.StartsWith("A"))
    .Subscribe(user =>
    {
        Console.WriteLine($"User starting with A: {user.Name}");
    });

tree.Stash("alice", new User { Name = "Alice" }); // Triggers
tree.Stash("bob", new User { Name = "Bob" });     // Ignored
```

### Throttle Events

```csharp
tree.EventManager._subject
    .Throttle(TimeSpan.FromSeconds(1))
    .Subscribe(user =>
    {
        Console.WriteLine($"Throttled update: {user.Name}");
    });

// Only fires once per second, even if stashed 100 times
```

### Batch Events

```csharp
tree.EventManager._subject
    .Buffer(TimeSpan.FromSeconds(5))
    .Subscribe(users =>
    {
        Console.WriteLine($"Batch update: {users.Count} users changed");
    });
```

---

## 🌲 Grove-Level Events

While individual Trees have their own `EventManager`, you can create a grove-wide event system.

### Custom Grove Event Manager

```csharp
public class GroveEventManager
{
    private readonly Subject<object> _subject = new Subject<object>();

    public void Subscribe(Action<object> callback)
    {
        _subject.Subscribe(callback);
    }

    public void RaiseChanged(object document)
    {
        _subject.OnNext(document);
    }
}

var groveEvents = new GroveEventManager();

// Subscribe to all changes across all trees
groveEvents.Subscribe(obj =>
{
    Console.WriteLine($"Grove change detected: {obj.GetType().Name}");
});

// Wire up individual trees
userTree.EventManager.Subscribe(user => groveEvents.RaiseChanged(user));
productTree.EventManager.Subscribe(product => groveEvents.RaiseChanged(product));
```

---

## 🪢 Tangle Events (Sync Notifications)

While Tangles don't have built-in event managers (yet), you can track sync events manually.

### Custom Tangle Listener

```csharp
public class SyncEventManager
{
    private readonly Subject<(string Id, object Payload)> _subject = new();

    public void OnSync(string id, object payload)
    {
        _subject.OnNext((id, payload));
    }

    public void Subscribe(Action<(string Id, object Payload)> callback)
    {
        _subject.Subscribe(callback);
    }
}

var syncEvents = new SyncEventManager();

syncEvents.Subscribe(evt =>
{
    Console.WriteLine($"Synced: {evt.Id} to remote");
});

// Hook into Tangle pushes
tangle.PushUpdate("alice", alice);
syncEvents.OnSync("alice", alice);
```

---

## 🚨 Error Handling in Subscriptions

### Try-Catch in Callbacks

```csharp
tree.EventManager.Subscribe(user =>
{
    try
    {
        // Risky operation
        SendEmail(user.Email);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in subscriber: {ex.Message}");
    }
});
```

### Rx Error Handling

```csharp
tree.EventManager._subject
    .Retry(3) // Retry up to 3 times on error
    .Subscribe(
        onNext: user => Console.WriteLine($"User: {user.Name}"),
        onError: ex => Console.WriteLine($"Error: {ex.Message}")
    );
```

---

## 🧪 Testing with Events

### Mock Subscribers for Tests

```csharp
[Fact]
public void Test_EventFires_OnStash()
{
    var tree = new Tree<User>(new MemoryTrunk<User>());
    var eventFired = false;

    tree.EventManager.Subscribe(user =>
    {
        eventFired = true;
        Assert.Equal("Alice", user.Name);
    });

    tree.Stash("alice", new User { Name = "Alice" });

    Assert.True(eventFired);
}
```

### Count Events

```csharp
[Fact]
public void Test_MultipleStashes_FireMultipleEvents()
{
    var tree = new Tree<User>(new MemoryTrunk<User>());
    var count = 0;

    tree.EventManager.Subscribe(_ => count++);

    tree.Stash("alice", new User { Name = "Alice" });
    tree.Stash("bob", new User { Name = "Bob" });

    Assert.Equal(2, count);
}
```

---

## 🔮 Future: Built-in Event Types

**Coming Soon:**

```csharp
public enum TreeEventType
{
    Stashed,
    Cracked,
    Tossed,
    Squabbled,
    Synced
}

public class TreeEvent<T>
{
    public TreeEventType Type { get; set; }
    public string Id { get; set; }
    public T Payload { get; set; }
    public DateTime Timestamp { get; set; }
}

tree.EventManager.Subscribe(evt =>
{
    switch (evt.Type)
    {
        case TreeEventType.Stashed:
            Console.WriteLine($"Stashed: {evt.Id}");
            break;
        case TreeEventType.Tossed:
            Console.WriteLine($"Tossed: {evt.Id}");
            break;
    }
});
```

---

## 📊 Event Statistics

Track event activity:

```csharp
public class EventStats
{
    private int _eventCount = 0;
    private DateTime _lastEvent = DateTime.MinValue;

    public EventStats(EventManager<User> eventManager)
    {
        eventManager.Subscribe(_ =>
        {
            _eventCount++;
            _lastEvent = DateTime.UtcNow;
        });
    }

    public int EventCount => _eventCount;
    public DateTime LastEvent => _lastEvent;
}

var stats = new EventStats(tree.EventManager);

tree.Stash("alice", new User { Name = "Alice" });
tree.Stash("bob", new User { Name = "Bob" });

Console.WriteLine($"Events fired: {stats.EventCount}");
Console.WriteLine($"Last event at: {stats.LastEvent}");
```

---

## 🧭 Best Practices

### ✅ Do:
- Use events for side effects (logging, notifications, cache invalidation)
- Unsubscribe when done (if using IDisposable subscriptions)
- Handle errors inside callbacks
- Use Rx operators for filtering and throttling

### ❌ Don't:
- Perform heavy computations inside event callbacks (use async tasks)
- Modify the same Tree inside its own event callback (risk of infinite loops)
- Rely on event order across different subscribers
- Throw unhandled exceptions in callbacks

---

## 🧭 Navigation

- **Previous:** [[Data Sync]] - Branches, Tangles, and sync strategies
- **Next:** [[Conflict Resolution]] - Squabbles and custom judges
- **Related:** [[Getting Started]] - Basic Tree operations

🌰 *Your trees are now reactive! Listen to the rustle of every leaf.*
