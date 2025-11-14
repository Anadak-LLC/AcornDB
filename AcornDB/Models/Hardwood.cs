
using System;
using System.Collections.Generic;
using AcornDB.Logging;

namespace AcornDB.Models
{
    public interface IWood
    {
        void Grow(Grove grove);
    }

    public partial class Hardwood : IWood
    {
        public int Port { get; set; } = 5000;
        public bool EnableDiscovery { get; set; } = true;

        // Basic Grow implementation - can be enhanced in Canopy project
        public void Grow(Grove grove)
        {
            AcornLog.Info($"🌳 Hardwood growing on port {Port}...");
            // Extended implementation in HardwoodServer.cs (Canopy project)
        }
    }

    public partial class Grove
    {
        private readonly List<IWood> _woods = new();

        public void ExposeWood(IWood wood)
        {
            wood.Grow(this);
            _woods.Add(wood);
        }
    }
}
