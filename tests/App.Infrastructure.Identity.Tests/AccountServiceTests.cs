using System.Net;
using System.Security.Cryptography;
using App.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Identity.Tests;

public sealed class AccountServiceTests
{
    [Fact]
    public async Task Register_creates_a_reader_awaiting_email_confirmation_then_confirms()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            AccountService accounts = sp.GetRequiredService<AccountService>();
            (OperationResult Result, string? UserId, string? Token) reg =
                await accounts.RegisterAsync("newbie", "newbie@x.io", "Newbie!Passw0rd1");
            Assert.True(reg.Result.Succeeded, string.Join(";", reg.Result.Errors));

            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            AppUser user = (await users.FindByIdAsync(reg.UserId!))!;
            Assert.False(await users.IsEmailConfirmedAsync(user));
            Assert.Contains(SecurityRoles.Reader, await users.GetRolesAsync(user));

            Assert.True((await accounts.ConfirmEmailAsync(reg.UserId!, reg.Token!)).Succeeded);
            Assert.True(await users.IsEmailConfirmedAsync((await users.FindByIdAsync(reg.UserId!))!));
            return true;
        });
    }

    [Fact]
    public async Task Password_reset_issues_a_token_only_for_a_known_account_and_resets()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            IUserDirectory directory = sp.GetRequiredService<IUserDirectory>();
            Assert.True((await directory.CreateUserAsync(new CreateUserRequest("erin", "erin@x.io", "Erin", "Erin!Passw0rd12", [SecurityRoles.Reader]))).Succeeded);

            AccountService accounts = sp.GetRequiredService<AccountService>();
            Assert.Null(await accounts.GeneratePasswordResetAsync("nobody@x.io")); // enumeration-safe

            (AppUser User, string Token)? ticket = await accounts.GeneratePasswordResetAsync("erin@x.io");
            Assert.NotNull(ticket);
            Assert.True((await accounts.ResetPasswordAsync(ticket!.Value.User.Id.ToString(), ticket.Value.Token, "Erin!Newpass0rd1")).Succeeded);

            UserManager<AppUser> users = sp.GetRequiredService<UserManager<AppUser>>();
            Assert.True(await users.CheckPasswordAsync((await users.FindByEmailAsync("erin@x.io"))!, "Erin!Newpass0rd1"));
            return true;
        });
    }

    [Fact]
    public async Task Change_password_requires_the_current_password()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            IUserDirectory directory = sp.GetRequiredService<IUserDirectory>();
            await directory.CreateUserAsync(new CreateUserRequest("frank", "frank@x.io", "Frank", "Frank!Passw0rd1", [SecurityRoles.Reader]));
            string id = (await directory.ListUsersAsync("frank")).Single().Id;

            AccountService accounts = sp.GetRequiredService<AccountService>();
            Assert.False((await accounts.ChangePasswordAsync(id, "wrong-current", "Frank!Newpass0rd1")).Succeeded);
            Assert.True((await accounts.ChangePasswordAsync(id, "Frank!Passw0rd1", "Frank!Newpass0rd1")).Succeeded);
            return true;
        });
    }

    [Fact]
    public async Task Mfa_enrolment_enables_two_factor_with_a_valid_code_and_returns_recovery_codes()
    {
        await using SecurityTestHost host = new();
        await host.SeedAsync();

        await host.InScopeAsync(async sp =>
        {
            IUserDirectory directory = sp.GetRequiredService<IUserDirectory>();
            await directory.CreateUserAsync(new CreateUserRequest("gwen", "gwen@x.io", "Gwen", "Gwen!Passw0rd12", [SecurityRoles.Editor]));
            string id = (await directory.ListUsersAsync("gwen")).Single().Id;

            AccountService accounts = sp.GetRequiredService<AccountService>();
            MfaEnrollment enrol = await accounts.BeginMfaEnrollmentAsync(id);
            Assert.StartsWith("otpauth://totp/", enrol.AuthenticatorUri);
            Assert.StartsWith("data:image/png;base64,", enrol.QrPngDataUri);

            // A wrong code is rejected.
            Assert.False((await accounts.CompleteMfaEnrollmentAsync(id, "000000")).Result.Succeeded);

            // A real TOTP computed from the shared key is accepted.
            string code = Totp(enrol.SharedKey.Replace(" ", string.Empty).ToUpperInvariant());
            (OperationResult Result, IReadOnlyList<string> RecoveryCodes) done = await accounts.CompleteMfaEnrollmentAsync(id, code);
            Assert.True(done.Result.Succeeded, string.Join(";", done.Result.Errors));
            Assert.NotEmpty(done.RecoveryCodes);
            Assert.True(await accounts.IsMfaEnabledAsync(id));

            Assert.True((await accounts.DisableMfaAsync(id)).Succeeded);
            Assert.False(await accounts.IsMfaEnabledAsync(id));
            return true;
        });
    }

    [Fact]
    public async Task Email_sender_falls_back_to_the_log_sender_when_smtp_is_not_configured()
    {
        await using SecurityTestHost noSmtp = new();
        await noSmtp.SeedAsync();
        await noSmtp.InScopeAsync(sp =>
        {
            Assert.False(sp.GetRequiredService<IAppEmailSender>().IsConfigured);
            return Task.FromResult(true);
        });

        await using SecurityTestHost withSmtp = new(s => s["Auth:Email:Host"] = "smtp.example.com");
        await withSmtp.SeedAsync();
        await withSmtp.InScopeAsync(sp =>
        {
            Assert.True(sp.GetRequiredService<IAppEmailSender>().IsConfigured);
            return Task.FromResult(true);
        });
    }

    // Standard RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30s) over a base32 secret — matches the authenticator.
    private static string Totp(string base32Secret)
    {
        byte[] key = Base32Decode(base32Secret);
        long counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        byte[] message = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(counter));
        byte[] hash = HMACSHA1.HashData(key, message);
        int offset = hash[^1] & 0x0f;
        int binary = ((hash[offset] & 0x7f) << 24) | ((hash[offset + 1] & 0xff) << 16) | ((hash[offset + 2] & 0xff) << 8) | (hash[offset + 3] & 0xff);
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        List<byte> bytes = [];
        int buffer = 0, bits = 0;
        foreach (char c in input)
        {
            int value = alphabet.IndexOf(c);
            if (value < 0) { continue; }
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xff));
            }
        }

        return [.. bytes];
    }
}
