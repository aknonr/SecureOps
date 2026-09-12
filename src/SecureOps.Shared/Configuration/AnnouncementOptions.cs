namespace SecureOps.Shared.Configuration;

/// <summary>Private local draft configuration. No SMTP destination or sending switch exists.</summary>
public sealed class AnnouncementOptions
{
    /// <summary>Default-off module activation, independent of external write gates.</summary>
    public bool Enabled { get; set; }
    /// <summary>Approved server-owned bare sender address.</summary>
    public string Sender { get; set; } = "";
    /// <summary>Private read-only directory outside application payloads.</summary>
    public string AssetDirectory { get; set; } = "";
    /// <summary>Immutable revision to flat PNG/JPEG filename allowlist.</summary>
    public Dictionary<string, string> Banners { get; set; } = [];
    /// <summary>Optional plain-text choice labels; absent/invalid labels fall back to revision.</summary>
    public Dictionary<string, string> BannerLabels { get; set; } = [];
}
