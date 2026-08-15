using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using NewsletterBuilder.Entities;
using System.Collections.Concurrent;
using System.Numerics;
using System.Security.Cryptography;

namespace NewsletterBuilder;

public class DuplicationDetectionService(TableService tableService)
{
  private const int maximumHashDistance = 6;
  private static readonly ConcurrentDictionary<string, Lazy<Task<ConcurrentDictionary<string, StoredImageHash>>>> imageHashCaches = new();

  public async Task<bool> CheckAndStoreImageAsync(string imageName, byte[] image, params string[] excludedImageNames)
  {
    var hashes = await GetImageHashCacheAsync();
    var exclusions = excludedImageNames.Append(imageName).ToHashSet();
    var existingHashes = hashes.Values.Where(o => !exclusions.Contains(o.RowKey)).ToList();
    var sha256 = Convert.ToHexString(SHA256.HashData(image));
    var exactMatch = existingHashes.FirstOrDefault(o => o.Sha256 == sha256);

    long perceptualHash;
    if (exactMatch is not null)
    {
      perceptualHash = exactMatch.PerceptualHash;
    }
    else
    {
      using var stream = new MemoryStream(image);
      perceptualHash = unchecked((long)new PerceptualHash().Hash(stream));
    }

    var isDuplicate = exactMatch is not null || existingHashes.Any(o =>
      BitOperations.PopCount(unchecked((ulong)o.PerceptualHash) ^ unchecked((ulong)perceptualHash)) <= maximumHashDistance);
    var imageHash = new StoredImageHash { RowKey = imageName, Sha256 = sha256, PerceptualHash = perceptualHash };
    await tableService.UpsertImageHashAsync(imageHash);
    await UpdateImageHashCacheAsync(cache => cache[imageName] = imageHash);
    return isDuplicate;
  }

  public async Task DeleteImageHashAsync(string imageName)
  {
    await tableService.DeleteImageHashAsync(imageName);
    await UpdateImageHashCacheAsync(cache => cache.TryRemove(imageName, out _));
  }

  private async Task<ConcurrentDictionary<string, StoredImageHash>> GetImageHashCacheAsync()
  {
    var cache = imageHashCaches.GetOrAdd(tableService.StoragePartitionKey, _ => new(async () =>
    {
      var hashes = await tableService.ListImageHashesAsync();
      return new ConcurrentDictionary<string, StoredImageHash>(hashes.ToDictionary(o => o.RowKey));
    }));

    try
    {
      return await cache.Value;
    }
    catch
    {
      RemoveImageHashCache(tableService.StoragePartitionKey, cache);
      throw;
    }
  }

  private async Task UpdateImageHashCacheAsync(Action<ConcurrentDictionary<string, StoredImageHash>> update)
  {
    if (!imageHashCaches.TryGetValue(tableService.StoragePartitionKey, out var cache) || !cache.IsValueCreated) return;

    ConcurrentDictionary<string, StoredImageHash> hashes;
    try
    {
      hashes = await cache.Value;
    }
    catch
    {
      RemoveImageHashCache(tableService.StoragePartitionKey, cache);
      return;
    }
    update(hashes);
  }

  private static void RemoveImageHashCache(string key, Lazy<Task<ConcurrentDictionary<string, StoredImageHash>>> cache) =>
    ((ICollection<KeyValuePair<string, Lazy<Task<ConcurrentDictionary<string, StoredImageHash>>>>>)imageHashCaches).Remove(new(key, cache));
}
