using Azure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewsletterBuilder.Entities;
using NewsletterBuilder.Pages;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NewsletterBuilder;

public static class Api
{
  public static void MapApiPaths(this WebApplication app)
  {
    app.MapGet("/api/newsletters/{key}/images", [Authorize(Roles = Roles.Editor)] async (string key, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var tableService = new TableService(domain);
      var articles = await tableService.ListArticlesAsync(key);
      if (articles.Count == 0) return Results.NotFound("Newsletter not found.");

      var blobService = new BlobService(domain);
      var sas = blobService.GetSasQueryString();

      var articleContents = articles.Where(o => o.Content is not null)
        .Select(o => new { Name = o.ShortName, Content = JsonSerializer.Deserialize<ArticleContentData>(o.Content) }).ToList();

      foreach (var article in articleContents)
      {
        RenderPageModel.AddImageRenderNames(article.Name, article.Content.Sections);
      }

      var blobBaseUrl = $"{BlobService.Uri}photos/{domain}";

      return Results.Ok(articleContents.SelectMany(a => a.Content.Sections.Select(s => new { Key = a.Name, Section = s })
      .Where(o => o.Section.Image is not null).Select(o => new ArticleImageData
      {
        FileName = o.Section.ImageRenderName,
        Url = $"{blobBaseUrl}/{key}_{o.Key}/{o.Section.Image}?{sas}"
      }).ToList()));
    });

    var group = app.MapGroup("/api").ValidateAntiforgery();

    group.MapPost("/users", [Authorize(Roles = Roles.Editor)] async (User user, HttpContext context) =>
    {
      user.RowKey = user.RowKey?.Trim().ToLowerInvariant();
      user.DisplayName = user.DisplayName?.Trim();
      user.FirstName = user.FirstName?.Trim();
      if (string.IsNullOrEmpty(user.RowKey)) return Results.BadRequest("Invalid username.");
      if (string.IsNullOrEmpty(user.DisplayName)) return Results.BadRequest("Invalid display name.");
      if (string.IsNullOrEmpty(user.FirstName)) return Results.BadRequest("Invalid first name.");
      var domain = context.User.GetDomain();
      user.PartitionKey = domain;
      user.IsEditor = false;
      var service = new TableService(domain);
      try
      {
        await service.CreateUserAsync(user);
      }
      catch (RequestFailedException ex) when (ex.Status == 409)
      {
        return Results.Conflict("User already exists.");
      }
      return Results.Ok();
    });

    group.MapDelete("/users/{username}", [Authorize(Roles = Roles.Editor)] async (string username, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var service = new TableService(domain);
      await service.DeleteUserAsync(username);
      return Results.Ok();
    });

    group.MapPost("/events", async (CalendarEvent ev, HttpContext context) =>
    {
      if (ev.RowKey is null) return Results.BadRequest("Invalid event key.");
      ev.RowKey = ev.RowKey?.Trim();
      var parts = ev.RowKey.Split('_');
      if (parts.Length != 3) return Results.BadRequest("Invalid event key.");
      if (!DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", out _)) return Results.BadRequest("Invalid start date.");
      if (!DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", out _)) return Results.BadRequest("Invalid end date.");
      if (string.IsNullOrWhiteSpace(parts[2])) return Results.BadRequest("Invalid title.");
      var domain = context.User.GetDomain();
      ev.PartitionKey = domain;
      ev.Owner = context.User.GetUsername();
      ev.IsApproved = context.User.IsInRole(Roles.Editor);
      var service = new TableService(domain);
      try
      {
        await service.CreateEventAsync(ev);
      }
      catch (RequestFailedException ex) when (ex.Status == 409)
      {
        return Results.Conflict("Event already exists.");
      }
      return Results.Ok(ev);
    });

    group.MapPost("/events/{key}/approve", [Authorize(Roles = Roles.Editor)] async (string key, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var service = new TableService(domain);
      await service.ApproveEventAsync(key);
      return Results.Ok();
    });

    group.MapDelete("/events/{key}", async (string key, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var service = new TableService(domain);
      if (context.User.GetRole() != Roles.Editor)
      {
        var ev = await service.GetEventAsync(key);
        if (ev.Owner != context.User.GetUsername())
        {
          return Results.Forbid();
        }
      }
      await service.DeleteEventAsync(key);
      return Results.Ok();
    });

    group.MapPost("/newsletters", [Authorize(Roles = Roles.Editor)] async (Newsletter newsletter, HttpContext context) =>
    {
      if (!DateOnly.TryParseExact(newsletter.RowKey, "yyyy-MM-dd", out _)) return Results.BadRequest("Invalid newsletter date.");
      if (!DateOnly.TryParseExact(newsletter.Deadline, "yyyy-MM-dd", out _)) return Results.BadRequest("Invalid deadline.");
      var domain = context.User.GetDomain();
      newsletter.PartitionKey = domain;
      var service = new TableService(domain);
      try
      {
        await service.CreateNewsletterAsync(newsletter);
      }
      catch (RequestFailedException ex) when (ex.Status == 409)
      {
        return Results.Conflict("Newsletter already exists.");
      }
      var username = context.User.GetUsername();
      await service.CreateArticleAsync(new Article { PartitionKey = domain, RowKey = $"{newsletter.RowKey}_intro", Title = "Intro", Owner = username, Contributors = username });
      return Results.Ok();
    });

    group.MapDelete("/newsletters/{key}", [Authorize(Roles = Roles.Editor)] async (string key, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var service = new TableService(domain);
      var articles = await service.ListArticlesAsync(key);
      if (articles.Count > 1) return Results.Conflict("Cannot delete newsletter which contains articles.");
      await service.DeleteArticleAsync($"{key}_intro");
      await service.DeleteNewsletterAsync(key);
      return Results.Ok();
    });

    group.MapPost("/articles", async (Article article, HttpContext context) =>
    {
      article.RowKey = article.RowKey?.Trim().ToLowerInvariant();
      if (string.IsNullOrEmpty(article.RowKey)) return Results.BadRequest("Invalid article key.");
      if (!DateOnly.TryParseExact(article.Date, "yyyy-MM-dd", out _)) return Results.BadRequest("Invalid article date.");
      var domain = context.User.GetDomain();
      article.PartitionKey = domain;
      article.Owner = context.User.GetUsername();
      article.IsSubmitted = false;
      var service = new TableService(domain);
      if (context.User.IsInRole(Roles.Editor))
      {
        if (string.IsNullOrWhiteSpace(article.Contributors)) return Results.BadRequest("Contributors required.");
        article.Contributors = string.Join(',', article.Contributors.Split(',').Select(o => o.Trim()));
        foreach (var user in article.ContributorList)
        {
          var foundUser = await service.GetUserAsync(user);
          if (foundUser is null) return Results.BadRequest($"User not found: {user}.");
        }
      }
      else
      {
        article.Contributors = article.Owner;
      }
      try
      {
        await service.CreateArticleAsync(article);
      }
      catch (RequestFailedException ex) when (ex.Status == 409)
      {
        return Results.Conflict("Article already exists.");
      }
      var newsletter = await service.GetNewsletterAsync(article.Date);
      newsletter.ArticleOrder = string.IsNullOrEmpty(newsletter.ArticleOrder) ? article.ShortName : $"{newsletter.ArticleOrder},{article.ShortName}";
      newsletter.LastPublished = null;
      await service.UpdateNewsletterAsync(newsletter);

      return Results.Ok();
    });

    group.MapDelete("/articles/{key}", async (string key, [FromQuery] string order, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var tableService = new TableService(domain);
      if (context.User.GetRole() != Roles.Editor)
      {
        var article = await tableService.GetArticleAsync(key);
        if (article.Owner != context.User.GetUsername())
        {
          return Results.Forbid();
        }
      }
      var newsletterKey = key[..10];
      if (!await ValidateOrderAsync(tableService, newsletterKey, order, remove: key[11..])) return Results.BadRequest("Invalid article order.");
      await tableService.DeleteArticleAsync(key);

      var newsletter = await tableService.GetNewsletterAsync(newsletterKey);
      newsletter.ArticleOrder = order;
      newsletter.LastPublished = null;
      await tableService.UpdateNewsletterAsync(newsletter);

      var blobService = new BlobService(domain);
      await blobService.DeleteArticleImagesAsync(key);
      return Results.Ok();
    });

    group.MapPut("/newsletters/{key}/order", [Authorize(Roles = Roles.Editor)] async (string key, ArticleOrderData orderData, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var tableService = new TableService(domain);
      if (!await ValidateOrderAsync(tableService, key, orderData.Order)) return Results.BadRequest("Invalid article order.");

      var newsletter = await tableService.GetNewsletterAsync(key);
      newsletter.ArticleOrder = orderData.Order;
      newsletter.LastPublished = null;
      await tableService.UpdateNewsletterAsync(newsletter);

      return Results.Ok();
    });

    group.MapPut("/newsletters/{key}/coverphoto", [Authorize(Roles = Roles.Editor)] async (string key, CoverPhotoData coverPhotoData, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var tableService = new TableService(domain);
      var newsletter = await tableService.GetNewsletterAsync(key);
      if (newsletter is null) return Results.NotFound("Newsletter not found.");
      var blobService = new BlobService(domain);
      if (!await blobService.ImageExistsAsync(coverPhotoData.ArticleKey, coverPhotoData.ImageName)) return Results.BadRequest("Invalid image.");
      newsletter.CoverPhoto = coverPhotoData.ImageName;
      newsletter.LastPublished = null;
      await tableService.UpdateNewsletterAsync(newsletter);
      return Results.Ok();
    });

    group.MapPost("/articles/{key}/move", [Authorize(Roles = Roles.Editor)] async (string key, ArticleMoveData moveData, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var tableService = new TableService(domain);
      var article = await tableService.GetArticleAsync(key);
      if (article is null) return Results.NotFound("Article not found.");
      var source = await tableService.GetNewsletterAsync(key[..10]);
      if (source is null) return Results.BadRequest("Source does not exist.");
      var dest = await tableService.GetNewsletterAsync(moveData.Destination);
      if (dest is null) return Results.BadRequest("Destination does not exist.");
      var articleName = key[11..];
      article.RowKey = moveData.Destination + "_" + articleName;
      if (!await ValidateOrderAsync(tableService, source.RowKey, moveData.SourceOrder, remove: articleName)) return Results.BadRequest("Invalid source order.");
      source.ArticleOrder = moveData.SourceOrder;
      source.LastPublished = null;
      if (!await ValidateOrderAsync(tableService, dest.RowKey, moveData.DestinationOrder, add: articleName)) return Results.BadRequest("Invalid destination order.");
      dest.ArticleOrder = moveData.DestinationOrder;
      dest.LastPublished = null;
      await tableService.MoveArticleAsync(key, article, source, dest);

      var blobService = new BlobService(domain);
      await blobService.MoveImagesAsync(key, article.RowKey);
      return Results.Ok();
    });

    group.MapPut("/articles/{key}/content", async (string key, ArticleContentData contentData, HttpContext context) =>
    {
      if (contentData?.Sections is null) return Results.BadRequest("No content submitted.");
      var domain = context.User.GetDomain();
      var service = new TableService(domain);
      var article = await service.GetArticleAsync(key);
      if (article is null) return Results.NotFound("Article not found.");
      if (article.ETag.ToString() != contentData.ETag)
      {
        return Results.Conflict("Article has been modified by another user.");
      }
      if (!context.User.IsInRole(Roles.Editor) && (!article.ContributorList.Contains(context.User.GetUsername()) || article.IsSubmitted)) return Results.Forbid();
      var isBlank = string.IsNullOrEmpty(contentData.Headline) && contentData.Sections.All(o => string.IsNullOrEmpty(o.Text) && string.IsNullOrEmpty(o.Image));
      article.Content = isBlank ? null : JsonSerializer.Serialize(contentData);
      await service.UpdateArticleAsync(article);
      return Results.Ok(article.ETag);
    });

    group.MapPost("/articles/{key}/image", async (string key, HttpContext context) =>
    {
      if (context.Request.ContentLength == 0) return Results.BadRequest("No image submitted.");
      var contentType = context.Request.ContentType;
      if (contentType is not "image/jpeg" and not "image/png") return Results.BadRequest("Only JPG and PNG images are supported.");
      var domain = context.User.GetDomain();
      var tableService = new TableService(domain);
      var article = await tableService.GetArticleAsync(key);
      if (article is null) return Results.NotFound("Article not found.");
      if (!context.User.IsInRole(Roles.Editor) && (!article.ContributorList.Contains(context.User.GetUsername()) || article.IsSubmitted)) return Results.Forbid();
      var blobService = new BlobService(domain);
      var imageName = Guid.NewGuid().ToString() + (contentType == "image/jpeg" ? ".jpg" : ".png");
      await blobService.UploadImageAsync(key, imageName, context.Request.Body);
      return Results.Ok(imageName);
    });

    group.MapPost("/articles/{key}/image/{imageName}/describe", async (string key, string imageName, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var tableService = new TableService(domain);
      var article = await tableService.GetArticleAsync(key);
      if (article is null) return Results.NotFound("Article not found.");
      if (!context.User.IsInRole(Roles.Editor) && (!article.ContributorList.Contains(context.User.GetUsername()) || article.IsSubmitted)) return Results.Forbid();
      var blobService = new BlobService(domain);
      var sas = blobService.GetSasQueryString(key, imageName);
      var imageUrl = $"{BlobService.Uri}photos/{domain}/{key}/{imageName}?{sas}";
      var description = await AIService.DescribePhotoAsync(new Uri(imageUrl), article.Title, key);
      return Results.Ok(description);
    });

    group.MapDelete("/articles/{key}/image/{imageName}", async (string key, string imageName, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var tableService = new TableService(domain);
      var article = await tableService.GetArticleAsync(key);
      if (article is null) return Results.NotFound("Article not found.");
      if (!context.User.IsInRole(Roles.Editor) && (!article.ContributorList.Contains(context.User.GetUsername()) || article.IsSubmitted)) return Results.Forbid();
      var blobService = new BlobService(domain);
      await blobService.DeleteImageAsync(key, imageName);
      return Results.Ok();
    });

    group.MapPost("/articles/{key}/submit", async (string key, HttpContext context, ETagData etagData) =>
    {
      var domain = context.User.GetDomain();
      var service = new TableService(domain);
      var article = await service.GetArticleAsync(key);
      if (article is null) return Results.NotFound("Article not found.");
      if (!article.ContributorList.Contains(context.User.GetUsername()) && !context.User.IsInRole(Roles.Editor)) return Results.Forbid();
      if (article.ETag.ToString() != etagData.ETag) return Results.Conflict("Article has been modified by another user.");
      article.IsSubmitted = true;
      await service.UpdateArticleAsync(article);
      return Results.Ok(article.ETag);
    });

    group.MapPost("/articles/{key}/unsubmit", [Authorize(Roles = Roles.Editor)] async (string key, HttpContext context, ETagData etagData) =>
    {
      var domain = context.User.GetDomain();
      var service = new TableService(domain);
      var article = await service.GetArticleAsync(key);
      if (article is null) return Results.NotFound("Article not found.");
      if (article.ETag.ToString() != etagData.ETag) return Results.Conflict("Article has been modified by another user.");
      article.IsSubmitted = false;
      await service.UpdateArticleAsync(article);
      return Results.Ok(article.ETag);
    });

    group.MapPost("/articles/{key}/approve", [Authorize(Roles = Roles.Editor)] async (string key, HttpContext context, ETagData etagData) =>
    {
      var domain = context.User.GetDomain();
      var tableService = new TableService(domain);
      var article = await tableService.GetArticleAsync(key);
      if (article is null) return Results.NotFound("Article not found.");
      if (article.Content is null) return Results.BadRequest("Article content is missing.");
      if (article.ETag.ToString() != etagData.ETag) return Results.Conflict("Article has been modified by another user.");
      article.IsSubmitted = true;
      article.IsApproved = true;
      await tableService.UpdateArticleAsync(article);
      var imageOrder = JsonSerializer.Deserialize<ArticleContentData>(article.Content).Sections.Where(o => !string.IsNullOrEmpty(o.Image)).Select(o => o.Image).ToList();
      var blobService = new BlobService(domain);
      await blobService.PublishImagesAsync(key, imageOrder);
      return Results.Ok(article.ETag);
    });

    group.MapPost("/articles/{key}/unapprove", [Authorize(Roles = Roles.Editor)] async (string key, HttpContext context, ETagData etagData) =>
    {
      var domain = context.User.GetDomain();
      var service = new TableService(domain);
      var article = await service.GetArticleAsync(key);
      if (article is null) return Results.NotFound("Article not found.");
      if (article.ETag.ToString() != etagData.ETag) return Results.Conflict("Article has been modified by another user.");
      article.IsApproved = false;
      await service.UpdateArticleAsync(article);
      return Results.Ok(article.ETag);
    });

    group.MapPost("/aiwrite", async (WriteArticleRequest writeRequest) =>
    {
      var response = await AIService.WriteArticleAsync(writeRequest.Headline, writeRequest.Content, writeRequest.Paragraphs, writeRequest.Identifier);
      return Results.Ok(response);
    });

    group.MapPost("/aifeedback", async (ArticleFeedbackRequest feedbackRequest, HttpResponse response) =>
    {
      response.ContentType = "text/plain; charset=utf-8";
      await foreach (var token in AIService.RequestArticleFeedbackAsync(feedbackRequest.Headline, feedbackRequest.Content, feedbackRequest.Identifier))
      {
        await response.WriteAsync(token);
        await response.Body.FlushAsync();
      }
    });

    group.MapPost("/recipients", [Authorize(Roles = Roles.Editor)] async (RecipientData recipientsData, HttpContext context) =>
    {
      var domain = context.User.GetDomain();
      var service = new TableService(domain);
      var suppressed = await Mailer.GetSuppressedRecipientsAsync();
      var recipients = recipientsData.Recipients.Where(o => !suppressed.Contains(o)).ToList();
      await service.ReplaceRecipientsAsync(recipients);
      return Results.Ok();
    });

    group.MapPost("/newsletters/{key}/publish", [Authorize(Roles = Roles.Editor)] async (string key, PublishData publishData, HttpContext context) =>
    {
      if (string.IsNullOrEmpty(key)) return Results.BadRequest("Missing newsletter key.");
      if (string.IsNullOrEmpty(publishData?.Html)) return Results.BadRequest("Missing HTML content.");
      if (string.IsNullOrEmpty(publishData?.Description)) return Results.BadRequest("Missing newsletter description.");
      var domain = context.User.GetDomain();
      var tableService = new TableService(domain);
      var newsletter = await tableService.GetNewsletterAsync(key);
      if (newsletter is null) return Results.NotFound("Newsletter not found.");
      if (string.IsNullOrEmpty(newsletter.CoverPhoto)) return Results.BadRequest("Missing cover photo.");
      var articles = await tableService.ListArticlesAsync(key);
      if (articles.Any(o => !o.IsApproved)) return Results.BadRequest("Not all articles are approved.");
      var formats = await NewsletterFormatter.FormatAsync(publishData.Html, Organisation.ByDomain[context.User.GetDomain()].TwitterHandle);
      var blobService = new BlobService(domain);
      await blobService.PublishNewsletterAsync(key, formats.WebHtml, formats.EmailHtml, formats.EmailPlainText);
      await blobService.AppendToNewsletterListAsync(new() { Date = key, Description = publishData.Description });
      newsletter.Description = publishData.Description;
      newsletter.LastPublished = DateTime.UtcNow;
      await tableService.UpdateNewsletterAsync(newsletter);
      return Results.Ok();
    });

    group.MapPost("/newsletters/{key}/send", [Authorize(Roles = Roles.Editor)] async (string key, SendData sendData, HttpContext context) =>
    {
      if (string.IsNullOrEmpty(key)) return Results.BadRequest("Missing newsletter key.");
      var domain = context.User.GetDomain();
      var tableService = new TableService(domain);
      var newsletter = await tableService.GetNewsletterAsync(key);
      if (newsletter is null) return Results.NotFound("Newsletter not found.");
      if (newsletter.LastPublished is null) return Results.BadRequest("Newsletter has not been published.");
      if (newsletter.IsSent) return Results.BadRequest("Newsletter has already been sent.");
      var articles = await tableService.ListArticlesAsync(key);
      if (articles.Any(o => o.Timestamp > newsletter.LastPublished)) return Results.BadRequest("Newsletter has been updated since last publish.");
      var introContent = JsonSerializer.Deserialize<ArticleContentData>(articles.Single(o => o.ShortName == "intro").Content);
      var title = introContent.Headline;
      var thisOrganisation = Organisation.ByDomain[domain];
      var currentUserEmail = context.User.GetEmail();

      var blobService = new BlobService(domain);
      var html = await blobService.ReadTextAsync(key, "email.html");
      var plainText = await blobService.ReadTextAsync(key, "email.txt");
      var mailer = new Mailer();

      switch (sendData.To)
      {
        case "preview":
          mailer.Enqueue(context.User.GetEmail(), $"Preview: {title}", thisOrganisation.FromEmail, currentUserEmail, true, html, plainText);
          await mailer.SendAsync();
          break;

        case "qa":
          mailer.Enqueue(thisOrganisation.QualityAssuranceEmail, $"Please QA: {title}", thisOrganisation.FromEmail, currentUserEmail, true, html, plainText);
          await mailer.SendAsync();
          break;

        case "all":
          if (!newsletter.IsTimeToSend()) return Results.BadRequest("It is too early to send this newsletter.");
          return Results.Stream(async (outputStream) =>
          {
            var allRecipientsIncludingSuppressed = await tableService.ListRecipientsAsync();
            var suppressed = await Mailer.GetSuppressedRecipientsAsync();
            var recipients = allRecipientsIncludingSuppressed.Where(o => !suppressed.Contains(o)).ToList();

            var batches = recipients.Chunk(100).ToList();
            var total = (float)recipients.Count;
            var sent = 0;
            foreach (var batch in batches)
            {
              foreach (var recipient in batch)
              {
                mailer.Enqueue(recipient, title, thisOrganisation.FromEmail, null, true, html, plainText);
                sent++;
              }
              await mailer.SendAsync();
              var perc = (int)(sent / total * 100.0f);
              var bytes = Encoding.UTF8.GetBytes(perc.ToString(CultureInfo.InvariantCulture));
              await outputStream.WriteAsync(bytes);
              await outputStream.FlushAsync();
            }

            if (thisOrganisation.SocialMediaEmail is not null)
            {
              var socialMediaHtml = "<html><body style=\"font-family: arial, helvetica, sans-serif; font-size: 11pt\">Hi<br /><br />" +
                "Please post the latest newsletter on social media.<br /><br />Best wishes<br /><br />" +
                $"{thisOrganisation.Name}<br /><br /></body></html>";
              mailer.Enqueue(thisOrganisation.SocialMediaEmail, "Newsletter", thisOrganisation.FromEmail, currentUserEmail, false, socialMediaHtml);
              await mailer.SendAsync();
            }

            newsletter.IsSent = true;
            await tableService.UpdateNewsletterAsync(newsletter);
          }, contentType: "text/plain; charset=utf-8");

        default:
          return Results.BadRequest("Invalid recipient.");
      }

      return Results.Ok();
    });
  }

  private static async Task<bool> ValidateOrderAsync(TableService service, string key, string order, string add = null, string remove = null)
  {
    var articles = await service.ListArticlesAsync(key);
    var validKeys = articles.Select(o => o.ShortName).Where(o => o != "intro").ToHashSet();
    if (add is not null) validKeys.Add(add);
    if (remove is not null) validKeys.Remove(remove);
    var submittedKeys = order is null ? [] : order.Split(',').Where(o => o.Length > 0).ToHashSet();
    return submittedKeys.SetEquals(validKeys);
  }
}

public static class AntiForgeryExtensions
{
  public static TBuilder ValidateAntiforgery<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
  {
    return builder.AddEndpointFilter(async (context, next) =>
    {
      try
      {
        var antiForgeryService = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        await antiForgeryService.ValidateRequestAsync(context.HttpContext);
      }
      catch (AntiforgeryValidationException)
      {
        return Results.BadRequest("Antiforgery token validation failed.");
      }

      return await next(context);
    });
  }
}