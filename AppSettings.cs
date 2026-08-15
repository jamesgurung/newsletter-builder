using System.Text.Json.Serialization;

namespace NewsletterBuilder;

public class AppSettings
{
  public string OrganisationName { get; init; }
  public string EmailDomain { get; init; }
  public string NewsletterEditorUrl { get; init; }
  public string NewsletterUrl { get; init; }
  public string Address { get; init; }
  public string Footer { get; init; }
  public string BannedWords { get; init; }
  public string PhotoConsentUrl { get; init; }
  public string UnlistedArticles { get; init; } = string.Empty;
  public string FromEmail { get; init; }
  public string QualityAssuranceEmail { get; init; }
  public string SocialMediaEmail { get; init; }
  public string ReminderReplyTo { get; init; }
  public int DefaultDeadlineDaysBeforePublish { get; init; }
  public IList<Reminder> Reminders { get; init; } = [];
  public string TwitterHandle { get; init; }
}

public class Reminder
{
  public int DaysBeforeDeadline { get; set; }
  public string Subject { get; set; }
  public string Message { get; set; }

  public string Time
  {
    get;
    set
    {
      field = value;
      if (!TimeOnly.TryParse(value, out var timeOnly))
        throw new ArgumentException("Invalid time format. Use HH:mm (24-hour format).", nameof(value));
      TimeOnly = timeOnly;
    }
  }

  [JsonIgnore]
  public TimeOnly TimeOnly { get; set; }
  [JsonIgnore]
  public DateTime NextRunUtc { get; set; }

  public void CalculateNextRun(TimeZoneInfo timeZone)
  {
    ArgumentNullException.ThrowIfNull(timeZone, nameof(timeZone));
    var localNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, timeZone);
    var todayAtTaskTime = localNow.Date + TimeOnly.ToTimeSpan();
    var nextLocal = localNow < todayAtTaskTime ? todayAtTaskTime : todayAtTaskTime.AddDays(1);
    if (nextLocal.DayOfWeek == DayOfWeek.Saturday) nextLocal = nextLocal.AddDays(2);
    else if (nextLocal.DayOfWeek == DayOfWeek.Sunday) nextLocal = nextLocal.AddDays(1);
    if (timeZone.IsInvalidTime(nextLocal)) nextLocal = nextLocal.AddHours(1);
    NextRunUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocal, timeZone);
  }
}
