using Microsoft.Extensions.Primitives;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Api.Security;

/// <summary>Immutable, fail-closed source-origin and request-intent policy.</summary>
public sealed class ApiCsrfPolicy
{
    private readonly HashSet<string> _origins = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Validates the configured exact origins; no implicit host or proxy trust.</summary>
    public ApiCsrfPolicy(IConfiguration configuration)
    {
        string[] values = configuration.GetSection("ApiCsrf:AllowedOrigins").Get<string[]>() ?? [];
        foreach (string value in values)
        {
            if (!TryOrigin(value, false, out string? origin))
            {
                throw new InvalidOperationException("ApiCsrf:AllowedOrigins must contain exact HTTP(S) origins without credentials, paths, query, fragment or wildcards.");
            }

            _origins.Add(origin!);
        }
    }

    /// <summary>Methods excluded from the guard. All other methods require explicit intent.</summary>
    public static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    /// <summary>Returns a bounded denial reason, or null when the request passes.</summary>
    public string? RejectionReason(HttpRequest request)
    {
        if (IsSafeMethod(request.Method))
        {
            return null;
        }

        if (request.Headers.TryGetValue("Sec-Fetch-Site", out StringValues site))
        {
            if (site.Count != 1 || site[0] is not ("same-origin" or "same-site" or "none" or "cross-site"))
            {
                return "FetchMetadataInvalid";
            }

            if (site[0] == "cross-site")
            {
                return "CrossSite";
            }
        }

        if (!request.Headers.TryGetValue(ApiCsrf.HeaderName, out StringValues intent)
            || intent.Count != 1 || intent[0] != ApiCsrf.HeaderValue)
        {
            return "IntentHeaderMissingOrInvalid";
        }

        if (request.Headers.TryGetValue("Origin", out StringValues origin))
        {
            return ValidateSource(origin, false);
        }

        if (request.Headers.TryGetValue("Referer", out StringValues referer))
        {
            return ValidateSource(referer, true);
        }

        return request.Headers.Keys.Any(key => key.StartsWith("Sec-Fetch-", StringComparison.OrdinalIgnoreCase))
            ? "SourceOriginMissing"
            : null;
    }

    private string? ValidateSource(StringValues values, bool allowPath)
    {
        if (values.Count != 1 || !TryOrigin(values[0], allowPath, out string? origin))
        {
            return "SourceOriginInvalid";
        }

        return _origins.Contains(origin!) ? null : "SourceOriginNotAllowed";
    }

    private static bool TryOrigin(string? value, bool allowPath, out string? origin)
    {
        origin = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096
            || value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '\\' or '*')
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !uri.IsWellFormedOriginalString()
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        if (!allowPath && (!string.IsNullOrEmpty(uri.Query)
            || !string.Equals(value, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        origin = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        return true;
    }
}
