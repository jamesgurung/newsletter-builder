using Azure;
using Azure.Data.Tables;

namespace NewsletterBuilder.Entities;

public class StoredImageHash : ITableEntity
{
  public string PartitionKey { get; set; } // Domain
  public string RowKey { get; set; }       // Image name
  public DateTimeOffset? Timestamp { get; set; }
  public ETag ETag { get; set; }

  public string Sha256 { get; set; }
  public long PerceptualHash { get; set; }
}
