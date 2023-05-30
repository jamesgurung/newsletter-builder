using Azure;
using Azure.Data.Tables;

namespace NewsletterBuilder.Entities;

public class Recipient : ITableEntity
{
  public string PartitionKey { get; set; } // Domain
  public string RowKey { get; set; }       // Email address
  public DateTimeOffset? Timestamp { get; set; }
  public ETag ETag { get; set; }
}