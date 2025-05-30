﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace NewsletterBuilder;

public static class AutomationApi
{
  private static string _automationApiKey;
  private static readonly TimeZoneInfo _britishZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

  public static void Configure(string automationApiKey)
  {
    _automationApiKey = automationApiKey;
  }

  public static void MapAutomationApiPaths(this WebApplication app)
  {
    var group = app.MapGroup("/api/automate");

    group.MapPost("/{domain}/emailreminders/{n:int}", [AllowAnonymous]
    async (string domain, int n, [FromHeader(Name = "X-Api-Key")] string auth, IWebHostEnvironment env) =>
    {
      if (string.IsNullOrEmpty(_automationApiKey)) return Results.Conflict("An automation API key is not configured.");
      if (auth != _automationApiKey) return Results.Unauthorized();
      if (string.IsNullOrEmpty(domain)) return Results.BadRequest("Domain required.");
      var thisOrganisation = Organisation.ByDomain[domain];
      if (n >= thisOrganisation.Reminders.Count) return Results.BadRequest("No reminder found at this index.");
      var reminder = thisOrganisation.Reminders[n];

      var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, _britishZone);
      if (!env.IsDevelopment() && now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return Results.Conflict("Cannot send emails at the weekend.");

      var newsletterDeadline = now.AddDays(reminder.DaysBeforeDeadline).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

      var service = new TableService(domain);
      var newsletter = (await service.ListNewslettersAsync()).SingleOrDefault(o => o.Deadline == newsletterDeadline);
      if (newsletter is null) return Results.NoContent();
      var articles = (await service.ListArticlesAsync(newsletter.RowKey)).Where(o => !o.IsSubmitted && o.Title != "Intro").ToList();
      if (articles.Count == 0) return Results.NoContent();

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
          $"<a href=\"{Organisation.NewsletterEditorUrl}/{article.RowKey.Replace('_', '/')}\" style=\"text-decoration: none; color: #1379CE\">" +
          "<b>Click here to submit your article and photos</b></a><br /><br />" +
          $"Many thanks<br /><br />{thisOrganisation.Name}<br /></body></html>";

        mailer.Enqueue(contributorEmails, reminder.Subject, thisOrganisation.FromEmail, thisOrganisation.ReminderReplyTo, false, body);
      }

      await mailer.SendAsync();

      return Results.Ok();
    });

    group.MapPut("/{domain}/recipients", [AllowAnonymous] async (string domain, HttpContext context, [FromHeader(Name = "X-Api-Key")] string auth) =>
    {
      if (string.IsNullOrEmpty(_automationApiKey)) return Results.Conflict("An automation API key is not configured.");
      if (auth != _automationApiKey) return Results.Unauthorized();
      if (string.IsNullOrEmpty(domain)) return Results.BadRequest("Domain required.");
      if (!Organisation.ByDomain.ContainsKey(domain)) return Results.NotFound("Domain not recognised.");

      if (!context.Request.ContentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Content type must be text/plain.");
      using var reader = new StreamReader(context.Request.Body);
      var data = await reader.ReadToEndAsync();
      if (string.IsNullOrWhiteSpace(data)) return Results.BadRequest("Data cannot be empty.");
      var suppressed = await Mailer.GetSuppressedRecipientsAsync();
      var recipients = data.Trim().Split('\n').Select(o => o.Trim().ToLowerInvariant()).Distinct()
        .Where(o => o.Contains('@', StringComparison.OrdinalIgnoreCase) && !suppressed.Contains(o, StringComparer.OrdinalIgnoreCase)).ToList();

      var service = new TableService(domain);
      await service.ReplaceRecipientsAsync(recipients);

      return Results.Ok();
    });

    group.MapPut("/{domain}/users", [AllowAnonymous] async (string domain, HttpContext context, [FromHeader(Name = "X-Api-Key")] string auth) =>
    {
      if (string.IsNullOrEmpty(_automationApiKey)) return Results.Conflict("An automation API key is not configured.");
      if (auth != _automationApiKey) return Results.Unauthorized();
      if (string.IsNullOrEmpty(domain)) return Results.BadRequest("Domain required.");
      if (!Organisation.ByDomain.ContainsKey(domain)) return Results.NotFound("Domain not recognised.");

      if (!context.Request.ContentType.StartsWith("text/csv", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Content type must be text/csv.");
      using var reader = new StreamReader(context.Request.Body);
      var data = await reader.ReadToEndAsync();
      if (string.IsNullOrWhiteSpace(data)) return Results.BadRequest("Data cannot be empty.");
      var csvUsers = data.Trim().Split('\n').Select(o => o.Trim()).ToList();

      var service = new TableService(domain);
      await service.ReplaceUsersAsync(csvUsers);

      return Results.Ok();
    });
  }
}