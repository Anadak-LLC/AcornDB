// Placeholder for Branch.cs


using System.Text;
using System.Text.Json;

namespace AcornDB.Sync
{
    public partial class Branch
    {
        public string RemoteUrl { get; }
        public SyncMode SyncMode { get; set; } = SyncMode.Bidirectional;
        public ConflictDirection ConflictDirection { get; set; } = ConflictDirection.UseJudge;

        private readonly HttpClient _httpClient;
        private readonly HashSet<string> _pushedNuts = new(); // Track pushed nuts to avoid re-pushing

        public Branch(string remoteUrl, SyncMode syncMode = SyncMode.Bidirectional)
        {
            RemoteUrl = remoteUrl.TrimEnd('/');
            SyncMode = syncMode;
            _httpClient = new HttpClient();
        }

        public virtual void TryPush<T>(string id, Nut<T> shell)
        {
            // Respect sync mode - only push if push is enabled
            if (SyncMode == SyncMode.PullOnly || SyncMode == SyncMode.Disabled)
                return;

            // Check if we've already pushed this nut to avoid duplicates
            var nutKey = $"{id}:{shell.Timestamp.Ticks}";
            if (_pushedNuts.Contains(nutKey))
                return;

            _pushedNuts.Add(nutKey);
            _ = PushAsync(id, shell);
        }

        public virtual void TryDelete<T>(string id)
        {
            // Respect sync mode - only delete if push is enabled
            if (SyncMode == SyncMode.PullOnly || SyncMode == SyncMode.Disabled)
                return;

            _ = DeleteAsync<T>(id);
        }

        private async Task DeleteAsync<T>(string id)
        {
            try
            {
                var treeName = typeof(T).Name.ToLowerInvariant();
                var endpoint = $"{RemoteUrl}/bark/{treeName}/toss/{id}";

                var response = await _httpClient.DeleteAsync(endpoint);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"> 🌐 Failed to delete nut {id} from {RemoteUrl}: {response.StatusCode}");
                }
                else
                {
                    Console.WriteLine($"> 🌐 Nut {id} deleted from {RemoteUrl}.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"> 🌐 Branch delete failed: {ex.Message}");
            }
        }

        private async Task PushAsync<T>(string id, Nut<T> shell)
        {
            try
            {
                var json = JsonSerializer.Serialize(shell);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var treeName = typeof(T).Name.ToLowerInvariant(); // naive default mapping
                var endpoint = $"{RemoteUrl}/bark/{treeName}/stash";

                var response = await _httpClient.PostAsync(endpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"> 🌐 Failed to push nut {id} to {RemoteUrl}: {response.StatusCode}");
                }
                else
                {
                    Console.WriteLine($"> 🌐 Nut {id} synced to {RemoteUrl}.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"> 🌐 Branch push failed: {ex.Message}");
            }
        }

        public virtual async Task ShakeAsync<T>(Tree<T> targetTree)
        {
            // Respect sync mode - only pull if pull is enabled
            if (SyncMode == SyncMode.PushOnly || SyncMode == SyncMode.Disabled)
                return;

            try
            {
                var treeName = typeof(T).Name.ToLowerInvariant();
                var endpoint = $"{RemoteUrl}/bark/{treeName}/export";

                var response = await _httpClient.GetAsync(endpoint);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"> 🌐 Failed to shake branch from {RemoteUrl}: {response.StatusCode}");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var nuts = JsonSerializer.Deserialize<List<Nut<T>>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (nuts == null) return;

                foreach (var nut in nuts)
                {
                    targetTree.Squabble(nut.Id, nut);
                }

                Console.WriteLine($"> 🍂 Shake complete: {nuts.Count} nuts received from {RemoteUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"> 🌐 Branch shake failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear the tracking of pushed nuts to allow re-pushing
        /// Useful when you want to force a full re-sync
        /// </summary>
        public void ClearPushHistory()
        {
            _pushedNuts.Clear();
        }

        /// <summary>
        /// Get statistics about this branch
        /// </summary>
        public BranchStats GetStats()
        {
            return new BranchStats
            {
                RemoteUrl = RemoteUrl,
                SyncMode = SyncMode,
                ConflictDirection = ConflictDirection,
                TotalPushed = _pushedNuts.Count
            };
        }
    }

    /// <summary>
    /// Statistics about a branch's sync activity
    /// </summary>
    public class BranchStats
    {
        public string RemoteUrl { get; set; } = "";
        public SyncMode SyncMode { get; set; }
        public ConflictDirection ConflictDirection { get; set; }
        public int TotalPushed { get; set; }
    }
}
