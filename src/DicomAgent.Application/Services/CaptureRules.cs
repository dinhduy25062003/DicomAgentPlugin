using System.Text.RegularExpressions;
using DicomAgent.Application.Models;

namespace DicomAgent.Application.Services;

public static partial class CaptureRules
{
    [GeneratedRegex("^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$")]
    private static partial Regex TenantPattern();
    public static string Tenant(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !TenantPattern().IsMatch(value)) throw new ArgumentException("TenantName must be a single DNS label.");
        return value.ToLowerInvariant();
    }
    public static Uri Server(LocalApiOptions options, string tenant)
    {
        var value = options.SaasBaseUrlTemplate.Replace("{TenantName}", Tenant(tenant), StringComparison.Ordinal);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("SaaS URL must be an HTTPS origin, without /#/ or a path (HTTP loopback is allowed for testing).");
        return uri;
    }
    public static bool OriginAllowed(LocalApiOptions options, string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != origin) return false;
        if (options.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;
        // The production web and API share the configured tenant origin.
        var tenant = uri.Host.Split('.')[0];
        try { return Server(options, tenant).GetLeftPart(UriPartial.Authority).Equals(origin, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }
    public static void Validate(LocalApiOptions options)
    {
        if (options.Port is < 1024 or > 65535) throw new ArgumentException("Local API port must be between 1024 and 65535.");
        _ = Server(options, "demo");
        foreach (var origin in options.AllowedOrigins)
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != origin ||
                (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
                throw new ArgumentException("Allowed origins must be HTTPS origins or HTTP loopback origins, without a trailing slash.");
    }
    public static void Input(CaptureInput input)
    {
        if (input.ResId == Guid.Empty || new[] { input.ResModel, input.EntityName, input.ModelName, input.ServiceName, input.PartnerId, input.PartnerName }
            .Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 512 || x.Any(char.IsControl)))
            throw new ArgumentException("resId must be a non-empty UUID; all request fields are required (maximum 512 characters each).");
    }
    public static void Authorization(string value)
    {
        if (!System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(value, out var auth) ||
            !auth.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(auth.Parameter) || value.Length > 16384)
            throw new ArgumentException("Authorization must contain a Bearer token.");
    }
}
