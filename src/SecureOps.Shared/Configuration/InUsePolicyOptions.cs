namespace SecureOps.Shared.Configuration;

/// <summary>Reviewed business proposals, never source observations or completed monitoring checks.</summary>
public sealed class InUsePolicyOptions
{
    /// <summary>Change when approved organizational proposals change.</summary>
    public string Revision { get; set; } = "inuse-nms-v1";
    /// <summary>Optional approved values for the bounded organizational field allowlist.</summary>
    public Dictionary<string, string> Proposals { get; set; } = new(StringComparer.Ordinal);
}
