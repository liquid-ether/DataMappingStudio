using App.Application.Security;

namespace App.Web;

/// <summary>
/// Restricts a signed-in user whose session is marked <see cref="SecurityClaims.MfaPending"/> (policy
/// requires two-factor but they haven't enrolled) to the account pages until enrolment completes. The
/// claim is dropped by re-issuing the cookie after successful enrolment.
/// </summary>
internal static class MfaEnforcement
{
    public static IApplicationBuilder UseMfaEnforcement(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true
                && context.User.HasClaim(c => c.Type == SecurityClaims.MfaPending)
                && !IsExempt(context.Request.Path))
            {
                context.Response.Redirect("/account/mfa?required=1");
                return;
            }

            await next();
        });

    private static bool IsExempt(PathString path) =>
        path.StartsWithSegments("/account")
        || path.StartsWithSegments("/health")
        || path.StartsWithSegments("/_content")
        || path.StartsWithSegments("/_framework")
        || path.StartsWithSegments("/js")
        || path.StartsWithSegments("/css");
}
