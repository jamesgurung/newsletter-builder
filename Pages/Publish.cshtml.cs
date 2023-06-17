using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;

namespace NewsletterBuilder.Pages;

public class PublishPageModel(TableServiceClient tableClient) : PageModel
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
    var tableService = new TableService(tableClient, domain);
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
      var articleOrder = newsletter.ArticleOrder.Split(',').Select(textInfo.ToTitleCase).ToList();
      Description = articleOrder switch
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