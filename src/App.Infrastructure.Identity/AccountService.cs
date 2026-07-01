using App.Application.Security;
using Microsoft.AspNetCore.Identity;

namespace App.Infrastructure.Identity;

/// <summary>
/// Self-service + MFA operations over ASP.NET Core Identity: registration + email confirmation, password
/// reset / change, invitations, and TOTP two-factor enrolment. Token generation is separated from email
/// delivery so the flows are unit-testable without SMTP (the endpoints send the emails).
/// </summary>
/// <summary>The data an authenticator-app enrolment page needs: the manual key, the otpauth URI, and a QR.</summary>
public sealed record MfaEnrollment(string SharedKey, string AuthenticatorUri, string QrPngDataUri);

public sealed class AccountService(
    UserManager<AppUser> users,
    RoleManager<AppRole> roles)
{
    private const string Issuer = "Mapping Studio";

    // ---------- registration + email confirmation ----------

    public async Task<(OperationResult Result, string? UserId, string? Token)> RegisterAsync(string userName, string email, string password)
    {
        AppUser user = new()
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = false,
            DisplayName = userName,
            Origin = "Local",
            IsEnabled = true,
        };

        IdentityResult created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return (ToResult(created), null, null);
        }

        await AddRoleIfExistsAsync(user, SecurityRoles.Reader); // new sign-ups start as Reader
        string token = await users.GenerateEmailConfirmationTokenAsync(user);
        return (OperationResult.Ok, user.Id.ToString(), token);
    }

    public async Task<OperationResult> ConfirmEmailAsync(string userId, string token)
    {
        AppUser? user = await users.FindByIdAsync(userId);
        return user is null ? OperationResult.Fail("Invalid confirmation link.") : ToResult(await users.ConfirmEmailAsync(user, token));
    }

    // ---------- password reset / change ----------

    /// <summary>Generates a reset token for the email, or null if there is no eligible account (enumeration-safe).</summary>
    public async Task<(AppUser User, string Token)?> GeneratePasswordResetAsync(string email)
    {
        AppUser? user = await users.FindByEmailAsync(email);
        return user is { IsEnabled: true } ? (user, await users.GeneratePasswordResetTokenAsync(user)) : null;
    }

    public async Task<OperationResult> ResetPasswordAsync(string userId, string token, string newPassword)
    {
        AppUser? user = await users.FindByIdAsync(userId);
        return user is null ? OperationResult.Fail("Invalid reset link.") : ToResult(await users.ResetPasswordAsync(user, token, newPassword));
    }

    public async Task<OperationResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        AppUser? user = await users.FindByIdAsync(userId);
        return user is null ? OperationResult.Fail("User not found.") : ToResult(await users.ChangePasswordAsync(user, currentPassword, newPassword));
    }

    // ---------- MFA (TOTP) ----------

    public async Task<bool> IsMfaEnabledAsync(string userId)
        => await users.FindByIdAsync(userId) is { } user && await users.GetTwoFactorEnabledAsync(user);

    /// <summary>Ensures an authenticator key and returns the shared key + a scannable QR (PNG data URI).</summary>
    public async Task<MfaEnrollment> BeginMfaEnrollmentAsync(string userId)
    {
        AppUser user = await users.FindByIdAsync(userId) ?? throw new InvalidOperationException("User not found.");

        string? key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
        }

        string label = Uri.EscapeDataString(user.Email ?? user.UserName ?? "user");
        string issuer = Uri.EscapeDataString(Issuer);
        string uri = $"otpauth://totp/{issuer}:{label}?secret={key}&issuer={issuer}&digits=6";

        using QRCoder.QRCodeGenerator generator = new();
        using QRCoder.QRCodeData data = generator.CreateQrCode(uri, QRCoder.QRCodeGenerator.ECCLevel.Q);
        byte[] png = new QRCoder.PngByteQRCode(data).GetGraphic(6);
        string qr = "data:image/png;base64," + Convert.ToBase64String(png);

        return new MfaEnrollment(FormatKey(key!), uri, qr);
    }

    /// <summary>Verifies a TOTP code and, on success, enables 2FA and returns fresh recovery codes.</summary>
    public async Task<(OperationResult Result, IReadOnlyList<string> RecoveryCodes)> CompleteMfaEnrollmentAsync(string userId, string code)
    {
        AppUser? user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return (OperationResult.Fail("User not found."), []);
        }

        string normalized = code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        bool valid = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, normalized);
        if (!valid)
        {
            return (OperationResult.Fail("That code is invalid or expired."), []);
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        IEnumerable<string>? codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return (OperationResult.Ok, codes?.ToList() ?? []);
    }

    public async Task<OperationResult> DisableMfaAsync(string userId)
    {
        AppUser? user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return OperationResult.Fail("User not found.");
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        return OperationResult.Ok;
    }

    private async Task AddRoleIfExistsAsync(AppUser user, string role)
    {
        if (!string.IsNullOrWhiteSpace(role) && await roles.RoleExistsAsync(role))
        {
            await users.AddToRoleAsync(user, role);
        }
    }

    private static string FormatKey(string key)
    {
        List<string> groups = [];
        for (int i = 0; i < key.Length; i += 4)
        {
            groups.Add(key.Substring(i, Math.Min(4, key.Length - i)));
        }

        return string.Join(" ", groups).ToLowerInvariant();
    }

    private static OperationResult ToResult(IdentityResult result)
        => result.Succeeded ? OperationResult.Ok : OperationResult.Fail(result.Errors.Select(e => e.Description).ToArray());
}
