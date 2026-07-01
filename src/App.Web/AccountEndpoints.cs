using System.Net;
using System.Text;
using App.Application.Security;
using App.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace App.Web;

/// <summary>
/// Static (non-circuit) account endpoints. Sign-in must set an auth cookie, which a Blazor Server circuit
/// cannot do, so login/logout are plain HTTP endpoints rendered server-side and anonymous. Everything else
/// (admin, the app) stays in the interactive Blazor components behind the authorization policies.
/// </summary>
internal static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        RouteGroupBuilder account = app.MapGroup("/account").AllowAnonymous();

        account.MapGet("/login", (HttpContext ctx, IAntiforgery antiforgery, string? returnUrl, int? error) =>
        {
            AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(ctx);
            return Results.Content(LoginPage(tokens, returnUrl, error), "text/html");
        });

        account.MapPost("/login", async (HttpContext ctx, IAntiforgery antiforgery,
            SignInManager<AppUser> signIn, UserManager<AppUser> users, ISecurityAudit audit) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(ctx);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Redirect("/account/login?error=1");
            }

            IFormCollection form = await ctx.Request.ReadFormAsync();
            string userName = form["username"].ToString().Trim();
            string password = form["password"].ToString();
            string? returnUrl = form["returnUrl"];
            string? ip = ctx.Connection.RemoteIpAddress?.ToString();

            AppUser? user = await users.FindByNameAsync(userName);
            if (user is null || !user.IsEnabled)
            {
                await audit.RecordAsync(SecurityEvents.LoginFailed, userName, false, user is null ? "unknown user" : "disabled", ip);
                return Results.Redirect(LoginUrl(1, returnUrl));
            }

            SignInResult result = await signIn.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                user.LastLoginUtc = DateTimeOffset.UtcNow;
                await users.UpdateAsync(user);
                await audit.RecordAsync(SecurityEvents.LoginSucceeded, userName, true, null, ip);
                return Results.Redirect(SafeReturnUrl(returnUrl));
            }

            await audit.RecordAsync(result.IsLockedOut ? SecurityEvents.LoginLockedOut : SecurityEvents.LoginFailed, userName, false, null, ip);
            return Results.Redirect(LoginUrl(result.IsLockedOut ? 2 : 1, returnUrl));
        }).DisableAntiforgery(); // validated explicitly above

        account.MapPost("/logout", async (HttpContext ctx, SignInManager<AppUser> signIn, ISecurityAudit audit) =>
        {
            string? name = ctx.User.Identity?.Name;
            await signIn.SignOutAsync();
            await audit.RecordAsync(SecurityEvents.Logout, name, true);
            return Results.Redirect("/account/login");
        });

        account.MapGet("/denied", () => Results.Content(DeniedPage(), "text/html", null, statusCode: 403));
    }

    private static string SafeReturnUrl(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.Contains(':', StringComparison.Ordinal)
            ? returnUrl
            : "/";

    private static string LoginUrl(int error, string? returnUrl)
        => $"/account/login?error={error}" + (string.IsNullOrEmpty(returnUrl) ? "" : $"&returnUrl={WebUtility.UrlEncode(returnUrl)}");

    private static string LoginPage(AntiforgeryTokenSet tokens, string? returnUrl, int? error)
    {
        string message = error switch
        {
            2 => "<p class=\"err\">Account locked. Try again later.</p>",
            1 => "<p class=\"err\">Invalid username or password.</p>",
            _ => "",
        };

        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Sign in — Mapping Studio</title>
          <link rel="stylesheet" href="/_content/App.UI/css/app.css">
          <style>
            body{align-items:center;justify-content:center}
            .login{background:var(--surface);border:1px solid var(--border);border-radius:12px;
              box-shadow:var(--shadow);padding:30px 32px;width:340px;max-width:92vw}
            .login .ms-brand{font-size:17px;margin-bottom:4px}
            .login .sub{color:var(--text-3);font-size:13px;margin:0 0 20px}
            .login label{display:block;font-size:12px;color:var(--text-2);font-weight:600;margin:12px 0 5px}
            .login input{width:100%;font:inherit;font-size:14px;padding:9px 11px;border:1px solid var(--border-strong);
              border-radius:8px;background:var(--surface);color:var(--text)}
            .login input:focus{outline:none;border-color:var(--accent);box-shadow:0 0 0 2px var(--accent-soft)}
            .login button{width:100%;margin-top:20px;padding:10px}
            .login .err{color:var(--err);font-size:13px;margin:14px 0 0}
          </style>
        </head>
        <body>
          <form class="login" method="post" action="/account/login">
            <div class="ms-brand"><span class="glyph">&#x21F2;</span><span>Mapping Studio</span></div>
            <p class="sub">Sign in to continue</p>
            <input type="hidden" name="{{tokens.FormFieldName}}" value="{{tokens.RequestToken}}">
            <input type="hidden" name="returnUrl" value="{{WebUtility.HtmlEncode(returnUrl ?? "/")}}">
            <label for="username">Username</label>
            <input id="username" name="username" autocomplete="username" autofocus required>
            <label for="password">Password</label>
            <input id="password" name="password" type="password" autocomplete="current-password" required>
            <button class="ms-publish" type="submit">Sign in</button>
            {{message}}
          </form>
        </body>
        </html>
        """;
    }

    private static string DeniedPage() => """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>Access denied — Mapping Studio</title>
          <link rel="stylesheet" href="/_content/App.UI/css/app.css">
          <style>body{align-items:center;justify-content:center;text-align:center}</style>
        </head>
        <body>
          <div>
            <h1 class="page-title">Access denied</h1>
            <p class="muted">You don't have permission to view this page.</p>
            <p><a href="/">Return to the app</a></p>
          </div>
        </body>
        </html>
        """;
}
