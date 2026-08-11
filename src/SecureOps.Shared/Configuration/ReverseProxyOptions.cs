namespace SecureOps.Shared.Configuration;

/// <summary>Configuration for forwarding headers received from explicitly trusted reverse proxies.</summary>
public sealed class ReverseProxyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>Forwarded-header settings.</summary>
    public ForwardedHeaderTrustOptions ForwardedHeaders { get; set; } = new();
}

/// <summary>Trusted reverse-proxy source settings.</summary>
public sealed class ForwardedHeaderTrustOptions
{
    /// <summary>Whether forwarded-header processing is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Exact trusted proxy IP addresses.</summary>
    public string[] TrustedProxyIps { get; set; } = [];
}
