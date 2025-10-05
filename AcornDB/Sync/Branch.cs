// Placeholder for Branch.cs


using System.Text;
using System.Text.Json;

namespace AcornDB.Sync
{
    public partial class Branch
    {
        public string RemoteUrl { get; }

        private readonly HttpClient _httpClient;

        public Branch(string remoteUrl)
        {
            RemoteUrl = remoteUrl.TrimEnd('/');
            _httpClient = new HttpClient();
        }

        public void TryPush<T>(string id, NutShell<T> shell)
        {
            _ = PushAsync(id, shell);
        }

        private async Task PushAsync<T>(string id, NutShell<T> shell)
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

        public async Task ShakeAsync<T>(Tree<T> targetTree)
        {
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
                var nuts = JsonSerializer.Deserialize<List<NutShell<T>>>(json, new JsonSerializerOptions
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
    }
}
