using AcornDB.Sync;

namespace AcornDB
{
    public partial class Tangle<T>
    {
        private readonly Tree<T> _local;
        private readonly Sync.Branch _remoteBranch;
        private readonly string _id;

        public Tangle(Tree<T> local, Branch remoteBranch, string id)
        {
            _local = local;
            _remoteBranch = remoteBranch;
            _id = id;
            _local.RegisterTangle(this);
        }

        public void PushUpdate(string key, T item)
        {
            var shell = new Nut<T>
            {
                Id = key,
                Payload = item,
                Timestamp = DateTime.UtcNow
            };
            _remoteBranch.TryPush(key, shell);
        }

        public void PushDelete(string key)
        {
            Console.WriteLine($"> 🔄 Tangle '{_id}': Push delete for '{key}'");
            _remoteBranch.TryDelete<T>(key);
        }

        public void PushAll(Tree<T> tree)
        {
            Console.WriteLine($"> 🍃 Tangle '{_id}' pushing all to remote...");
            foreach (var shell in tree.ExportChanges())
            {
                _remoteBranch.TryPush(shell.Id, shell);
            }
        }
    }
}
