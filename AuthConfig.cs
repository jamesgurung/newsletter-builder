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
            if (issued.HasValue && issued.Value > DateTimeOffset.UtcNow.AddDays(-1))
            {
              return;
            }
            var email = context.Principal.GetEmail();
            var identity = new ClaimsIdentity(context.Principal.Identity.AuthenticationType);
            if (await RefreshIdentityAsync(identity, email))
            {
              context.ReplacePrincipal(new ClaimsPrincipal(identity));
              context.ShouldRenew = true;
            }
            else
            {
              context.RejectPrincipal();
              await context.HttpContext.SignOutAsync();
            };
          }
        };
      })
      .AddOpenIdConnect("Microsoft", "Microsoft", o =>
      {
        o.Authority = $"https://login.microsoftonline.com/{builder.Configuration["Azure:TenantId"]}/v2.0/";
        o.ClientId = builder.Configuration["Azure:ClientId"];
        o.ResponseType = OpenIdConnectResponseType.IdToken;
        o.Scope.Add("profile");
        o.Events = new()
        {
          OnTicketReceived = async context =>
          {
            var email = context.Principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Upn)?.Value.ToLowerInvariant();
            if (!await RefreshIdentityAsync((ClaimsIdentity)context.Principal.Identity, email))
            {
              context.Response.Redirect("/auth/denied");
              context.HandleResponse();
            };
          }
        };
      });

    builder.Services.AddAuthorizationBuilder()
      .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
  }

  private static async Task<bool> RefreshIdentityAsync(ClaimsIdentity identity, string email)
  {
    User user = null;
    if (email is not null)
    {
      var emailParts = email.Split('@');
      if (Organisation.ByDomain.ContainsKey(emailParts[1]))
      {
        var service = new TableService(emailParts[1]);
        user = await service.GetUserAsync(emailParts[0]);
      }
    }
    if (user is null)
    {
      return false;
    }
    for (var i = identity.Claims.Count() - 1; i >= 0; i--)
    {
      identity.RemoveClaim(identity.Claims.ElementAt(i));
    }
    identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
    identity.AddClaim(new Claim(ClaimTypes.Email, $"{user.RowKey}@{user.PartitionKey}"));
    identity.AddClaim(new Claim(ClaimTypes.GivenName, user.FirstName));
    identity.AddClaim(new Claim(ClaimTypes.Role, user.IsEditor ? Roles.Editor : Roles.Contributor));
    return true;
  }

  private static readonly string[] authenticationSchemes = ["Microsoft"];

  public static void MapAuthPaths(this WebApplication app)
  {
    app.MapGet("/auth/login/challenge", [AllowAnonymous] ([FromQuery] string path) =>
      Results.Challenge(
        new AuthenticationProperties { RedirectUri = path is null ? "/" : WebUtility.UrlDecode(path), AllowRefresh = true, IsPersistent = true },
        authenticationSchemes
      )
    );

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

public static class Roles {
  public const string Contributor = nameof(Contributor);
  public const string Editor = nameof(Editor);
}