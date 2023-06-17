using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NewsletterBuilder.Pages;

public class RecipientsPageModel(TableServiceClient tableClient) : PageModel
{
  public int RecipientCount { get; set; }

  public async Task<IActionResult> OnGet()
  {
    if (!User.IsInRole(Roles.Editor)) return Forbid();
    var domain = User.GetDomain();
    var tableService = new TableService(tableClient, domain);
    RecipientCount = await tableService.CountRecipientsAsync();
    return Page();
  }
}