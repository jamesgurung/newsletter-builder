using Azure;
using Azure.Data.Tables;
using System.Globalization;

namespace NewsletterBuilder.Entities;

public class Newsletter : ITableEntity
{
  public string PartitionKey { get; set; } // Domain
  public string RowKey { get; set; }       // Date (ISO 8601)
  public DateTimeOffset? Timestamp { get; set; }
  public ETag ETag { get; set; }

  public string ArticleOrder { get; set; }
  public string Deadline { get; set; }
  public string CoverPhoto { get; set; }
  public DateTime? LastPublished { get; set; }
  public bool IsSent { get; set; }
  public string Description { get; set; }

  public bool IsTimeToSend() {
    var newsletterDate = DateOnly.ParseExact(RowKey[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
    var now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Europe/London");
    var today = DateOnly.FromDateTime(now);
    return newsletterDate < today || (newsletterDate == today && now.Hour > 15) || (newsletterDate == today && now.Hour == 15 && now.Minute >= 30);
  }
}