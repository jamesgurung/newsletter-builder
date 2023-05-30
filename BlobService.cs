using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using NewsletterBuilder.Entities;
using System.Text;
using System.Text.Json;

namespace NewsletterBuilder;

public class BlobService
{
  private readonly BlobServiceClient _client;
  private readonly string _domain;

  private static string _storageAccountKey;
  public static void Configure(string storageAccountKey) => _storageAccountKey = storageAccountKey;

  public BlobService(BlobServiceClient client, string domain)
  {
    _client = client;
    _domain = domain;
  }

  public async Task UploadImageAsync(string articleKey, string imageName, Stream stream) {
    var container = _client.GetBlobContainerClient("photos");
    var blobName = $"{_domain}/{articleKey}/{imageName}";
    await container.UploadBlobAsync(blobName, stream);
  }

  public async Task DeleteImageAsync(string articleKey, string imageName)
  {
    var container = _client.GetBlobContainerClient("photos");
    var blobName = $"{_domain}/{articleKey}/{imageName}";
    await container.DeleteBlobIfExistsAsync(blobName, DeleteSnapshotsOption.IncludeSnapshots);
  }

  public async Task DeleteArticleImagesAsync(string articleKey)
  {
    var container = _client.GetBlobContainerClient("photos");
    await foreach (var item in container.GetBlobsByHierarchyAsync(prefix: $"{_domain}/{articleKey}/"))
    {
      if (!item.IsBlob) continue;
      await container.DeleteBlobIfExistsAsync(item.Blob.Name, DeleteSnapshotsOption.IncludeSnapshots);
    }
  }

  public async Task MoveImagesAsync(string oldArticleKey, string newArticleKey)
  {
    var container = _client.GetBlobContainerClient("photos");
    await foreach (var item in container.GetBlobsByHierarchyAsync(prefix: $"{_domain}/{oldArticleKey}/")) {
      if (!item.IsBlob) continue;
      var source = container.GetBlockBlobClient(item.Blob.Name);
      var imageName = item.Blob.Name.Split('/').Last();
      var dest = container.GetBlockBlobClient($"{_domain}/{newArticleKey}/{imageName}");
      var resp = await dest.SyncCopyFromUriAsync(source.Uri);
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
    return builder.ToSasQueryParameters(new StorageSharedKeyCredential(_client.AccountName, _storageAccountKey)).ToString();
  }

  public async Task<bool> ImageExistsAsync(string articleKey, string imageName)
  {
    var container = _client.GetBlobContainerClient("photos");
    var blobName = $"{_domain}/{articleKey}/{imageName}";
    return await container.GetBlobClient(blobName).ExistsAsync();
  }

  public async Task PublishImagesAsync(string articleKey, IList<string> imageOrder)
  {
    ArgumentNullException.ThrowIfNull(articleKey);
    ArgumentNullException.ThrowIfNull(imageOrder);
    if (imageOrder.Count == 0) return;
    var sourceContainer = _client.GetBlobContainerClient("photos");
    var destContainer = _client.GetBlobContainerClient("$web");
    var articleKeyParts = articleKey.Split('_');
    await foreach (var item in sourceContainer.GetBlobsByHierarchyAsync(prefix: $"{_domain}/{articleKey}/"))
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
    var container = _client.GetBlobContainerClient("$web");
    var blob = container.GetBlockBlobClient("list.json");
    var response = await blob.DownloadContentAsync();
    var list = JsonSerializer.Deserialize<List<NewsletterListItem>>(response.Value.Content);
    var existing = list.FirstOrDefault(x => x.Date == item.Date);
    if (existing is not null) list.Remove(existing);
    list.Insert(0, item);
    var json = JsonSerializer.Serialize(list.OrderByDescending(o => o.Date), new JsonSerializerOptions { WriteIndented = true });
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    await blob.UploadAsync(stream);
  }

  public async Task PublishNewsletterAsync(string date, string webHtml, string emailHtml, string emailPlainText) {
    var container = _client.GetBlobContainerClient("$web");
    var files = new[] { ("index.html", webHtml), ("email.html", emailHtml), ("email.txt", emailPlainText) };
    foreach (var (fileName, contents) in files) {
      var blob = container.GetBlockBlobClient($"{date}/{fileName}");
      using var stream = new MemoryStream(Encoding.UTF8.GetBytes(contents));
      await blob.UploadAsync(stream);
    }
  }

  public async Task<string> ReadTextAsync(string date, string fileName) {
    var container = _client.GetBlobContainerClient("$web");
    var blob = container.GetBlockBlobClient($"{date}/{fileName}");
    var response = await blob.DownloadContentAsync();
    return response.Value.Content.ToString();
  }
}