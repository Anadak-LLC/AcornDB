## ☁️ AcornDB.Persistence.Cloud - Cloud Storage Guide

Store your AcornDB trees in AWS S3, Azure Blob Storage, MinIO, DigitalOcean Spaces, or any S3-compatible storage!

---

## 📦 Installation

```bash
# Install both packages
dotnet add package AcornDB
dotnet add package AcornDB.Persistence.Cloud
```

---

## 🚀 Quick Start

### AWS S3

```csharp
using AcornDB;
using AcornDB.Persistence.Cloud;

public class User
{
    public string Id { get; set; }
    public string Name { get; set; }
}

// Option 1: Explicit credentials
var tree = new Acorn<User>()
    .WithS3Storage(
        accessKey: "YOUR_ACCESS_KEY",
        secretKey: "YOUR_SECRET_KEY",
        bucketName: "my-acorndb-bucket",
        region: "us-east-1"
    )
    .Sprout();

// Option 2: Default credential chain (IAM roles, environment vars)
var tree = new Acorn<User>()
    .WithS3Storage(
        bucketName: "my-acorndb-bucket",
        region: "us-east-1"
    )
    .Sprout();

// Use it like any other tree!
tree.Stash(new User { Id = "alice", Name = "Alice" });
var alice = tree.Crack("alice");
```

### Azure Blob Storage

```csharp
using AcornDB;
using AcornDB.Persistence.Cloud;

// Option 1: Connection string
var tree = new Acorn<User>()
    .WithAzureBlobStorage(
        connectionString: "DefaultEndpointsProtocol=https;AccountName=...",
        containerName: "acorndb"
    )
    .Sprout();

// Option 2: SAS URI
var sasUri = new Uri("https://myaccount.blob.core.windows.net/mycontainer?sv=...");
var tree = new Acorn<User>()
    .WithAzureBlobStorage(sasUri)
    .Sprout();

tree.Stash(new User { Id = "bob", Name = "Bob" });
```

### MinIO / S3-Compatible Storage

```csharp
using AcornDB;
using AcornDB.Persistence.Cloud;

// Works with MinIO, DigitalOcean Spaces, Backblaze B2, etc.
var tree = new Acorn<User>()
    .WithS3CompatibleStorage(
        accessKey: "minioadmin",
        secretKey: "minioadmin",
        bucketName: "acorndb",
        serviceUrl: "http://localhost:9000"
    )
    .Sprout();
```

---

## 🎯 Features

### ✅ Multi-Cloud Support
- **AWS S3** - Native S3 support with all AWS regions
- **Azure Blob Storage** - Full Azure integration
- **MinIO** - Self-hosted S3-compatible storage
- **DigitalOcean Spaces** - Easy S3-compatible cloud storage
- **Backblaze B2** - Cost-effective S3-compatible storage
- **Any S3-compatible service** - Fully compatible

### ✅ Flexible Authentication
- **AWS**: IAM roles, access keys, environment variables, credential chains
- **Azure**: Connection strings, SAS URIs, managed identities
- **S3-Compatible**: Custom endpoints with access/secret keys

### ✅ Prefix Support (Folder-like Organization)
```csharp
var tree = new Acorn<User>()
    .WithS3Storage(
        bucketName: "my-bucket",
        region: "us-east-1",
        prefix: "production/users" // Organize by environment/type
    )
    .Sprout();

// Files stored as: production/users/alice.json
```

### ✅ Async Support
CloudTrunk supports both sync and async operations:
```csharp
var cloudTrunk = new CloudTrunk<User>(s3Provider);

// Sync (automatic async bridge)
cloudTrunk.Save("id", nut);

// Async (more efficient for cloud storage)
await cloudTrunk.SaveAsync("id", nut);
await cloudTrunk.LoadAllAsync();
```

---

## 📚 Detailed Examples

### 1. **Production AWS S3 Setup**

