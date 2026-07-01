using System.Net;
using System.Security.Claims;
using App.Application.Security;
using App.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace App.Web;

/// <summary>
/// Self-service account flows (password reset / change, registration + email confirmation, MFA enrolment
/// and the two-factor login step) as static HTTP endpoints — a Blazor circuit can't set auth cookies, so
/// these render server-side like the sign-in page. Token generation lives in <see cref="AccountService"/>.
/// </summary>
internal static class AccountSelfService
{
    // ---------------------------- anonymous flows ----------------------------

    public static void MapSelfServiceAnonymousEndpoints(this RouteGroupBuilder account)
    {
        // Forgot password: always report success (no account enumeration).
        account.MapGet("/forgot", (HttpContext ctx, IAntiforgery af, bool? sent) =>
        {
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string msg = sent == true ? "<p class=\"ok\">If that account exists, a reset link has been sent.</p>" : "";
            return Html(AccountHtml.Shell("Reset password", AccountHtml.Brand("Reset your password") + $$"""
                <form method="post" action="/account/forgot">{{AccountHtml.Hidden(t)}}
                  <label>Email</label>
                  <input name="email" type="email" autocomplete="email" autofocus required>
                  <button class="ms-publish" type="submit">Send reset link</button>{{msg}}
                  <div class="links"><a href="/account/login">Back to sign in</a></div>
                </form>
                """));
        });

        account.MapPost("/forgot", async (HttpContext ctx, IAntiforgery af, AccountService accounts, IAppEmailSender email, ISecurityAudit audit) =>
        {
            if (!await ValidAsync(ctx, af)) { return Results.Redirect("/account/forgot"); }
            IFormCollection form = await ctx.Request.ReadFormAsync();
            string address = form["email"].ToString().Trim();

            (AppUser User, string Token)? ticket = await accounts.GeneratePasswordResetAsync(address);
            if (ticket is { } t)
            {
                string link = Absolute(ctx, $"/account/reset?userId={WebUtility.UrlEncode(t.User.Id.ToString())}&token={WebUtility.UrlEncode(t.Token)}");
                await email.SendAsync(address, "Reset your Mapping Studio password",
                    $"<p>We received a request to reset your password. This link expires shortly:</p><p><a href=\"{link}\">{link}</a></p>");
                await audit.RecordAsync(SecurityEvents.UserPasswordReset, address, true, "reset link requested", ctx.Connection.RemoteIpAddress?.ToString());
            }

            return Results.Redirect("/account/forgot?sent=true");
        }).DisableAntiforgery();

        account.MapGet("/reset", (HttpContext ctx, IAntiforgery af, string userId, string token, int? error) =>
        {
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string msg = error == 1 ? "<p class=\"err\">Could not reset — the link may have expired.</p>" : "";
            return Html(AccountHtml.Shell("Set a new password", AccountHtml.Brand("Choose a new password") + $$"""
                <form method="post" action="/account/reset">{{AccountHtml.Hidden(t)}}
                  <input type="hidden" name="userId" value="{{WebUtility.HtmlEncode(userId)}}">
                  <input type="hidden" name="token" value="{{WebUtility.HtmlEncode(token)}}">
                  <label>New password</label>
                  <input name="password" type="password" autocomplete="new-password" autofocus required>
                  <button class="ms-publish" type="submit">Set password</button>{{msg}}
                </form>
                """));
        });

        account.MapPost("/reset", async (HttpContext ctx, IAntiforgery af, AccountService accounts) =>
        {
            if (!await ValidAsync(ctx, af)) { return Results.Redirect("/account/login"); }
            IFormCollection form = await ctx.Request.ReadFormAsync();
            string userId = form["userId"].ToString();
            OperationResult result = await accounts.ResetPasswordAsync(userId, form["token"].ToString(), form["password"].ToString());
            return result.Succeeded
                ? Results.Redirect("/account/login?notice=1")
                : Results.Redirect($"/account/reset?userId={WebUtility.UrlEncode(userId)}&token={WebUtility.UrlEncode(form["token"].ToString())}&error=1");
        }).DisableAntiforgery();

        account.MapGet("/confirm", async (AccountService accounts, string userId, string token) =>
        {
            OperationResult result = await accounts.ConfirmEmailAsync(userId, token);
            string body = AccountHtml.Brand(result.Succeeded ? "Email confirmed" : "Confirmation failed")
                + (result.Succeeded
                    ? "<p class=\"ok\">Your email is confirmed — you can sign in.</p>"
                    : "<p class=\"err\">This confirmation link is invalid or has expired.</p>")
                + "<div class=\"links\"><a href=\"/account/login\">Go to sign in</a></div>";
            return Html(AccountHtml.Shell("Confirm email", body));
        });

        account.MapGet("/register", (HttpContext ctx, IAntiforgery af, IOptions<SecurityOptions> options, int? error) =>
        {
            if (!options.Value.Providers.Local.AllowSelfRegistration) { return Results.NotFound(); }
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string msg = error == 1 ? "<p class=\"err\">Could not create the account. The username or email may be taken, or the password too weak.</p>" : "";
            return Html(AccountHtml.Shell("Create account", AccountHtml.Brand("Create your account") + $$"""
                <form method="post" action="/account/register">{{AccountHtml.Hidden(t)}}
                  <label>Username</label><input name="username" autocomplete="username" autofocus required>
                  <label>Email</label><input name="email" type="email" autocomplete="email" required>
                  <label>Password</label><input name="password" type="password" autocomplete="new-password" required>
                  <button class="ms-publish" type="submit">Create account</button>{{msg}}
                  <div class="links"><a href="/account/login">Back to sign in</a></div>
                </form>
                """));
        });

        account.MapPost("/register", async (HttpContext ctx, IAntiforgery af, IOptions<SecurityOptions> options, AccountService accounts, IAppEmailSender email, ISecurityAudit audit) =>
        {
            if (!options.Value.Providers.Local.AllowSelfRegistration) { return Results.NotFound(); }
            if (!await ValidAsync(ctx, af)) { return Results.Redirect("/account/register?error=1"); }
            IFormCollection form = await ctx.Request.ReadFormAsync();
            string userName = form["username"].ToString().Trim();
            string address = form["email"].ToString().Trim();

            (OperationResult Result, string? UserId, string? Token) reg = await accounts.RegisterAsync(userName, address, form["password"].ToString());
            if (!reg.Result.Succeeded) { return Results.Redirect("/account/register?error=1"); }

            string link = Absolute(ctx, $"/account/confirm?userId={WebUtility.UrlEncode(reg.UserId!)}&token={WebUtility.UrlEncode(reg.Token!)}");
            await email.SendAsync(address, "Confirm your Mapping Studio account",
                $"<p>Welcome! Confirm your email to activate your account:</p><p><a href=\"{link}\">{link}</a></p>");
            await audit.RecordAsync(SecurityEvents.UserCreated, userName, true, "self-registered", ctx.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect("/account/login?notice=2");
        }).DisableAntiforgery();

        // Two-factor login step (holds the partial 2FA cookie set by the password step).
        account.MapGet("/2fa", (HttpContext ctx, IAntiforgery af, string? returnUrl, int? error) =>
        {
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string msg = error == 1 ? "<p class=\"err\">Invalid code. Try again, or use a recovery code.</p>" : "";
            return Html(AccountHtml.Shell("Two-factor", AccountHtml.Brand("Two-factor authentication") + $$"""
                <form method="post" action="/account/2fa">{{AccountHtml.Hidden(t)}}
                  <input type="hidden" name="returnUrl" value="{{WebUtility.HtmlEncode(returnUrl ?? "/")}}">
                  <label>Authenticator or recovery code</label>
                  <input name="code" inputmode="numeric" autocomplete="one-time-code" autofocus required>
                  <button class="ms-publish" type="submit">Verify</button>{{msg}}
                </form>
                """));
        });

        account.MapPost("/2fa", async (HttpContext ctx, IAntiforgery af, SignInManager<AppUser> signIn, ISecurityAudit audit) =>
        {
            if (!await ValidAsync(ctx, af)) { return Results.Redirect("/account/login"); }
            IFormCollection form = await ctx.Request.ReadFormAsync();
            string code = form["code"].ToString().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
            string? returnUrl = form["returnUrl"];

            AppUser? user = await signIn.GetTwoFactorAuthenticationUserAsync();
            if (user is null) { return Results.Redirect("/account/login"); }

            bool authenticator = code.Length <= 8 && code.All(char.IsDigit);
            SignInResult result = authenticator
                ? await signIn.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false)
                : await signIn.TwoFactorRecoveryCodeSignInAsync(code);

            if (result.Succeeded)
            {
                await audit.RecordAsync(SecurityEvents.LoginSucceeded, user.UserName, true, "two-factor", ctx.Connection.RemoteIpAddress?.ToString());
                return Results.Redirect(AccountEndpoints.SafeReturnUrl(returnUrl));
            }

            await audit.RecordAsync(SecurityEvents.LoginFailed, user.UserName, false, "two-factor", ctx.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect($"/account/2fa?error=1&returnUrl={WebUtility.UrlEncode(returnUrl ?? "/")}");
        }).DisableAntiforgery();
    }

    // ------------------------- authenticated flows -------------------------
    // Mapped on the app (not the anonymous group) so the fallback policy requires a signed-in user.

    public static void MapSelfServiceAuthenticatedEndpoints(this WebApplication app)
    {
        app.MapGet("/account/password", (HttpContext ctx, IAntiforgery af, int? error, bool? changed) =>
        {
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string msg = error == 1 ? "<p class=\"err\">Current password is incorrect, or the new one is invalid.</p>"
                : changed == true ? "<p class=\"ok\">Password changed.</p>" : "";
            return Html(AccountHtml.Shell("Change password", AccountHtml.Brand("Change your password") + $$"""
                <form method="post" action="/account/password">{{AccountHtml.Hidden(t)}}
                  <label>Current password</label><input name="current" type="password" autocomplete="current-password" autofocus required>
                  <label>New password</label><input name="password" type="password" autocomplete="new-password" required>
                  <button class="ms-publish" type="submit">Change password</button>{{msg}}
                  <div class="links"><a href="/">Back to the app</a><a href="/account/mfa">Two-factor</a></div>
                </form>
                """));
        }).RequireAuthorization();

        app.MapPost("/account/password", async (HttpContext ctx, IAntiforgery af, AccountService accounts) =>
        {
            if (!await ValidAsync(ctx, af)) { return Results.Redirect("/account/password?error=1"); }
            string? userId = ctx.User.FindFirstValue(SecurityClaims.UserId);
            if (userId is null) { return Results.Redirect("/account/login"); }
            IFormCollection form = await ctx.Request.ReadFormAsync();
            OperationResult result = await accounts.ChangePasswordAsync(userId, form["current"].ToString(), form["password"].ToString());
            return Results.Redirect(result.Succeeded ? "/account/password?changed=true" : "/account/password?error=1");
        }).RequireAuthorization().DisableAntiforgery();

        app.MapGet("/account/mfa", async (HttpContext ctx, IAntiforgery af, AccountService accounts, int? error) =>
        {
            string userId = ctx.User.FindFirstValue(SecurityClaims.UserId)!;
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string inner;
            if (await accounts.IsMfaEnabledAsync(userId))
            {
                inner = AccountHtml.Brand("Two-factor authentication")
                    + "<p class=\"ok\">Two-factor authentication is enabled on your account.</p>"
                    + $$"""
                    <form method="post" action="/account/mfa/disable">{{AccountHtml.Hidden(t)}}
                      <button class="add" type="submit">Disable two-factor</button>
                    </form>
                    <div class="links"><a href="/">Back to the app</a></div>
                    """;
            }
            else
            {
                MfaEnrollment enrol = await accounts.BeginMfaEnrollmentAsync(userId);
                string msg = error == 1 ? "<p class=\"err\">That code was invalid. Try again.</p>" : "";
                inner = AccountHtml.Brand("Set up two-factor authentication") + $$"""
                    <p class="sub">Scan the QR with an authenticator app (or type the key), then enter a code to confirm.</p>
                    <img class="qr" src="{{enrol.QrPngDataUri}}" alt="Authenticator QR code">
                    <div class="key">{{enrol.SharedKey}}</div>
                    <form method="post" action="/account/mfa">{{AccountHtml.Hidden(t)}}
                      <label>Verification code</label>
                      <input name="code" inputmode="numeric" autocomplete="one-time-code" autofocus required>
                      <button class="ms-publish" type="submit">Verify &amp; enable</button>{{msg}}
                    </form>
                    <div class="links"><a href="/">Back to the app</a></div>
                    """;
            }

            return Html(AccountHtml.Shell("Two-factor", inner));
        }).RequireAuthorization();

        app.MapPost("/account/mfa", async (HttpContext ctx, IAntiforgery af, AccountService accounts, ISecurityAudit audit) =>
        {
            if (!await ValidAsync(ctx, af)) { return Results.Redirect("/account/mfa?error=1"); }
            string? userId = ctx.User.FindFirstValue(SecurityClaims.UserId);
            if (userId is null) { return Results.Redirect("/account/login"); }
            IFormCollection form = await ctx.Request.ReadFormAsync();

            (OperationResult Result, IReadOnlyList<string> RecoveryCodes) done = await accounts.CompleteMfaEnrollmentAsync(userId, form["code"].ToString());
            if (!done.Result.Succeeded) { return Results.Redirect("/account/mfa?error=1"); }

            await audit.RecordAsync("mfa.enabled", ctx.User.Identity?.Name, true);
            string codes = "<div class=\"codes\">" + string.Join("<br>", done.RecoveryCodes.Select(WebUtility.HtmlEncode)) + "</div>";
            string inner = AccountHtml.Brand("Two-factor enabled")
                + "<p class=\"ok\">Save these recovery codes somewhere safe — each works once if you lose your authenticator.</p>"
                + codes + "<div class=\"links\"><a href=\"/\">Back to the app</a></div>";
            return Html(AccountHtml.Shell("Recovery codes", inner));
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/account/mfa/disable", async (HttpContext ctx, IAntiforgery af, AccountService accounts, ISecurityAudit audit) =>
        {
            if (!await ValidAsync(ctx, af)) { return Results.Redirect("/account/mfa"); }
            string? userId = ctx.User.FindFirstValue(SecurityClaims.UserId);
            if (userId is not null)
            {
                await accounts.DisableMfaAsync(userId);
                await audit.RecordAsync("mfa.disabled", ctx.User.Identity?.Name, true);
            }

            return Results.Redirect("/account/mfa");
        }).RequireAuthorization().DisableAntiforgery();
    }

    private static async Task<bool> ValidAsync(HttpContext ctx, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(ctx);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static IResult Html(string content) => Results.Content(content, "text/html");

    private static string Absolute(HttpContext ctx, string path) => $"{ctx.Request.Scheme}://{ctx.Request.Host}{path}";
}

/// <summary>Shared HTML shell for the static account pages (matches the sign-in page's card styling).</summary>
internal static class AccountHtml
{
    public static string Brand(string subtitle) =>
        $"<div class=\"ms-brand\"><span class=\"glyph\">&#x21F2;</span><span>Mapping Studio</span></div><p class=\"sub\">{WebUtility.HtmlEncode(subtitle)}</p>";

    public static string Hidden(AntiforgeryTokenSet tokens) =>
        $"<input type=\"hidden\" name=\"{tokens.FormFieldName}\" value=\"{tokens.RequestToken}\">";

    public static string Shell(string title, string inner) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{WebUtility.HtmlEncode(title)}} — Mapping Studio</title>
          <link rel="stylesheet" href="/_content/App.UI/css/app.css">
          <style>
            body{align-items:center;justify-content:center}
            .card{background:var(--surface);border:1px solid var(--border);border-radius:12px;box-shadow:var(--shadow);padding:30px 32px;width:390px;max-width:92vw}
            .card .ms-brand{font-size:17px;margin-bottom:4px}
            .card .sub{color:var(--text-3);font-size:13px;margin:0 0 18px}
            .card label{display:block;font-size:12px;color:var(--text-2);font-weight:600;margin:12px 0 5px}
            .card input{width:100%;font:inherit;font-size:14px;padding:9px 11px;border:1px solid var(--border-strong);border-radius:8px;background:var(--surface);color:var(--text)}
            .card input:focus{outline:none;border-color:var(--accent);box-shadow:0 0 0 2px var(--accent-soft)}
            .card button{width:100%;margin-top:20px;padding:10px}
            .card .err{color:var(--err);font-size:13px;margin:14px 0 0}
            .card .ok{color:var(--ok);font-size:13px;margin:14px 0 0}
            .card .links{margin-top:16px;display:flex;justify-content:space-between;gap:10px;font-size:12px}
            .card .links a{color:var(--accent)}
            .qr{display:block;margin:12px auto;width:190px;height:190px;image-rendering:pixelated}
            .key{font-family:var(--font-mono);background:var(--surface-2);border:1px solid var(--border);border-radius:6px;padding:7px 8px;text-align:center;letter-spacing:1px;font-size:13px}
            .codes{font-family:var(--font-mono);font-size:13px;background:var(--surface-2);border:1px solid var(--border);border-radius:8px;padding:12px;margin-top:12px;line-height:1.9}
          </style>
        </head>
        <body>
          <div class="card">{{inner}}</div>
        </body>
        </html>
        """;
}
