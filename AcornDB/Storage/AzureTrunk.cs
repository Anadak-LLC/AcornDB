
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Newtonsoft.Json;

using AcornDB.Storage;

namespace AcornDB
{
    public class AzureTrunk<T> : ITrunk<T>
    {
        private readonly BlobContainerClient _container;

        public AzureTrunk(string connectionString, string containerName = null!)
        {
            containerName ??= typeof(T).Name.ToLower() + "-acorns";
            var serviceClient = new BlobServiceClient(connectionString);
            _container = serviceClient.GetBlobContainerClient(containerName);
            _container.CreateIfNotExists();
        }

        public void Save(string id, Nut<T> shell)
        {
            var json = JsonConvert.SerializeObject(shell, Formatting.Indented);
            var blob = _container.GetBlobClient(id + ".json");
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            blob.Upload(stream, overwrite: true);
        }

        public Nut<T>? Load(string id)
        {
            var blob = _container.GetBlobClient(id + ".json");
            if (blob.Exists())
            {
                var download = blob.DownloadContent();
                var content = download.Value.Content.ToString();
                return JsonConvert.DeserializeObject<Nut<T>>(content);
            }
            return null;
        }

        public void Delete(string id)
        {
            var blob = _container.GetBlobClient(id + ".json");
            blob.DeleteIfExists();
        }

        public IEnumerable<Nut<T>> LoadAll()
        {
            var list = new List<Nut<T>>();
            foreach (var blob in _container.GetBlobs())
            {
                var blobClient = _container.GetBlobClient(blob.Name);
                var download = blobClient.DownloadContent();
                var content = download.Value.Content.ToString();
                var shell = JsonConvert.DeserializeObject<Nut<T>>(content);
                if (shell != null) list.Add(shell);
            }
            return list;
        }

        // Optional features - not supported by AzureTrunk
        public IReadOnlyList<Nut<T>> GetHistory(string id)
        {
            throw new NotSupportedException("AzureTrunk does not support history. Consider using blob versioning or DocumentStoreTrunk.");
        }

        public IEnumerable<Nut<T>> ExportChanges()
        {
            return LoadAll();
        }

        public void ImportChanges(IEnumerable<Nut<T>> incoming)
        {
            foreach (var shell in incoming)
            {
                Save(shell.Id, shell);
            }
        }

        // Async variants for performance-critical scenarios
        public async Task SaveAsync(string id, Nut<T> shell)
        {
            var json = JsonConvert.SerializeObject(shell, Formatting.Indented);
            var blob = _container.GetBlobClient(id + ".json");
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await blob.UploadAsync(stream, overwrite: true);
        }

        public async Task<Nut<T>?> LoadAsync(string id)
        {
            var blob = _container.GetBlobClient(id + ".json");
            if (await blob.ExistsAsync())
            {
                var download = await blob.DownloadContentAsync();
                var content = download.Value.Content.ToString();
                return JsonConvert.DeserializeObject<Nut<T>>(content);
            }
            return null;
        }

        public async Task DeleteAsync(string id)
        {
            var blob = _container.GetBlobClient(id + ".json");
            await blob.DeleteIfExistsAsync();
        }
    }
}
