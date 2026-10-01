using System.Net;
using Microsoft.Extensions.Options;

namespace SecureOps.Ui.Hosting;

/// <summary>
/// Marks a backend HTTP request as HTTPS only when it matches the complete configured trust boundary.
/// </summary>
public sealed class HttpsOffloadMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _enabled;
    private readonly HashSet<IPAddress> _trustedProxyIps;
    private readonly HashSet<string> _expectedHosts;
    private readonly int _expectedLocalPort;

    /// <summary>
    /// Initializes the middleware from validated HTTPS offload options.
    /// </summary>
    /// <param name="next">The next request delegate.</param>
    /// <param name="options">Validated HTTPS offload options.</param>
    public HttpsOffloadMiddleware(RequestDelegate next, IOptions<HttpsOffloadOptions> options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);

        HttpsOffloadOptions value = options.Value;
        _next = next;
        _enabled = value.Enabled;
        _trustedProxyIps = value.TrustedProxyIps
            .Select(proxyIp => NormalizeAddress(IPAddress.Parse(proxyIp.Trim())))
            .ToHashSet();
        _expectedHosts = value.ExpectedHosts
            .Select(host => host.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _expectedLocalPort = value.ExpectedLocalPort;
    }

    /// <summary>
    /// Applies the trusted HTTPS scheme when every configured condition matches.
    /// </summary>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>A task representing request processing.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ShouldApplyHttps(context))
        {
            context.Request.Scheme = Uri.UriSchemeHttps;
        }

        return _next(context);
    }

    private bool ShouldApplyHttps(HttpContext context)
    {
        IPAddress? remoteAddress = context.Connection.RemoteIpAddress;
        return _enabled
            && string.Equals(context.Request.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && remoteAddress is not null
            && _trustedProxyIps.Contains(NormalizeAddress(remoteAddress))
            && _expectedHosts.Contains(context.Request.Host.Host)
            && context.Connection.LocalPort == _expectedLocalPort;
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}
