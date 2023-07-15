using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NewsletterBuilder.Entities;

namespace NewsletterBuilder.Pages;

public class ArticlePageModel() : PageModel
{
  public Article Article { get; set; }
  public bool IsEditor { get; set; }
  public bool IsOwner { get; set; }
  public string BlobBaseUrl { get; set; }
  public string BlobSas { get; set; }
  public string CoverPhoto { get; set; }

  public async Task<IActionResult> OnGet(string date, string articleName)
  {
    var domain = User.GetDomain();
    var tableService = new TableService(domain);
    var key = $"{date}_{articleName}";
    Article = await tableService.GetArticleAsync(key);
    if (Article is null) return NotFound();
    var username = User.GetUsername();
    IsEditor = User.IsInRole(Roles.Editor);
    IsOwner = Article.Owner == username;
    if (!Article.ContributorList.Contains(username) && !IsEditor) return Forbid();

    var newsletter = await tableService.GetNewsletterAsync(Article.Date);
    CoverPhoto = newsletter.CoverPhoto;

    var blobService = new BlobService(domain);
    BlobBaseUrl = $"{BlobService.Uri}photos/";
    BlobSas = blobService.GetSasQueryString();
    
    return Page();
  }
}