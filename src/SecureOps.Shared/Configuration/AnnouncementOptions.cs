namespace SecureOps.Shared.Configuration;

/// <summary>Private local draft configuration. No SMTP destination or sending switch exists.</summary>
public sealed class AnnouncementOptions
{
    /// <summary>Default-off module activation, independent of external write gates.</summary>
    public bool Enabled { get; set; }
    /// <summary>UI default for newly entered manual times only; never source timezone evidence.</summary>
    public string DefaultDisplayOffset { get; set; } = "+00:00";
    /// <summary>Legacy configuration retained for compatibility; new revisions use the actor's persisted Mail.</summary>
    public string Sender { get; set; } = "";
    /// <summary>Private read-only directory outside application payloads.</summary>
    public string AssetDirectory { get; set; } = "";
    /// <summary>Immutable revision to flat PNG/JPEG filename allowlist.</summary>
    public Dictionary<string, string> Banners { get; set; } = [];
    /// <summary>Optional plain-text choice labels; absent/invalid labels fall back to revision.</summary>
    public Dictionary<string, string> BannerLabels { get; set; } = [];
    /// <summary>Versioned final-presentation bundles; never SMTP or recipient defaults.</summary>
    public Dictionary<string, AnnouncementAssetBundle> Bundles { get; set; } = [];
}

/// <summary>Private approved presentation: six role-to-banner revisions and plain footer.</summary>
public sealed class AnnouncementAssetBundle
{
    /// <summary>Safe operator selection label.</summary>
    public string Label { get; set; } = "";
    /// <summary>Header, main, logo, linkedin, instagram and youtube; no remote URLs.</summary>
    public Dictionary<string, string> Assets { get; set; } = [];
    /// <summary>Version-bound plain text, never executable HTML.</summary>
    public string Footer { get; set; } = "";
}