```csharp
using AcornDB;
using AcornDB.Persistence.Cloud;

// Use IAM roles (recommended for production)
var tree = new Acorn<User>()
    .WithS3Storage(
        bucketName: "prod-acorndb-users",
        region: "us-west-2",
        prefix: "production"
    )
    .Sprout();

// Stash users
tree.Stash(new User { Id = "alice", Name = "Alice", Email = "alice@example.com" });
tree.Stash(new User { Id = "bob", Name = "Bob", Email = "bob@example.com" });

// Crack (load) users
var alice = tree.Crack("alice");
Console.WriteLine($"Loaded: {alice.Name}");

// Get all users
var allUsers = tree.GetAll();
Console.WriteLine($"Total users: {allUsers.Count()}");
```

**S3 Structure:**
```
s3://prod-acorndb-users/
└── production/
    ├── alice.json
    └── bob.json
```

### 2. **Azure Blob Storage with Connection String**

```csharp
// Get connection string from Azure Portal or environment variable
var connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
    ?? "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=...";

var tree = new Acorn<Document>()
    .WithAzureBlobStorage(
        connectionString: connectionString,
        containerName: "documents",
        prefix: "acorndb"
    )
    .Sprout();

tree.Stash(new Document { Id = "doc1", Title = "Report", Content = "..." });
```

**Azure Structure:**
```
Container: documents
└── acorndb/
    └── doc1.json
```

### 3. **MinIO for Local Development**

```csharp
// Run MinIO locally: docker run -p 9000:9000 minio/minio server /data
var tree = new Acorn<User>()
    .WithS3CompatibleStorage(
        accessKey: "minioadmin",
        secretKey: "minioadmin",
        bucketName: "dev-acorndb",
        serviceUrl: "http://localhost:9000"
    )
    .Sprout();

// Works exactly like S3!
tree.Stash(new User { Id = "test", Name = "Test User" });
```

### 4. **DigitalOcean Spaces**

```csharp
var tree = new Acorn<Image>()
    .WithS3CompatibleStorage(
        accessKey: "YOUR_DO_SPACES_KEY",
        secretKey: "YOUR_DO_SPACES_SECRET",
        bucketName: "my-app-images",
        serviceUrl: "https://nyc3.digitaloceanspaces.com"
    )
    .Sprout();
```

### 5. **Multi-Environment Setup**

```csharp
public class AcornFactory
{
    public static Tree<T> CreateTree<T>(string environment)
    {
        var prefix = environment.ToLower(); // "dev", "staging", "prod"

        return new Acorn<T>()
            .WithS3Storage(
                bucketName: "acorndb-shared",
                region: "us-east-1",
                prefix: prefix
            )
            .Sprout();
    }
}

// Usage
var devTree = AcornFactory.CreateTree<User>("dev");
var prodTree = AcornFactory.CreateTree<User>("prod");
```

**S3 Structure:**
```
s3://acorndb-shared/
├── dev/
│   └── user.json
├── staging/
│   └── user.json
└── prod/
    └── user.json
```

---

## 🛠️ Advanced Usage

### Custom Cloud Storage Provider

Implement `ICloudStorageProvider` for custom backends:

```csharp
public class CustomCloudProvider : ICloudStorageProvider
{
    public async Task UploadAsync(string key, string content) { /* ... */ }
    public async Task<string?> DownloadAsync(string key) { /* ... */ }
    public async Task DeleteAsync(string key) { /* ... */ }
    public async Task<bool> ExistsAsync(string key) { /* ... */ }
    public async Task<List<string>> ListAsync(string? prefix = null) { /* ... */ }
    public CloudStorageInfo GetInfo() { /* ... */ }
}

// Use it
var tree = new Acorn<User>()
    .WithCloudStorage(new CustomCloudProvider())
    .Sprout();
```

### Direct CloudTrunk Usage

For more control, create CloudTrunk directly:

```csharp
var s3Provider = new AwsS3Provider("accessKey", "secretKey", "bucket", "region");
var cloudTrunk = new CloudTrunk<User>(s3Provider, prefix: "users");

var tree = new Acorn<User>()
    .WithTrunk(cloudTrunk)
    .Sprout();
```

### Async Operations

Use async methods for better performance with cloud storage:

```csharp
var s3Provider = new AwsS3Provider("bucket", "region");
var cloudTrunk = new CloudTrunk<User>(s3Provider);

// Async save
await cloudTrunk.SaveAsync("id", nut);

// Async load
var nut = await cloudTrunk.LoadAsync("id");

// Async load all
var allNuts = await cloudTrunk.LoadAllAsync();

// Async exists check
var exists = await cloudTrunk.ExistsAsync("id");
```

