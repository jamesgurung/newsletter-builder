using Azure;
using Azure.Data.Tables;

namespace NewsletterBuilder.Entities;

public class User : ITableEntity
{
  public string PartitionKey { get; set; } // Domain
  public string RowKey { get; set; }       // Username
  public DateTimeOffset? Timestamp { get; set; }
  public ETag ETag { get; set; }

  public bool IsEditor { get; set; }
  public string FirstName { get; set; }
  public string DisplayName { get; set; }
}