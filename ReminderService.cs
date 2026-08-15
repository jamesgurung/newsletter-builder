using System.Globalization;
using System.Runtime.InteropServices;

namespace NewsletterBuilder;

public class ReminderService(ILogger<ReminderService> logger, AppSettings appSettings) : BackgroundService
{
  private static readonly TimeZoneInfo _ukTimeZone = TimeZoneInfo.FindSystemTimeZoneById(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "GMT Standard Time" : "Europe/London");
  private List<Reminder> _reminders;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _reminders = appSettings.Reminders.ToList();
    if (_reminders.Count == 0)
    {
      logger.LogInformation("No reminders configured.");
      return;
    }

    while (!stoppingToken.IsCancellationRequested)
    {
      CalculateNextRuns();
      var utcNext = _reminders.Min(o => o.NextRunUtc);
      var delay = utcNext - DateTime.UtcNow;
      if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
      await Task.Delay(delay, stoppingToken);
      var remindersToRun = _reminders.Where(o => o.NextRunUtc <= DateTime.UtcNow.AddSeconds(1)).ToList();
      foreach (var reminder in remindersToRun)
      {
        try
        {
          if (await RunReminderAsync(reminder))
          {
            logger.LogInformation("Sent reminders for -{Days}d/{Time}", reminder.DaysBeforeDeadline, reminder.Time);
          }
          else
          {
            logger.LogInformation("No reminders to send for -{Days}d/{Time}", reminder.DaysBeforeDeadline, reminder.Time);
          }
        }
        catch (Exception e)
        {
          logger.LogError("Reminders failed for -{Days}d/{Time}: {Error}", reminder.DaysBeforeDeadline, reminder.Time, e.Message);
        }
      }
    }
  }

  private void CalculateNextRuns()
  {
    foreach (var reminder in _reminders)
    {
      reminder.CalculateNextRun(_ukTimeZone);
    }
  }

  private async Task<bool> RunReminderAsync(Reminder reminder)
  {
    var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, _ukTimeZone);
    if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) throw new InvalidOperationException("Cannot send emails at the weekend.");

    var newsletterDeadline = now.AddDays(reminder.DaysBeforeDeadline).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    var service = new TableService();
    var newsletter = (await service.ListNewslettersAsync()).SingleOrDefault(o => o.Deadline == newsletterDeadline);
    if (newsletter is null) return false;
    var articles = (await service.ListArticlesAsync(newsletter.RowKey)).Where(o => !o.IsSubmitted && o.Title != "Intro").ToList();
    if (articles.Count == 0) return false;

    var users = (await service.ListUsersAsync()).ToDictionary(o => o.RowKey);
    var mailer = new Mailer();

    foreach (var article in articles)
    {
      var contributors = article.ContributorList.Select(o => users.TryGetValue(o, out var u) ? u : null).Where(o => o is not null && !o.IsEditor).ToList();
      if (contributors.Count == 0) continue;
      var contributorEmails = string.Join(',', contributors.Select(o => $"{o.RowKey}@{o.PartitionKey}"));
      var contributorNames = contributors.Count == 1
        ? contributors[0].FirstName
        : $"{string.Join(", ", contributors.SkipLast(1).Select(o => o.FirstName))} and {contributors.Last().FirstName}";

      var body = $"<html><body style=\"font-family: Arial; font-size: 11pt\">Hi {contributorNames}<br /><br />" +
        reminder.Message + "<br /><br />" +
        (string.IsNullOrEmpty(article.Content) ? string.Empty : "It looks like you have made a start on this article, but it has not yet been submitted.<br /><br />") +
        $"Article: <b>{article.Title}</b><br />" +
        $"Deadline: <b>{now.AddDays(reminder.DaysBeforeDeadline):dddd d MMMM}</b><br /><br />" +
        $"<a href=\"{appSettings.NewsletterEditorUrl}/{article.RowKey.Replace('_', '/')}\" style=\"text-decoration: none; color: #1379CE\">" +
        "<b>Click here to submit your article and photos</b></a><br /><br />" +
        $"Many thanks<br /><br />{appSettings.OrganisationName}<br /></body></html>";

      mailer.Enqueue(contributorEmails, reminder.Subject, appSettings.FromEmail, appSettings.ReminderReplyTo, false, body);
    }

    await mailer.SendAsync();
    return true;
  }
}
