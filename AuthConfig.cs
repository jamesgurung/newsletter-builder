using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NewsletterBuilder.Entities;
using System.Net;
using System.Security.Claims;

namespace NewsletterBuilder;

public static class AuthConfig
{
  public static void ConfigureAuth(this WebApplicationBuilder builder)
  {
    ArgumentNullException.ThrowIfNull(builder);
    builder.Services
      .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
      .AddCookie(o =>
      {
        o.Cookie.Path = "/";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.LoginPath = "/auth/login";
        o.LogoutPath = "/auth/logout";
        o.ExpireTimeSpan = TimeSpan.FromDays(60);
        o.SlidingExpiration = true;
        o.ReturnUrlParameter = "path";
        o.Events = new()
        {
          OnRedirectToAccessDenied = context =>
          {
            context.Response.StatusCode = 403;
            return Task.CompletedTask;
          },
          OnValidatePrincipal = async context =>
          {
            var issued = context.Properties.IssuedUtc;
            if (issued.HasValue && issued.Value > DateTimeOffset.UtcNow.AddDays(-1)) return;

            var email = context.Principal.GetEmail();
            var user = await GetUserAsync(email);
            if (user is not null)
            {
              context.Principal = CreatePrincipal(user);
              context.ShouldRenew = true;
            }
            else
            {
              context.RejectPrincipal();
              await context.HttpContext.SignOutAsync();
            }
          }
        };
      })
      .AddOpenIdConnect("Microsoft", o =>
      {
        o.Authority = $"https://login.microsoftonline.com/{builder.Configuration["Azure:TenantId"]}/v2.0/";
        o.ClientId = builder.Configuration["Azure:ClientId"];
        o.ClientSecret = builder.Configuration["Azure:ClientSecret"];
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.MapInboundClaims = false;
        o.Scope.Clear();
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.Events = new()
        {
          OnTicketReceived = async context =>
          {
            var email = context.Principal.FindFirstValue("upn")?.ToLowerInvariant();
            var user = await GetUserAsync(email);
            if (user is not null)
            {
              context.Principal = CreatePrincipal(user);
            }
            else
            {
              context.Fail("Unauthorised");
              context.Response.Redirect("/auth/denied");
              context.HandleResponse();
            }
          }
        };
      });

    builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
  }

  private static async Task<User> GetUserAsync(string email)
  {
    var emailParts = email?.Split('@');
    if (email is null || !Organisation.ByDomain.ContainsKey(emailParts[1])) return null;
    var service = new TableService(emailParts[1]);
    return await service.GetUserAsync(emailParts[0]);
  }

  private static ClaimsPrincipal CreatePrincipal(User user)
  {
    var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
    identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
    identity.AddClaim(new Claim(ClaimTypes.Email, $"{user.RowKey}@{user.PartitionKey}"));
    identity.AddClaim(new Claim(ClaimTypes.GivenName, user.FirstName));
    identity.AddClaim(new Claim(ClaimTypes.Role, user.IsEditor ? Roles.Editor : Roles.Contributor));
    return new ClaimsPrincipal(identity);
  }

  private static readonly string[] authenticationSchemes = ["Microsoft"];

  public static void MapAuthPaths(this WebApplication app)
  {
    app.MapGet("/auth/login/challenge", [AllowAnonymous] ([FromQuery] string path) =>
    {
      var authProperties = new AuthenticationProperties { RedirectUri = path is null ? "/" : WebUtility.UrlDecode(path), AllowRefresh = true, IsPersistent = true };
      return Results.Challenge(authProperties, authenticationSchemes);
    });

    app.MapGet("/auth/logout", (HttpContext context) =>
    {
      context.SignOutAsync();
      return Results.Redirect("/auth/login");
    });
  }

  public static string GetEmail(this ClaimsPrincipal user) => user?.Claims.FirstOrDefault(o => o.Type == ClaimTypes.Email)?.Value;
  public static string GetFirstName(this ClaimsPrincipal user) => user?.Claims.FirstOrDefault(o => o.Type == ClaimTypes.GivenName)?.Value;
  public static string GetRole(this ClaimsPrincipal user) => user?.Claims.FirstOrDefault(o => o.Type == ClaimTypes.Role)?.Value;
  public static string GetUsername(this ClaimsPrincipal user) => user?.Claims.FirstOrDefault(o => o.Type == ClaimTypes.Email)?.Value.Split('@')[0];
  public static string GetDomain(this ClaimsPrincipal user) => user?.Claims.FirstOrDefault(o => o.Type == ClaimTypes.Email)?.Value.Split('@')[1];
}

public static class Roles
{
  public const string Contributor = nameof(Contributor);
  public const string Editor = nameof(Editor);
}