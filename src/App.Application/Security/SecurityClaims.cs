namespace App.Application.Security;

/// <summary>Claim types carried on the authenticated principal (beyond name + roles).</summary>
public static class SecurityClaims
{
    /// <summary>Stable user id — keys the workspace and attributes changes.</summary>
    public const string UserId = "uid";

    /// <summary>Friendly display name.</summary>
    public const string DisplayName = "display_name";

    /// <summary>
    /// Present when policy requires two-factor for this user but they haven't enrolled yet — the host
    /// restricts such a session to the account pages until enrolment completes.
    /// </summary>
    public const string MfaPending = "mfa_pending";
}
