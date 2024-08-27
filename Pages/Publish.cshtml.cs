using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;

namespace NewsletterBuilder.Pages;

public class PublishPageModel() : PageModel
{
  public string NewsletterKey { get; set; }
  public bool IsPublished { get; set; }
  public bool IsSent { get; set; }
  public bool AllArticlesApproved { get; set; }
  public bool CoverImageSet { get; set; }
  public bool IsTimeToSend { get; set; }
  public string Description { get; set; }

  public async Task<IActionResult> OnGet(string date)
  {
    if (!User.IsInRole(Roles.Editor)) return Forbid();
    NewsletterKey = date;
    var domain = User.GetDomain();
    var tableService = new TableService(domain);
    var newsletter = await tableService.GetNewsletterAsync(date);
    if (newsletter is null) return NotFound();
    IsTimeToSend = newsletter.IsTimeToSend();
    CoverImageSet = !string.IsNullOrEmpty(newsletter.CoverPhoto);
    var articles = await tableService.ListArticlesAsync(date);
    IsPublished = newsletter.LastPublished is not null && newsletter.LastPublished > articles.Select(o => o.Timestamp).Max();
    AllArticlesApproved = articles.All(o => o.IsApproved);
    IsSent = newsletter.IsSent;
    Description = newsletter.Description;
    if (Description is null) {
      var textInfo = CultureInfo.InvariantCulture.TextInfo;
      var unlisted = Organisation.ByDomain[domain].UnlistedArticles ?? [];
      var articleTitles = newsletter.ArticleOrder.Split(',')
        .Where(o => !unlisted.Contains(o, StringComparer.OrdinalIgnoreCase))
        .Select(o => articles.FirstOrDefault(a => a.ShortName == o)?.Title).Where(o => o is not null).ToList();
      var introArticle = articles.First(o => o.ShortName == "intro");
      if (introArticle.Title != "Intro") articleTitles.Insert(0, introArticle.Title);
      Description = articleTitles switch
      {
        [] => string.Empty,
        [var el] => el,
        [var el1, var el2] => $"{el1} and {el2}",
        [..var els] => $"{string.Join(", ", els[..^1])}, and {els[^1]}",
      };
    }
    return Page();
  }
}