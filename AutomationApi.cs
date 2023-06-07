using Azure.Data.Tables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace NewsletterBuilder;

public static class AutomationApi
{
  private static string _automationApiKey;
  private static readonly TimeZoneInfo _britishZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

  public static void Configure(string automationApiKey) => _automationApiKey = automationApiKey;

  public static void MapAutomationApiPaths(this WebApplication app)
  {
    var group = app.MapGroup("/api/automate");

    group.MapPost("/emailreminders/{domain}/{n:int}", [AllowAnonymous]
    async (string domain, int n, [FromHeader(Name = "X-Api-Key")] string auth, TableServiceClient client, IWebHostEnvironment env) =>
    {
      if (auth != _automationApiKey) return Results.Unauthorized();
      if (string.IsNullOrEmpty(domain)) return Results.BadRequest("Domain required.");
      var reminders = Organisation.Instance.Reminders.Where(o => o.Domain == domain).ToList();
      if (n >= reminders.Count) return Results.BadRequest("No reminder found at this index.");
      var reminder = reminders[n];
      
      var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, _britishZone);
      if (!env.IsDevelopment() && now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return Results.Conflict("Cannot send emails at the weekend.");

      var newsletterDeadline = now.AddDays(reminder.DaysBeforeDeadline).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

      var service = new TableService(client, domain);
      var newsletter = (await service.ListNewslettersAsync()).SingleOrDefault(o => o.Deadline == newsletterDeadline);
      if (newsletter is null) return Results.NoContent();
      var articles = (await service.ListArticlesAsync(newsletter.RowKey)).Where(o => !o.IsSubmitted && o.ShortName != "intro").ToList();
      if (articles.Count == 0) return Results.NoContent();

      var users = (await service.ListUsersAsync()).ToDictionary(o => o.RowKey);
      var mailer = new Mailer();

      foreach (var article in articles) {
        var contributors = article.ContributorList.Select(o => users.TryGetValue(o, out var u) ? u : null).Where(o => o is not null && !o.IsEditor).ToList();
        if (contributors.Count == 0) continue;
        var contributorEmails = string.Join(',', contributors.Select(o => $"{o.RowKey}@{o.PartitionKey}"));
        var contributorNames = contributors.Count == 1
          ? contributors[0].FirstName
          : $"{string.Join(", ", contributors.SkipLast(1).Select(o => o.FirstName))} and {contributors.Last().FirstName}";

        var body = $"<html><body style=\"font-family: Arial; font-size: 11pt\">Hi {contributorNames}<br /><br />" +
          reminder.Message + "<br /><br />" +
          (string.IsNullOrEmpty(article.Content) ? string.Empty : "It looks like you have made a start on this article, but it has not yet been submitted.<br /><br />") +
          $"Article: <b>{article.ShortName}</b><br />" +
          $"Deadline: <b>{now.AddDays(reminder.DaysBeforeDeadline):dddd d MMMM}</b><br /><br />" +
          $"<a href=\"{Organisation.Instance.NewsletterEditorUrl}/{article.RowKey.Replace('_', '/')}\" style=\"text-decoration: none; color: #1379CE\">" +
          "<b>Click here to submit your article and photos</b></a><br /><br />" +
          $"Many thanks<br /><br />{Organisation.Instance.Name}<br /></body></html>";

        mailer.Enqueue(contributorEmails, reminder.Subject, false, body);
      }

      await mailer.SendAsync();
      
      return Results.Ok();
    });
  }
}