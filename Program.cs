using Microsoft.AspNetCore.Http.Features;
using NewsletterBuilder;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
  options.MemoryBufferThreshold = 2 * 1024 * 1024;
});

var storageAccountName = builder.Configuration["Azure:StorageAccountName"];
var storageAccountKey = builder.Configuration["Azure:StorageAccountKey"];
var connectionString = $"DefaultEndpointsProtocol=https;AccountName={storageAccountName};AccountKey={storageAccountKey};EndpointSuffix=core.windows.net";
TableService.Configure(connectionString);
BlobService.Configure(connectionString, storageAccountKey);

Organisation.Instance = builder.Configuration.GetSection("Organisation").Get<Organisation>();
AutomationApi.Configure(builder.Configuration["AutomationApiKey"]);
Mailer.Configure(builder.Configuration["PostmarkServerToken"], Organisation.Instance.FromEmail, Organisation.Instance.ReminderReplyTo,
  builder.Environment.IsDevelopment());

builder.ConfigureAuth();
builder.Services.AddResponseCompression();
builder.Services.AddAntiforgery(options => { options.HeaderName = "X-XSRF-TOKEN"; });
builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });
builder.Services.AddRazorPages(options => { options.Conventions.AllowAnonymousToFolder("/auth"); });

builder.Services.AddHttpClient("AzureOpenAI", options => {
  options.DefaultRequestHeaders.Add("api-key", builder.Configuration["Azure:OpenAIKey"]);
  options.BaseAddress = new Uri(builder.Configuration["Azure:OpenAIEndpoint"]);
});

builder.Services.AddSignalR();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
  var domain = Organisation.Instance.NewsletterEditorUrl.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
  app.Use(async (context, next) =>
  {
    if (context.Request.Path.Value == "/" && context.Request.Headers.UserAgent.ToString().Equals("alwayson", StringComparison.OrdinalIgnoreCase))
    {
      await TableService.WarmUpAsync();
      context.Response.StatusCode = 200;
    }
    else if (!context.Request.Host.Host.Equals(domain, StringComparison.OrdinalIgnoreCase))
    {
      context.Response.Redirect($"https://{domain}{context.Request.Path.Value}{context.Request.QueryString}", true);
    }
    else
    {
      await next();
    }
  });
  app.UseHsts();
}

app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<ChatHub>("/chat");
app.MapRazorPages();
app.MapAuthPaths();
app.MapApiPaths();
app.MapAutomationApiPaths();

app.Run();
