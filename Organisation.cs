namespace NewsletterBuilder;

public class Organisation
{
  public static Dictionary<string, Organisation> ByDomain { get; set; }
  public static string NewsletterEditorUrl { get; set; }

  public string Name { get; init; }
  public string Domain { get; init; }
  public string NewsletterUrl { get; init; }
  public string Address { get; init; }
  public string Footer { get; init; }
  public string BannedWords { get; init; }
  public string PhotoConsentUrl { get; init; }
  public IList<string> UnlistedArticles { get; init; }
  public string FromEmail { get; init; }
  public string QualityAssuranceEmail { get; init; }
  public string SocialMediaEmail { get; init; }
  public string ReminderReplyTo { get; init; }
  public int DefaultDeadlineDaysBeforePublish { get; init; }
  public IList<Reminder> Reminders { get; init; }
  public string TwitterHandle { get; init; }
  public string AzureStorageStaticWebsiteAccountName { get; init; }
  public string AzureStorageStaticWebsiteAccountKey { get; init; }
}

public class Reminder {
  public int DaysBeforeDeadline { get; set; }
  public string Subject { get; set; }
  public string Message { get; set; }
}