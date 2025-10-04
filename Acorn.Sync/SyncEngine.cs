using System;
using System.Threading.Tasks;

namespace AcornDB.Sync
{
    /// <summary>
    /// SyncEngine: the future OakTree cloud replicator, currently a squirrel with WiFi dreams.
    /// </summary>
    public class SyncEngine
    {
        private readonly string _remoteEndpoint;

        public SyncEngine(string remoteEndpoint)
        {
            _remoteEndpoint = remoteEndpoint;
        }

        public Task PushChangesAsync()
        {
            Console.WriteLine($">> [Acorn.Sync] Pushing local nutments to {_remoteEndpoint}...");
            // TODO: Actually send data over the wire, probably with HTTP, gRPC, or carrier pigeon
            return Task.CompletedTask;
        }

        public Task PullChangesAsync()
        {
            Console.WriteLine($">> [Acorn.Sync] Pulling latest nutment stash from {_remoteEndpoint}...");
            // TODO: Receive remote changes and reconcile
            return Task.CompletedTask;
        }

        public Task SyncBidirectionalAsync()
        {
            Console.WriteLine($">> [Acorn.Sync] Full nut shake: two-way sync initiated.");
            return Task.WhenAll(PushChangesAsync(), PullChangesAsync());
        }
    }
}
