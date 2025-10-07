using System;
using System.Threading;
using AcornDB;

namespace AcornDB.Demo
{
    /// <summary>
    /// 🐿️ GitHubTrunk Demo - Your database IS your Git history!
    ///
    /// Run this to see GitHubTrunk in action:
    /// - Every Stash() creates a Git commit
    /// - Full time-travel via GetHistory()
    /// - Auto-push support (if remote configured)
    /// </summary>
    public class GitTrunkDemo
    {
        public class User
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string Name { get; set; } = "";
            public int Age { get; set; }
            public string Status { get; set; } = "Active";
        }

        public static void Run()
        {
            Console.WriteLine("🌰 AcornDB - GitHubTrunk Demo");
            Console.WriteLine("=====================================\n");

            // Create a tree with Git storage
            Console.WriteLine("Creating tree with Git storage...");
            var tree = new Acorn<User>()
                .WithGitStorage(
                    repoPath: "./demo_git_db",
                    authorName: "Demo User",
                    authorEmail: "demo@acorndb.dev"
                )
                .Sprout();

            Console.WriteLine("✓ GitHubTrunk initialized!\n");

            // Stash some users (each creates a commit)
            Console.WriteLine("Stashing users (each is a Git commit)...\n");

            var alice = new User { Id = "alice", Name = "Alice", Age = 30, Status = "Active" };
            tree.Stash(alice);
            Thread.Sleep(100);

            var bob = new User { Id = "bob", Name = "Bob", Age = 25, Status = "Active" };
            tree.Stash(bob);
            Thread.Sleep(100);

            var charlie = new User { Id = "charlie", Name = "Charlie", Age = 35, Status = "Active" };
            tree.Stash(charlie);
            Thread.Sleep(100);

            Console.WriteLine("\n✓ All users stashed!\n");

            // Update Alice multiple times (version history)
            Console.WriteLine("Updating Alice multiple times (version history)...\n");

            alice.Age = 31;
            alice.Status = "Updated v1";
            tree.Stash(alice);
            Thread.Sleep(100);

            alice.Age = 32;
            alice.Status = "Updated v2";
            tree.Stash(alice);
            Thread.Sleep(100);

            alice.Age = 33;
            alice.Status = "Updated v3";
            tree.Stash(alice);

            Console.WriteLine("\n✓ Alice updated 3 times!\n");

            // Read current state
            Console.WriteLine("Reading current state...");
            var currentAlice = tree.Crack("alice");
            Console.WriteLine($"Current Alice: {currentAlice?.Name}, Age: {currentAlice?.Age}, Status: {currentAlice?.Status}\n");

            // Time-travel: Get Alice's full history
            Console.WriteLine("🕰️ Time-traveling through Alice's history...\n");
            var history = tree.GetHistory("alice");
            Console.WriteLine($"Alice has {history.Count} versions in Git history:\n");

            for (int i = 0; i < history.Count; i++)
            {
                var version = history[i];
                Console.WriteLine($"  Version {i + 1}:");
                Console.WriteLine($"    Name: {version.Payload.Name}");
                Console.WriteLine($"    Age: {version.Payload.Age}");
                Console.WriteLine($"    Status: {version.Payload.Status}");
                Console.WriteLine($"    Timestamp: {version.Timestamp:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine();
            }

            // Delete a user
            Console.WriteLine("Deleting Bob...");
            tree.Toss("bob");
            Console.WriteLine("✓ Bob deleted (Git commit created)\n");

            // Verify deletion
            var deletedBob = tree.Crack("bob");
            Console.WriteLine($"Bob exists: {deletedBob != null}\n");

            // Show all remaining users
            Console.WriteLine("All remaining users:");
            var allUsers = tree.GetAll();
            foreach (var user in allUsers)
            {
                Console.WriteLine($"  - {user.Name} (Age: {user.Age})");
            }

            Console.WriteLine("\n=====================================");
            Console.WriteLine("✓ Demo complete!");
            Console.WriteLine("\nCheck out your Git repository at: ./demo_git_db");
            Console.WriteLine("Try these commands:");
            Console.WriteLine("  cd demo_git_db");
            Console.WriteLine("  git log --oneline");
            Console.WriteLine("  git show HEAD:alice.json");
            Console.WriteLine("\n🐿️ Your database IS your Git history!\n");
        }
    }
}
