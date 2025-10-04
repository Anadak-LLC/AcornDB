using System;
using System.IO;
using System.Threading;
using AcornDB;
using AcornDB.Models;
using AcornDB.Storage;
using AcornDB.Serialization;

namespace AcornDB.Demo
{
    public class User : INutment<int>
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class Program
    {
        static void Main()
        {
            Console.WriteLine("🌰 AcornDB AutoSync Demo");

            var basePathA = Path.Combine(Directory.GetCurrentDirectory(), "NodeA");
            var basePathB = Path.Combine(Directory.GetCurrentDirectory(), "NodeB");

            Directory.CreateDirectory(basePathA);
            Directory.CreateDirectory(basePathB);

            var serializer = new JsonNetSerializer();

            var usersA = new Collection<User>(new DocumentStore<User>($"{basePathA}/users", "users", serializer));
            var usersB = new Collection<User>(new DocumentStore<User>($"{basePathB}/users", "users", serializer));

            var sync = new AutoSync<User>(usersA, usersB, TimeSpan.FromSeconds(5));
            sync.Start();

            Console.WriteLine("AutoSync started. Making some nutty changes...");

            usersA.Stash(new User { Id = 1, Name = "Alice the Squirrel" });
            Thread.Sleep(2000);
            usersA.Stash(new User { Id = 2, Name = "Bob the Nutcracker" });

            Thread.Sleep(10000);

            var bob = usersB.Crack(2);
            Console.WriteLine($"✅ Synced to NodeB: {bob.Name}");

            Console.WriteLine("Done. Press any key to exit...");
            Console.ReadKey();

            sync.Stop();
        }
    }
}
