using AcornDB.Models;
using Microsoft.AspNetCore.Mvc;

namespace AcornDB.Sync
{
    /// <summary>
    /// TreeBark: HTTP sync server for AcornDB
    /// Provides REST endpoints for remote Tree synchronization
    /// </summary>
    public class TreeBark
    {
        private readonly Grove _grove;
        private readonly int _port;

        public TreeBark(Grove grove, int port = 5000)
        {
            _grove = grove;
            _port = port;
        }

        /// <summary>
        /// Start the TreeBark sync server
        /// </summary>
        public void Start()
        {
            Console.WriteLine($"🌰 TreeBark starting on port {_port}...");
            // Implementation in Program.cs
        }

        /// <summary>
        /// Register a tree type to be available for sync
        /// </summary>
        public void RegisterTree<T>(Tree<T> tree)
        {
            _grove.Plant(tree);
            Console.WriteLine($"🌳 TreeBark registered Tree<{typeof(T).Name}>");
        }
    }

    /// <summary>
    /// Extension methods for easy TreeBark setup
    /// </summary>
    public static class TreeBarkExtensions
    {
        /// <summary>
        /// Create and start a TreeBark sync server
        /// </summary>
        public static TreeBark CreateTreeBark(this Grove grove, int port = 5000)
        {
            return new TreeBark(grove, port);
        }

        /// <summary>
        /// Quick start: Plant tree and expose via TreeBark
        /// </summary>
        public static void PlantAndBark<T>(this Grove grove, Tree<T> tree, int port = 5000)
        {
            grove.Plant(tree);
            var bark = grove.CreateTreeBark(port);
            bark.Start();
        }
    }
}
