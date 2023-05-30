using Azure;
using Azure.Data.Tables;
using System.Globalization;
using System.Runtime.Serialization;

namespace NewsletterBuilder.Entities;

public class CalendarEvent : ITableEntity
{
  public string PartitionKey { get; set; } // Domain
  public string RowKey { get; set; }       // Start_End_Title
  public DateTimeOffset? Timestamp { get; set; }
  public ETag ETag { get; set; }

  public string Owner { get; set; }
  public bool IsApproved { get; set; }

  [IgnoreDataMember]
  public string DisplayDate
  {
    get
    {
      var parts = RowKey.Split('_');
      var start = DateOnly.ParseExact(parts[0], "yyyy-MM-dd");
      var end = parts[1].Length == 0 ? start : DateOnly.ParseExact(parts[1], "yyyy-MM-dd");
      if (start == end) return start.ToString("d MMM", CultureInfo.InvariantCulture);
      if (start.Year == end.Year && start.Month == end.Month) return $"{start.Day}-{end.Day} {start:MMM}";
      return $"{start:d MMM}-{end:d MMM}";
    }
  }

  [IgnoreDataMember]
  public string DisplayTitle => RowKey.Split('_')[2];
  [IgnoreDataMember]
  public string StartDate => RowKey.Split('_')[0];
  [IgnoreDataMember]
  public string EndDate => RowKey.Split('_')[1];
}