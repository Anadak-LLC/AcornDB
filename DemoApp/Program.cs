using AcornDB;

class Program
{
    static void Main()
    {
        var db = new AcornDb("demo-data.acorn");
        var users = db.GetCollection<User>("users");

        users.Insert(new User { Id = 1, Name = "Taylor" });

        users.OnChanged(user => Console.WriteLine($">> Changed: {user.Id} - {user.Name}"));

        users.Update(1, u => u.Name = "Updated Taylor");

        Console.WriteLine("Done.");
    }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
}
