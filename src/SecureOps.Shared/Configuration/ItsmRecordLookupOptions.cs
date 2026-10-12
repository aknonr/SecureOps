namespace SecureOps.Shared.Configuration;

/// <summary>
/// Server-owned selection of the read-only ITSM record lookup (ADR-0031). The provider is chosen only by
/// configuration, defaults to Disabled, and never falls back to Fake. Turuncu Hat credentials and HTTP bounds
/// stay in <see cref="TuruncuHatOptions"/>; this section adds no credential.
/// </summary>
public sealed class ItsmRecordLookupOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ItsmRecordLookup";

    /// <summary>Lookup provider: Disabled, Fake (Development, Demo and Test only) or TuruncuHat.</summary>
    public string Provider { get; set; } = "Disabled";
    /// <summary>Turuncu Hat base object holding OR records; corporate value, configuration-owned only.</summary>
    public string OrBaseObject { get; set; } = string.Empty;
    /// <summary>Turuncu Hat base object holding OCO records; corporate value, configuration-owned only.</summary>
    public string OcoBaseObject { get; set; } = string.Empty;
}
