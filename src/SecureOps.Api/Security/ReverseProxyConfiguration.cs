using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using SecureOps.Shared.Configuration;

namespace SecureOps.Api.Security;

/// <summary>Validates and applies explicit reverse-proxy trust settings.</summary>
public static class ReverseProxyConfiguration
{
    /// <summary>Validates explicit trusted reverse-proxy settings.</summary>
    /// <param name="configuration">Application configuration.</param>
    public static void Validate(IConfiguration configuration)
    {
        ReverseProxyOptions options = new();
        configuration.GetSection(ReverseProxyOptions.SectionName).Bind(options);
        if (!options.ForwardedHeaders.Enabled)
        {
            return;
        }

        if (options.ForwardedHeaders.TrustedProxyIps.Length == 0)
        {
            throw new InvalidOperationException("ReverseProxy:ForwardedHeaders:TrustedProxyIps is required when forwarding is enabled.");
        }

        foreach (string value in options.ForwardedHeaders.TrustedProxyIps)
        {
            if (!IPAddress.TryParse(value, out _))
            {
                throw new InvalidOperationException("ReverseProxy:ForwardedHeaders:TrustedProxyIps contains an invalid IP address.");
            }
        }
    }

    /// <summary>Applies trusted reverse-proxy settings to ASP.NET Core forwarding options.</summary>
    /// <param name="options">Forwarded-header options.</param>
    /// <param name="configuration">Application configuration.</param>
    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        ReverseProxyOptions settings = new();
        configuration.GetSection(ReverseProxyOptions.SectionName).Bind(settings);
        options.ForwardedHeaders = settings.ForwardedHeaders.Enabled
            ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            : ForwardedHeaders.None;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (string value in settings.ForwardedHeaders.TrustedProxyIps)
        {
            options.KnownProxies.Add(IPAddress.Parse(value));
        }
    }
}
