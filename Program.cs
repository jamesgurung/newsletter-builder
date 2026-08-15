using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using NewsletterBuilder;

var builder = WebApplication.CreateBuilder(args);

var appConfigEndpoint = builder.Configuration["AppConfigurationEndpoint"];
var appConfigConnectionString = builder.Configuration.GetConnectionString("AppConfiguration");
if (appConfigEndpoint is not null || appConfigConnectionString is not null)
{
  builder.Configuration.AddAzureAppConfiguration(options =>
  {
    if (appConfigEndpoint is not null)
    {
      options.Connect(new Uri(appConfigEndpoint), new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
    }
    else
    {
      options.Connect(appConfigConnectionString);
    }
    options
      .Select("Shared:*")
      .Select("NewsletterBuilder:*")
      .TrimKeyPrefix("Shared:")
      .TrimKeyPrefix("NewsletterBuilder:");
  });
}

var appSettings = builder.Configuration.Get<AppSettings>();
builder.Services.AddSingleton(appSettings);

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
  o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
  o.KnownIPNetworks.Clear();
#pragma warning disable ASPDEPR005
  o.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
  o.KnownProxies.Clear();
});

builder.Services.Configure<FormOptions>(options =>
{
  options.MemoryBufferThreshold = 2 * 1024 * 1024;
});

builder.Services.AddDataProtection().PersistKeysToAzureBlobStorage(new Uri(builder.Configuration["DataProtectionBlobUri"]));

var storageAccountName = builder.Configuration["StorageAccountName"];
var storageAccountKey = builder.Configuration["StorageAccountKey"];
var connectionString = $"DefaultEndpointsProtocol=https;AccountName={storageAccountName};AccountKey={storageAccountKey};EndpointSuffix=core.windows.net";
TableService.Configure(connectionString, appSettings.EmailDomain);
BlobService.Configure(connectionString, storageAccountKey, appSettings.EmailDomain);

AutomationApi.Configure(builder.Configuration["SyncApiKey"]);
Mailer.Configure(builder.Configuration["PostmarkServerToken"], builder.Environment.IsDevelopment());

builder.ConfigureAuth(appSettings);
builder.Services.AddResponseCompression();
builder.Services.AddAntiforgery(options => { options.HeaderName = "X-XSRF-TOKEN"; });
builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });
builder.Services.AddRazorPages(options => { options.Conventions.AllowAnonymousToFolder("/auth"); });

AIService.Configure(builder.Configuration["AIFoundryEndpoint"], builder.Configuration["AIFoundryDeployment"], builder.Configuration["AIFoundryApiKey"]);

builder.Services.AddHostedService<ReminderService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
  var canonicalHost = appSettings.NewsletterEditorUrl.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
  app.Use(async (context, next) =>
  {
    if (context.Request.Path.Value == "/" && context.Request.Headers.UserAgent.ToString().Equals("alwayson", StringComparison.OrdinalIgnoreCase))
    {
      await TableService.WarmUpAsync();
      context.Response.StatusCode = 200;
    }
    else if (!context.Request.Host.Host.Equals(canonicalHost, StringComparison.OrdinalIgnoreCase))
    {
      context.Response.Redirect($"https://{canonicalHost}{context.Request.Path.Value}{context.Request.QueryString}", true);
    }
    else
    {
      await next();
    }
  });
  app.UseHsts();
}

app.UseForwardedHeaders();
app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorPages();
app.MapAuthPaths();
app.MapApiPaths(appSettings);
app.MapGet("/nav.js", () => Results.Content(string.Empty, "text/javascript"));
app.MapAutomationApiPaths();

app.Run();
