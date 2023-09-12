using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using NewsletterBuilder.Entities;
using System.Text;
using System.Text.Json;

namespace NewsletterBuilder;

public class BlobService(string domain)
{
  public static void Configure(string connectionString, string accountKey)
  {
    client = new BlobServiceClient(connectionString);
    storageAccountKey = accountKey;
    Uri = client.Uri.ToString();
  }

  public static string Uri { get; private set; }

  private static BlobServiceClient client;
  private static string storageAccountKey;
  private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

  public async Task UploadImageAsync(string articleKey, string imageName, Stream stream) {
    ArgumentNullException.ThrowIfNull(imageName);
    var container = client.GetBlobContainerClient("photos");
    var blobName = $"{domain}/{articleKey}/{imageName}";
    var blob = container.GetBlockBlobClient(blobName);
    var contentType = imageName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png";
    var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } };
    await blob.UploadAsync(stream, options);
  }

  public async Task DeleteImageAsync(string articleKey, string imageName)
  {
    var container = client.GetBlobContainerClient("photos");
    var blobName = $"{domain}/{articleKey}/{imageName}";
    await container.DeleteBlobIfExistsAsync(blobName, DeleteSnapshotsOption.IncludeSnapshots);
  }

  public async Task DeleteArticleImagesAsync(string articleKey)
  {
    var container = client.GetBlobContainerClient("photos");
    await foreach (var item in container.GetBlobsByHierarchyAsync(prefix: $"{domain}/{articleKey}/"))
    {
      if (!item.IsBlob) continue;
      await container.DeleteBlobIfExistsAsync(item.Blob.Name, DeleteSnapshotsOption.IncludeSnapshots);
    }
  }

  public async Task MoveImagesAsync(string oldArticleKey, string newArticleKey)
  {
    var container = client.GetBlobContainerClient("photos");
    await foreach (var item in container.GetBlobsByHierarchyAsync(prefix: $"{domain}/{oldArticleKey}/")) {
      if (!item.IsBlob) continue;
      var source = container.GetBlockBlobClient(item.Blob.Name);
      var imageName = item.Blob.Name.Split('/').Last();
      var dest = container.GetBlockBlobClient($"{domain}/{newArticleKey}/{imageName}");
      var resp = await dest.SyncCopyFromUriAsync(new Uri($"{source.Uri}?{GetSasQueryString()}"));
      if (resp.Value.CopyStatus == CopyStatus.Success)
      {
        await source.DeleteAsync();
      }
    }
  }

  public string GetSasQueryString()
  {
    var builder = new BlobSasBuilder()
    {
      BlobContainerName = "photos",
      Resource = "c",
      StartsOn = DateTime.UtcNow.AddMinutes(-2),
      ExpiresOn = DateTime.UtcNow.AddDays(1),
      Protocol = SasProtocol.Https
    };
    builder.SetPermissions(BlobSasPermissions.Read);
    return builder.ToSasQueryParameters(new StorageSharedKeyCredential(client.AccountName, storageAccountKey)).ToString();
  }

  public async Task<bool> ImageExistsAsync(string articleKey, string imageName)
  {
    var container = client.GetBlobContainerClient("photos");
    var blobName = $"{domain}/{articleKey}/{imageName}";
    return await container.GetBlobClient(blobName).ExistsAsync();
  }

  public async Task PublishImagesAsync(string articleKey, IList<string> imageOrder)
  {
    ArgumentNullException.ThrowIfNull(articleKey);
    ArgumentNullException.ThrowIfNull(imageOrder);
    if (imageOrder.Count == 0) return;
    var sourceContainer = client.GetBlobContainerClient("photos");
    var destContainer = client.GetBlobContainerClient("$web");
    var articleKeyParts = articleKey.Split('_');
    await foreach (var item in sourceContainer.GetBlobsByHierarchyAsync(prefix: $"{domain}/{articleKey}/"))
    {
      if (!item.IsBlob) continue;
      if (!item.Blob.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
        !item.Blob.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
      var source = sourceContainer.GetBlockBlobClient(item.Blob.Name);
      var sourceName = item.Blob.Name.Split('/').Last();
      var index = imageOrder.IndexOf(sourceName);
      if (index < 0) continue;
      var destName = imageOrder.Count == 1 ? articleKeyParts[1] : $"{articleKeyParts[1]}{index + 1}";
      var extension = sourceName.Split('.').Last();
      var dest = destContainer.GetBlockBlobClient($"{articleKeyParts[0]}/{destName}.{extension}");
      await dest.SyncCopyFromUriAsync(new Uri($"{source.Uri}?{GetSasQueryString()}"));
    }
  }

  public async Task AppendToNewsletterListAsync(NewsletterListItem item) {
    var container = client.GetBlobContainerClient("$web");
    var blob = container.GetBlockBlobClient("list.json");
    var response = await blob.DownloadContentAsync();
    var list = JsonSerializer.Deserialize<List<NewsletterListItem>>(response.Value.Content);
    var existing = list.FirstOrDefault(x => x.Date == item.Date);
    if (existing is not null) list.Remove(existing);
    list.Insert(0, item);
    var json = JsonSerializer.Serialize(list.OrderByDescending(o => o.Date), jsonOptions);
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" } };
    await blob.UploadAsync(stream, options);
  }

  public async Task PublishNewsletterAsync(string date, string webHtml, string emailHtml, string emailPlainText) {
    var container = client.GetBlobContainerClient("$web");
    var files = new[] { ("index.html", webHtml, "text/html"), ("email.html", emailHtml, "text/html"), ("email.txt", emailPlainText, "text/plain") };
    foreach (var (fileName, contents, contentType) in files)
    {
      var blob = container.GetBlockBlobClient($"{date}/{fileName}");
      using var stream = new MemoryStream(Encoding.UTF8.GetBytes(contents));
      var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType + "; charset=UTF-8" } };
      await blob.UploadAsync(stream, options);
    }
  }

  public async Task<string> ReadTextAsync(string date, string fileName) {
    var container = client.GetBlobContainerClient("$web");
    var blob = container.GetBlockBlobClient($"{date}/{fileName}");
    var response = await blob.DownloadContentAsync();
    return response.Value.Content.ToString();
  }
}