---

## 🔒 Security Best Practices

### 1. **Use IAM Roles (AWS)**

```csharp
// DON'T hardcode credentials
// var tree = new Acorn<User>().WithS3Storage("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI...", ...).Sprout();

// DO use IAM roles (automatically detected)
var tree = new Acorn<User>()
    .WithS3Storage("my-bucket", "us-east-1")
    .Sprout();
```

### 2. **Use Environment Variables**

```csharp
var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");

var tree = new Acorn<User>()
    .WithS3Storage(accessKey!, secretKey!, "bucket", "region")
    .Sprout();
```

### 3. **Use Managed Identities (Azure)**

```csharp
// Azure automatically handles authentication for Azure services
var connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");

var tree = new Acorn<User>()
    .WithAzureBlobStorage(connectionString!, "container")
    .Sprout();
```

### 4. **Least Privilege Access**

**AWS S3 Policy Example:**
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject",
        "s3:DeleteObject",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::my-acorndb-bucket/*",
        "arn:aws:s3:::my-acorndb-bucket"
      ]
    }
  ]
}
```

---

## 💰 Cost Optimization

### AWS S3 Pricing Tips

1. **Use Standard-IA for infrequent access:**
   - Configure lifecycle policies in S3
   - Automatically transition old nuts to cheaper storage

2. **Use S3 Intelligent-Tiering:**
   - Automatically moves objects between access tiers
   - Optimizes costs based on access patterns

3. **Enable S3 Compression:**
   ```csharp
   var tree = new Acorn<User>()
       .WithS3Storage("bucket", "region")
       .WithCompression() // Reduces storage and transfer costs!
       .Sprout();
   ```

### Azure Blob Storage Tips

1. **Use Cool or Archive tiers:**
   - Set access tier based on how often data is accessed
   - Archive tier is 10x cheaper than Hot tier

2. **Enable compression:**
   ```csharp
   var tree = new Acorn<User>()
       .WithAzureBlobStorage(connectionString, "container")
       .WithCompression()
       .Sprout();
   ```

---

## ⚡ Performance Considerations

### Pros:
- ✅ **Scalable** - Virtually unlimited storage
- ✅ **Durable** - 11 nines durability (AWS S3)
- ✅ **Geo-redundant** - Automatic replication
- ✅ **Pay-per-use** - No upfront costs
- ✅ **Async support** - Non-blocking operations

### Cons:
- ⚠️ **Network latency** - ~50-200ms per operation (vs <1ms local)
- ⚠️ **API costs** - Per-request charges
- ⚠️ **Requires internet** - Not offline-first

### When to Use:
- ✅ Distributed applications
- ✅ Serverless/cloud-native apps
- ✅ Multi-region deployments
- ✅ Backup and archival
- ✅ Shared storage across services

### When NOT to Use:
- ❌ High-frequency writes (> 1000/sec)
- ❌ Sub-millisecond latency requirements
- ❌ Offline-first applications
- ❌ Cost-sensitive with high read volume

---

## 🎉 Summary

**AcornDB.Persistence.Cloud** gives you enterprise-grade cloud storage in just a few lines:

```csharp
// AWS S3
var tree = new Acorn<User>()
    .WithS3Storage("bucket", "region")
    .Sprout();

// Azure Blob
var tree = new Acorn<User>()
    .WithAzureBlobStorage(connectionString, "container")
    .Sprout();

// MinIO / S3-Compatible
var tree = new Acorn<User>()
    .WithS3CompatibleStorage(accessKey, secretKey, "bucket", "http://localhost:9000")
    .Sprout();
```

**That's it!** Your AcornDB data is now in the cloud! ☁️🌰

---

## 📖 Additional Resources

- [AWS S3 Documentation](https://docs.aws.amazon.com/s3/)
- [Azure Blob Storage Documentation](https://docs.microsoft.com/en-us/azure/storage/blobs/)
- [MinIO Documentation](https://min.io/docs/minio/linux/index.html)
- [DigitalOcean Spaces Documentation](https://docs.digitalocean.com/products/spaces/)

---

**Happy cloud squirreling!** 🐿️☁️
