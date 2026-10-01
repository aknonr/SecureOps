using System.Net;
using Microsoft.Extensions.Options;

namespace SecureOps.Ui.Hosting;

/// <summary>
/// Validates the HTTPS offload trust boundary before the UI begins accepting requests.
/// </summary>
public sealed class HttpsOffloadOptionsValidator : IValidateOptions<HttpsOffloadOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, HttpsOffloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        ValidateProxyIps(options.TrustedProxyIps, failures);
        ValidateHosts(options.ExpectedHosts, failures);

        if (options.Enabled)
        {
            if (options.TrustedProxyIps.Count == 0)
            {
                failures.Add("ReverseProxy:HttpsOffload:TrustedProxyIps must contain at least one exact IP address when HTTPS offload is enabled.");
            }

            if (options.ExpectedHosts.Count == 0)
            {
                failures.Add("ReverseProxy:HttpsOffload:ExpectedHosts must contain at least one host when HTTPS offload is enabled.");
            }

            if (options.ExpectedLocalPort is < 1 or > 65535)
            {
                failures.Add("ReverseProxy:HttpsOffload:ExpectedLocalPort must be between 1 and 65535 when HTTPS offload is enabled.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateProxyIps(IEnumerable<string> proxyIps, ICollection<string> failures)
    {
        foreach (string proxyIp in proxyIps)
        {
            if (string.IsNullOrWhiteSpace(proxyIp) || !IPAddress.TryParse(proxyIp.Trim(), out _))
            {
                failures.Add($"ReverseProxy:HttpsOffload:TrustedProxyIps contains an invalid IP address: '{proxyIp}'.");
            }
        }
    }

    private static void ValidateHosts(IEnumerable<string> hosts, ICollection<string> failures)
    {
        foreach (string host in hosts)
        {
            string candidate = host?.Trim() ?? string.Empty;
            if (candidate.Length == 0 || Uri.CheckHostName(candidate) == UriHostNameType.Unknown)
            {
                failures.Add($"ReverseProxy:HttpsOffload:ExpectedHosts contains an invalid host: '{host}'.");
            }
        }
    }
}
