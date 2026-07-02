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
            bool fr = AccountLang.IsFrench(ctx);
            string T(string en, string f) => fr ? f : en;
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string msg = sent == true ? $"<p class=\"ok\">{T("If that account exists, a reset link has been sent.", "Si ce compte existe, un lien de réinitialisation a été envoyé.")}</p>" : "";
            return Html(AccountHtml.Shell(T("Reset password", "Réinitialiser le mot de passe"), AccountHtml.Brand(T("Reset your password", "Réinitialisez votre mot de passe")) + $$"""
                <form method="post" action="/account/forgot">{{AccountHtml.Hidden(t)}}
                  <label>{{T("Email", "Courriel")}}</label>
                  <input name="email" type="email" autocomplete="email" autofocus required>
                  <button class="ms-publish" type="submit">{{T("Send reset link", "Envoyer le lien")}}</button>{{msg}}
                  <div class="links"><a href="/account/login">{{T("Back to sign in", "Retour à la connexion")}}</a></div>
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
        }).DisableAntiforgery().RequireRateLimiting("auth");

        account.MapGet("/reset", (HttpContext ctx, IAntiforgery af, string userId, string token, int? error) =>
        {
            bool fr = AccountLang.IsFrench(ctx);
            string T(string en, string f) => fr ? f : en;
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string msg = error == 1 ? $"<p class=\"err\">{T("Could not reset — the link may have expired.", "Échec de la réinitialisation — le lien a peut-être expiré.")}</p>" : "";
            return Html(AccountHtml.Shell(T("Set a new password", "Définir un nouveau mot de passe"), AccountHtml.Brand(T("Choose a new password", "Choisissez un nouveau mot de passe")) + $$"""
                <form method="post" action="/account/reset">{{AccountHtml.Hidden(t)}}
                  <input type="hidden" name="userId" value="{{WebUtility.HtmlEncode(userId)}}">
                  <input type="hidden" name="token" value="{{WebUtility.HtmlEncode(token)}}">
                  <label>{{T("New password", "Nouveau mot de passe")}}</label>
                  <input name="password" type="password" autocomplete="new-password" autofocus required>
                  <button class="ms-publish" type="submit">{{T("Set password", "Définir le mot de passe")}}</button>{{msg}}
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
        }).DisableAntiforgery().RequireRateLimiting("auth");

        account.MapGet("/confirm", async (HttpContext ctx, AccountService accounts, string userId, string token) =>
        {
            bool fr = AccountLang.IsFrench(ctx);
            string T(string en, string f) => fr ? f : en;
            OperationResult result = await accounts.ConfirmEmailAsync(userId, token);
            string body = AccountHtml.Brand(result.Succeeded ? T("Email confirmed", "Courriel confirmé") : T("Confirmation failed", "Échec de la confirmation"))
                + (result.Succeeded
                    ? $"<p class=\"ok\">{T("Your email is confirmed — you can sign in.", "Votre courriel est confirmé — vous pouvez vous connecter.")}</p>"
                    : $"<p class=\"err\">{T("This confirmation link is invalid or has expired.", "Ce lien de confirmation est invalide ou expiré.")}</p>")
                + $"<div class=\"links\"><a href=\"/account/login\">{T("Go to sign in", "Aller à la connexion")}</a></div>";
            return Html(AccountHtml.Shell(T("Confirm email", "Confirmer le courriel"), body));
        });

        account.MapGet("/register", (HttpContext ctx, IAntiforgery af, IOptions<SecurityOptions> options, int? error) =>
        {
            if (!options.Value.Providers.Local.AllowSelfRegistration) { return Results.NotFound(); }
            bool fr = AccountLang.IsFrench(ctx);
            string T(string en, string f) => fr ? f : en;
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string msg = error == 1 ? $"<p class=\"err\">{T("Could not create the account. The username or email may be taken, or the password too weak.", "Impossible de créer le compte. L'identifiant ou le courriel est peut-être déjà pris, ou le mot de passe trop faible.")}</p>" : "";
            return Html(AccountHtml.Shell(T("Create account", "Créer un compte"), AccountHtml.Brand(T("Create your account", "Créez votre compte")) + $$"""
                <form method="post" action="/account/register">{{AccountHtml.Hidden(t)}}
                  <label>{{T("Username", "Identifiant")}}</label><input name="username" autocomplete="username" autofocus required>
                  <label>{{T("Email", "Courriel")}}</label><input name="email" type="email" autocomplete="email" required>
                  <label>{{T("Password", "Mot de passe")}}</label><input name="password" type="password" autocomplete="new-password" required>
                  <button class="ms-publish" type="submit">{{T("Create account", "Créer le compte")}}</button>{{msg}}
                  <div class="links"><a href="/account/login">{{T("Back to sign in", "Retour à la connexion")}}</a></div>
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
        }).DisableAntiforgery().RequireRateLimiting("auth");

        // Two-factor login step (holds the partial 2FA cookie set by the password step).
        account.MapGet("/2fa", (HttpContext ctx, IAntiforgery af, string? returnUrl, int? error) =>
        {
            bool fr = AccountLang.IsFrench(ctx);
            string T(string en, string f) => fr ? f : en;
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string msg = error == 1 ? $"<p class=\"err\">{T("Invalid code. Try again, or use a recovery code.", "Code invalide. Réessayez ou utilisez un code de récupération.")}</p>" : "";
            return Html(AccountHtml.Shell(T("Two-factor", "Deux facteurs"), AccountHtml.Brand(T("Two-factor authentication", "Authentification à deux facteurs")) + $$"""
                <form method="post" action="/account/2fa">{{AccountHtml.Hidden(t)}}
                  <input type="hidden" name="returnUrl" value="{{WebUtility.HtmlEncode(returnUrl ?? "/")}}">
                  <label>{{T("Authenticator or recovery code", "Code d'authentification ou de récupération")}}</label>
                  <input name="code" inputmode="numeric" autocomplete="one-time-code" autofocus required>
                  <button class="ms-publish" type="submit">{{T("Verify", "Vérifier")}}</button>{{msg}}
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
        }).DisableAntiforgery().RequireRateLimiting("auth");
    }

    // ------------------------- authenticated flows -------------------------
    // Mapped on the app (not the anonymous group) so the fallback policy requires a signed-in user.

    public static void MapSelfServiceAuthenticatedEndpoints(this WebApplication app)
    {
        app.MapGet("/account/password", (HttpContext ctx, IAntiforgery af, int? error, bool? changed) =>
        {
            bool fr = AccountLang.IsFrench(ctx);
            string T(string en, string f) => fr ? f : en;
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string msg = error == 1 ? $"<p class=\"err\">{T("Current password is incorrect, or the new one is invalid.", "Le mot de passe actuel est incorrect, ou le nouveau est invalide.")}</p>"
                : changed == true ? $"<p class=\"ok\">{T("Password changed.", "Mot de passe changé.")}</p>" : "";
            return Html(AccountHtml.Shell(T("Change password", "Changer le mot de passe"), AccountHtml.Brand(T("Change your password", "Changez votre mot de passe")) + $$"""
                <form method="post" action="/account/password">{{AccountHtml.Hidden(t)}}
                  <label>{{T("Current password", "Mot de passe actuel")}}</label><input name="current" type="password" autocomplete="current-password" autofocus required>
                  <label>{{T("New password", "Nouveau mot de passe")}}</label><input name="password" type="password" autocomplete="new-password" required>
                  <button class="ms-publish" type="submit">{{T("Change password", "Changer le mot de passe")}}</button>{{msg}}
                  <div class="links"><a href="/">{{T("Back to the app", "Retour à l'application")}}</a><a href="/account/mfa">{{T("Two-factor", "Deux facteurs")}}</a></div>
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

        app.MapGet("/account/mfa", async (HttpContext ctx, IAntiforgery af, AccountService accounts, int? error, int? required) =>
        {
            bool fr = AccountLang.IsFrench(ctx);
            string T(string en, string f) => fr ? f : en;
            string userId = ctx.User.FindFirstValue(SecurityClaims.UserId)!;
            AntiforgeryTokenSet t = af.GetAndStoreTokens(ctx);
            string requiredBanner = required == 1
                ? $"<p class=\"err\">{T("Your role requires two-factor authentication — set it up to continue.", "Votre rôle exige l'authentification à deux facteurs — configurez-la pour continuer.")}</p>"
                : "";
            string backLink = $"<div class=\"links\"><a href=\"/\">{T("Back to the app", "Retour à l'application")}</a></div>";
            string inner;
            if (await accounts.IsMfaEnabledAsync(userId))
            {
                inner = AccountHtml.Brand(T("Two-factor authentication", "Authentification à deux facteurs"))
                    + $"<p class=\"ok\">{T("Two-factor authentication is enabled on your account.", "L'authentification à deux facteurs est activée sur votre compte.")}</p>"
                    + $$"""
                    <form method="post" action="/account/mfa/disable">{{AccountHtml.Hidden(t)}}
                      <button class="add" type="submit">{{T("Disable two-factor", "Désactiver les deux facteurs")}}</button>
                    </form>
                    """ + backLink;
            }
            else
            {
                MfaEnrollment enrol = await accounts.BeginMfaEnrollmentAsync(userId);
                string msg = error == 1 ? $"<p class=\"err\">{T("That code was invalid. Try again.", "Ce code était invalide. Réessayez.")}</p>" : "";
                inner = AccountHtml.Brand(T("Set up two-factor authentication", "Configurer l'authentification à deux facteurs")) + requiredBanner + $$"""
                    <p class="sub">{{T("Scan the QR with an authenticator app (or type the key), then enter a code to confirm.", "Scannez le QR avec une application d'authentification (ou saisissez la clé), puis entrez un code pour confirmer.")}}</p>
                    <img class="qr" src="{{enrol.QrPngDataUri}}" alt="Authenticator QR code">
                    <div class="key">{{enrol.SharedKey}}</div>
                    <form method="post" action="/account/mfa">{{AccountHtml.Hidden(t)}}
                      <label>{{T("Verification code", "Code de vérification")}}</label>
                      <input name="code" inputmode="numeric" autocomplete="one-time-code" autofocus required>
                      <button class="ms-publish" type="submit">{{T("Verify &amp; enable", "Vérifier et activer")}}</button>{{msg}}
                    </form>
                    """ + backLink;
            }

            return Html(AccountHtml.Shell(T("Two-factor", "Deux facteurs"), inner));
        }).RequireAuthorization();

        app.MapPost("/account/mfa", async (HttpContext ctx, IAntiforgery af, AccountService accounts,
            SignInManager<AppUser> signIn, UserManager<AppUser> users, ISecurityAudit audit) =>
        {
            if (!await ValidAsync(ctx, af)) { return Results.Redirect("/account/mfa?error=1"); }
            string? userId = ctx.User.FindFirstValue(SecurityClaims.UserId);
            if (userId is null) { return Results.Redirect("/account/login"); }
            IFormCollection form = await ctx.Request.ReadFormAsync();

            (OperationResult Result, IReadOnlyList<string> RecoveryCodes) done = await accounts.CompleteMfaEnrollmentAsync(userId, form["code"].ToString());
            if (!done.Result.Succeeded) { return Results.Redirect("/account/mfa?error=1"); }

            // Re-issue the cookie: with 2FA now enabled the claims factory drops the mfa_pending marker.
            if (await users.FindByIdAsync(userId) is { } enrolled)
            {
                await signIn.RefreshSignInAsync(enrolled);
            }

            await audit.RecordAsync("mfa.enabled", ctx.User.Identity?.Name, true);
            bool fr = AccountLang.IsFrench(ctx);
            string T(string en, string f) => fr ? f : en;
            string codes = "<div class=\"codes\">" + string.Join("<br>", done.RecoveryCodes.Select(WebUtility.HtmlEncode)) + "</div>";
            string inner = AccountHtml.Brand(T("Two-factor enabled", "Deux facteurs activés"))
                + $"<p class=\"ok\">{T("Save these recovery codes somewhere safe — each works once if you lose your authenticator.", "Conservez ces codes de récupération en lieu sûr — chacun fonctionne une seule fois si vous perdez votre authentificateur.")}</p>"
                + codes + $"<div class=\"links\"><a href=\"/\">{T("Back to the app", "Retour à l'application")}</a></div>";
            return Html(AccountHtml.Shell(T("Recovery codes", "Codes de récupération"), inner));
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
