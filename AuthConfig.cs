using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
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
        o.ExpireTimeSpan = TimeSpan.FromDays(90);
        o.SlidingExpiration = true;
        o.ReturnUrlParameter = "path";
        o.Events = new()
        {
          OnRedirectToAccessDenied = context =>
          {
            context.Response.StatusCode = 403;
            return Task.CompletedTask;
          }
        };
      })
      .AddOpenIdConnect("Microsoft", "Microsoft", o =>
      {
        o.Authority = $"https://login.microsoftonline.com/{builder.Configuration["Azure:TenantId"]}/v2.0/";
        o.ClientId = builder.Configuration["Azure:ClientId"];
        o.ResponseType = OpenIdConnectResponseType.IdToken;
        o.Events = new()
        {
          OnTicketReceived = async context =>
          {
            var email = context.Principal.Claims.FirstOrDefault(c => c.Type == "preferred_username").Value.ToLowerInvariant();
            var emailParts = email.Split('@');
            var service = new TableService(emailParts[1]);
            var user = await service.GetUserAsync(emailParts[0]);
            if (user is null)
            {
              context.Response.Redirect("/auth/denied");
              context.HandleResponse();
              return;
            }
            var identity = context.Principal.Identity as ClaimsIdentity;
            for (var i = identity.Claims.Count() - 1; i >= 0; i--)
            {
              identity.RemoveClaim(identity.Claims.ElementAt(i));
            }
            identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
            identity.AddClaim(new Claim(ClaimTypes.Email, email));
            identity.AddClaim(new Claim(ClaimTypes.GivenName, user.FirstName));
            identity.AddClaim(new Claim(ClaimTypes.Role, user.IsEditor ? Roles.Editor : Roles.Contributor));
          }
        };
      });

    builder.Services.AddAuthorizationBuilder()
      .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
  }

  private static readonly string[] authenticationSchemes = new[] { "Microsoft" };

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