
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Newtonsoft.Json;

namespace AcornDB
{
    public class AzureTrunk<T>
    {
        private readonly BlobContainerClient _container;

        public AzureTrunk(string connectionString, string containerName = null)
        {
            containerName ??= typeof(T).Name.ToLower() + "-acorns";
            var serviceClient = new BlobServiceClient(connectionString);
            _container = serviceClient.GetBlobContainerClient(containerName);
            _container.CreateIfNotExists();
        }

        public async Task StashAsync(string id, T item)
        {
            var shell = new NutShell<T>
            {
                Id = id,
                Payload = item,
                Timestamp = DateTime.UtcNow
            };

            var json = JsonConvert.SerializeObject(shell, Formatting.Indented);
            var blob = _container.GetBlobClient(id + ".json");
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await blob.UploadAsync(stream, overwrite: true);
        }

        public async Task<T?> CrackAsync(string id)
        {
            var blob = _container.GetBlobClient(id + ".json");
            if (await blob.ExistsAsync())
            {
                var download = await blob.DownloadContentAsync();
                var content = download.Value.Content.ToString();
                var shell = JsonConvert.DeserializeObject<NutShell<T>>(content);
                return shell != null ? shell.Payload : default!;
            }

            return default!;
        }

        public async Task TossAsync(string id)
        {
            var blob = _container.GetBlobClient(id + ".json");
            await blob.DeleteIfExistsAsync();
        }
    }
}
