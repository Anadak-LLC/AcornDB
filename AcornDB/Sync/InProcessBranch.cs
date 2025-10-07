using System;
using System.Threading.Tasks;

namespace AcornDB.Sync
{
    /// <summary>
    /// InProcessBranch: Syncs between two trees in the same process without HTTP
    /// </summary>
    public class InProcessBranch<T> : Branch
    {
        private readonly Tree<T> _targetTree;

        public InProcessBranch(Tree<T> targetTree) : base("in-process")
        {
            _targetTree = targetTree ?? throw new ArgumentNullException(nameof(targetTree));
        }

        public override void TryPush<TItem>(string id, Nut<TItem> nut)
        {
            if (typeof(TItem) != typeof(T))
            {
                Console.WriteLine($"> ⚠️ InProcessBranch: Type mismatch - expected {typeof(T).Name}, got {typeof(TItem).Name}");
                return;
            }

            try
            {
                // Cast nut to correct type and squabble on target tree
                var typedNut = nut as Nut<T>;
                if (typedNut != null)
                {
                    _targetTree.Squabble(id, typedNut);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"> ⚠️ InProcessBranch push failed: {ex.Message}");
            }
        }

        public override async Task ShakeAsync<TItem>(Tree<TItem> sourceTree)
        {
            if (typeof(TItem) != typeof(T))
            {
                Console.WriteLine($"> ⚠️ InProcessBranch: Type mismatch during shake");
                return;
            }

            try
            {
                var changes = _targetTree.ExportChanges();
                foreach (var nut in changes)
                {
                    var typedSourceTree = sourceTree as Tree<T>;
                    typedSourceTree?.Squabble(nut.Id, nut);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"> ⚠️ InProcessBranch shake failed: {ex.Message}");
            }

            await Task.CompletedTask;
        }
    }
}
