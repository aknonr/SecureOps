namespace SecureOps.Ui.Hosting;

/// <summary>
/// Controls trusted HTTPS offload recognition for requests received over a cleartext backend hop.
/// </summary>
public sealed class HttpsOffloadOptions
{
    /// <summary>
    /// Configuration section containing HTTPS offload settings.
    /// </summary>
    public const string SectionName = "ReverseProxy:HttpsOffload";

    /// <summary>
    /// Gets or sets whether HTTPS offload recognition is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the exact proxy IP addresses trusted to deliver offloaded HTTPS requests.
    /// </summary>
    public List<string> TrustedProxyIps { get; set; } = [];

    /// <summary>
    /// Gets or sets the exact external hosts accepted for offloaded HTTPS requests.
    /// </summary>
    public List<string> ExpectedHosts { get; set; } = [];

    /// <summary>
    /// Gets or sets the local cleartext backend port expected for offloaded HTTPS requests.
    /// </summary>
    public int ExpectedLocalPort { get; set; }
}
