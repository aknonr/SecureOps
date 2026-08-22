namespace SecureOps.Shared.Configuration;

/// <summary>Persistent ASP.NET Core Data Protection configuration.</summary>
public sealed class PersistentDataProtectionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "DataProtection";

    /// <summary>Ephemeral, FileSystemDpapi, or FileSystemCertificate.</summary>
    public string Mode { get; set; } = "Ephemeral";

    /// <summary>Stable application discriminator shared by intended nodes only.</summary>
    public string ApplicationName { get; set; } = "SecureOps.Api";

    /// <summary>Server-owned persistent key-ring directory.</summary>
    public string? KeyRingPath { get; set; }

    /// <summary>Local-machine certificate thumbprint used by shared key rings.</summary>
    public string? CertificateThumbprint { get; set; }
}
