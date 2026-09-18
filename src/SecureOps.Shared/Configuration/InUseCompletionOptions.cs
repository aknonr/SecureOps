namespace SecureOps.Shared.Configuration;

/// <summary>Default-off source completion; mail and source-read switches do not enable it.</summary>
public sealed class InUseCompletionOptions
{
    /// <summary>Explicit deployment fence, false in shipped configuration.</summary>
    public bool Enabled { get; set; }
    /// <summary>Disabled, Fixture (isolated local only), or TuruncuHat (contract-blocked until authoritative evidence is implemented).</summary>
    public string Provider { get; set; } = "Disabled";
    /// <summary>Bounded per-step timeout, shorter than the durable lease.</summary>
    public int TimeoutSeconds { get; set; } = 30;
    /// <summary>Private absolute directory for synthetic remote fixture state; never corporate data.</summary>
    public string FixtureDirectory { get; set; } = "";
}
