using Azure.Data.Tables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsletterBuilder.Entities;
using System.Globalization;
using System.Text.Json;

namespace NewsletterBuilder.Pages;

[Authorize(Roles = Roles.Editor)]
public class RenderPageModel : PageModel
{
  private readonly TableServiceClient _tableClient;

  public string NewsletterDate { get; set; }
  public IList<CalendarEvent> Events { get; set; }
  public IList<DisplayArticle> Articles { get; set; }
  public string CoverPhoto { get; set; }

  public RenderPageModel(TableServiceClient tableClient)
  {
    _tableClient = tableClient;
  }

  public async Task<IActionResult> OnGet(string date)
  {
    var domain = User.GetDomain();
    var tableService = new TableService(_tableClient, domain);
    var newsletter = await tableService.GetNewsletterAsync(date);
    if (newsletter is null) return NotFound();
    var articles = await tableService.ListArticlesAsync(date);
    var users = (await tableService.ListUsersAsync()).Where(o => !o.IsEditor).ToDictionary(o => o.RowKey, o => o.DisplayName);
    Articles = OrderArticles(articles, newsletter.ArticleOrder).Select(o => new DisplayArticle {
      ShortName = o.ShortName,
      Content = o.Content is null ? new() { Sections = new List<ArticleSection>() } : JsonSerializer.Deserialize<ArticleContentData>(o.Content),
      AuthorDisplayName = users.TryGetValue(o.ContributorList[0], out var name) ? name : null
    }).ToList();
    foreach (var article in Articles) {
      if (article.Content.Sections.Count == 0) article.Content.Sections.Add(new() { IncludeImage = article.ShortName != "intro" });
      AddImageRenderNames(article.ShortName, article.Content.Sections);
    }
    CoverPhoto = (string.IsNullOrEmpty(newsletter.CoverPhoto) ? null : Articles.SelectMany(o => o.Content.Sections)
      .FirstOrDefault(o => o.Image == newsletter.CoverPhoto)?.ImageRenderName)
      ?? Articles.SelectMany(o => o.Content.Sections).FirstOrDefault(o => o.Image is not null)?.ImageRenderName;
    var twoMonthsTime = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    Events = (await tableService.ListEventsAsync())
      .Where(o => o.IsApproved && string.Compare(o.EndDate, date, StringComparison.Ordinal) > 0 && string.Compare(o.StartDate, twoMonthsTime, StringComparison.Ordinal) <= 0).ToList();
    NewsletterDate = date;
    return Page();
  }

  public static IList<Article> OrderArticles(IEnumerable<Article> articles, string order) {
    var orderDictionary = order?.Split(',').Select((id, pos) => new { Id = id, Pos = pos })
      .ToDictionary(o => o.Id, o => o.Pos) ?? new Dictionary<string, int>();
    return articles.OrderBy(o => o.ShortName != "intro")
      .ThenBy(o => orderDictionary.TryGetValue(o.ShortName, out var value) ? value : int.MaxValue).ToList();
  }

  public static void AddImageRenderNames(string articleShortName, IList<ArticleSection> sections) {
    var count = 1;
    var sectionsWithImages = sections.Where(o => o.IncludeImage).ToList();
    foreach (var section in sections.Where(o => o.IncludeImage)) {
      var ext = string.IsNullOrEmpty(section.Image) ? "jpg" : section.Image.Split('.').Last();
      section.ImageRenderName = $"{articleShortName}{(sectionsWithImages.Count == 1 ? string.Empty : count++)}.{ext}";
    }
  }
}