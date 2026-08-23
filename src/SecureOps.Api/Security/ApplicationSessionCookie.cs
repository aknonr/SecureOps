using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Api.Security;

/// <summary>Protects and manages the opaque browser-session handle.</summary>
public sealed class ApplicationSessionCookie
{
    private const string Purpose = "SecureOps.ApplicationSession.Cookie.v1";
    private readonly IDataProtector _protector;
    private readonly SessionSecurityOptions _options;

    /// <summary>Initializes the purpose-isolated cookie protector.</summary>
    public ApplicationSessionCookie(IDataProtectionProvider provider, IOptions<SessionSecurityOptions> options)
    {
        _protector = provider.CreateProtector(Purpose);
        _options = options.Value;
    }

    /// <summary>Reads and validates the protected handle.</summary>
    public ApplicationSessionCookieReadResult Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(_options.CookieName, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            return new ApplicationSessionCookieReadResult(ApplicationSessionCookieStatus.Missing, null);
        }

        if (value.Length > 1024)
        {
            return new ApplicationSessionCookieReadResult(ApplicationSessionCookieStatus.Invalid, null);
        }

        try
        {
            return Guid.TryParseExact(_protector.Unprotect(value), "N", out Guid sessionId) && sessionId != Guid.Empty
                ? new ApplicationSessionCookieReadResult(ApplicationSessionCookieStatus.Valid, sessionId)
                : new ApplicationSessionCookieReadResult(ApplicationSessionCookieStatus.Invalid, null);
        }
        catch (CryptographicException)
        {
            return new ApplicationSessionCookieReadResult(ApplicationSessionCookieStatus.Invalid, null);
        }
    }

    /// <summary>Writes a browser-session-only protected handle.</summary>
    public void Write(HttpResponse response, Guid sessionId) => response.Cookies.Append(
        _options.CookieName,
        _protector.Protect(sessionId.ToString("N")),
        CookieOptions());

    /// <summary>Clears the protected handle.</summary>
    public void Delete(HttpResponse response) => response.Cookies.Delete(_options.CookieName, CookieOptions());

    private CookieOptions CookieOptions() => new()
    {
        Secure = _options.SecureCookie,
        HttpOnly = _options.HttpOnly,
        SameSite = Enum.Parse<SameSiteMode>(_options.SameSite, true),
        Path = "/",
        IsEssential = true
    };
}

/// <summary>Protected application-session cookie read result.</summary>
public sealed record ApplicationSessionCookieReadResult(ApplicationSessionCookieStatus Status, Guid? SessionId);

/// <summary>Protected cookie state.</summary>
public enum ApplicationSessionCookieStatus
{
    /// <summary>No session handle was supplied.</summary>
    Missing,
    /// <summary>The handle was successfully unprotected.</summary>
    Valid,
    /// <summary>The handle was malformed, tampered, or protected for another purpose/application.</summary>
    Invalid
}
