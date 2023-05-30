namespace NewsletterBuilder;

public class Organisation
{
  public static Organisation Instance { get; set; }

  public string Name { get; init; }
  public string NewsletterUrl { get; init; }
  public string NewsletterEditorUrl { get; init; }
  public string Address { get; init; }
  public string Footer { get; init; }
  public string BannedWords { get; init; }
  public string PhotoConsentUrl { get; init; }
  public string FromEmail { get; init; }
  public string QualityAssuranceEmail { get; init; }
  public string ReminderReplyTo { get; init; }
  public int DefaultDeadlineDaysBeforePublish { get; init; }
  public IList<Reminder> Reminders { get; init; }
  public string TwitterHandle { get; init; }
}

public class Reminder {
  public string Domain { get; set; }
  public int DaysBeforeDeadline { get; set; }
  public string Subject { get; set; }
  public string Message { get; set; }
}