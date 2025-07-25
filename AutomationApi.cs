using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NewsletterBuilder;

public static class AutomationApi
{
  private static string _automationApiKey;

  public static void Configure(string automationApiKey)
  {
    _automationApiKey = automationApiKey;
  }

  public static void MapAutomationApiPaths(this WebApplication app)
  {
    var group = app.MapGroup("/api/automate");

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