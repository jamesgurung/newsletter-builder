using Azure;
using Azure.Data.Tables;
using System.Runtime.Serialization;

namespace NewsletterBuilder.Entities;

public class Article : ITableEntity
{
  public string PartitionKey { get; set; } // Domain
  public string RowKey { get; set; }       // Date_ShortName
  public DateTimeOffset? Timestamp { get; set; }
  public ETag ETag { get; set; }

  public string Title { get; set; }
  public string Content { get; set; }
  public string Contributors { get; set; }
  public string Owner { get; set; }
  public bool IsSubmitted { get; set; }
  public bool IsApproved { get; set; }

  [IgnoreDataMember]
  public IList<string> ContributorList => [.. Contributors.Split(',')];
  [IgnoreDataMember]
  public string Date => RowKey.Split('_')[0];
  [IgnoreDataMember]
  public string ShortName => RowKey.Split('_')[1];
}