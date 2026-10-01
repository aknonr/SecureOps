namespace SecureOps.Shared.Configuration;

/// <summary>Configuration boundary for a future approved PAM account resolver.</summary>
public sealed class PamProviderOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "PamProvider";

    /// <summary>Provider implementation. Only Mock is currently supported.</summary>
    public string Provider { get; set; } = "Mock";

    /// <summary>Maximum time allowed for a future provider call.</summary>
    public int TimeoutSeconds { get; set; } = 3;
}
