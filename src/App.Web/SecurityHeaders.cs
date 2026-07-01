namespace App.Web;

/// <summary>
/// Baseline security response headers for every request. The CSP is strict — no inline scripts (the theme
/// bootstrap is externalized), only self-hosted scripts, framing denied — while allowing what the app
/// genuinely needs: inline styles (the design system + the static account pages use <c>style</c>
/// attributes), Google Fonts, <c>data:</c> images (the MFA QR), and same-origin SignalR connections.
/// </summary>
internal static class SecurityHeaders
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "img-src 'self' data:; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "connect-src 'self'; " +
        "form-action 'self'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            IHeaderDictionary headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-Frame-Options"] = "DENY";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            await next();
        });
}
