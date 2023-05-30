using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NewsletterBuilder.Pages;

public class RecipientsPageModel : PageModel
{
  private readonly TableServiceClient _tableClient;
  public int RecipientCount { get; set; }

  public RecipientsPageModel(TableServiceClient tableClient)
  {
    _tableClient = tableClient;
  }

  public async Task<IActionResult> OnGet()
  {
    if (!User.IsInRole(Roles.Editor)) return Forbid();
    var domain = User.GetDomain();
    var tableService = new TableService(_tableClient, domain);
    RecipientCount = await tableService.CountRecipientsAsync();
    return Page();
  }
}