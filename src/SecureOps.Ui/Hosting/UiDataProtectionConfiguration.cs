using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using SecureOps.Shared.Configuration;

namespace SecureOps.Ui.Hosting;

/// <summary>Validates and registers the UI host's explicitly selected Data Protection key ring.</summary>
public static class UiDataProtectionConfiguration
{
    /// <summary>Validates persistent protection requirements before host construction.</summary>
    public static void Validate(IConfiguration configuration, string environmentName)
    {
        PersistentDataProtectionOptions options = configuration
            .GetSection(PersistentDataProtectionOptions.SectionName)
            .Get<PersistentDataProtectionOptions>() ?? new();

        if (string.IsNullOrWhiteSpace(options.ApplicationName) || options.ApplicationName.Trim().Length > 128)
        {
            throw new InvalidOperationException("DataProtection:ApplicationName is required and must be no longer than 128 characters.");
        }

        bool ephemeral = string.Equals(options.Mode, "Ephemeral", StringComparison.OrdinalIgnoreCase);
        bool fileSystemDpapi = string.Equals(options.Mode, "FileSystemDpapi", StringComparison.OrdinalIgnoreCase);
        bool fileSystemCertificate = string.Equals(options.Mode, "FileSystemCertificate", StringComparison.OrdinalIgnoreCase);
        if (!ephemeral && !fileSystemDpapi && !fileSystemCertificate)
        {
            throw new InvalidOperationException("DataProtection:Mode must be Ephemeral, FileSystemDpapi, or FileSystemCertificate.");
        }

        if (RequiresPersistentProtection(environmentName) && ephemeral)
        {
            throw new InvalidOperationException("Pilot and Production require persistent Data Protection; Ephemeral mode is prohibited.");
        }

        if ((fileSystemDpapi || fileSystemCertificate)
            && (string.IsNullOrWhiteSpace(options.KeyRingPath) || !Path.IsPathFullyQualified(options.KeyRingPath)))
        {
            throw new InvalidOperationException("DataProtection:KeyRingPath must be an absolute server-owned path for persistent modes.");
        }

        if (fileSystemCertificate && string.IsNullOrWhiteSpace(options.CertificateThumbprint))
        {
            throw new InvalidOperationException("DataProtection:CertificateThumbprint is required for FileSystemCertificate mode.");
        }
    }

    /// <summary>Registers the UI host's selected Data Protection provider.</summary>
    public static IServiceCollection AddSecureOpsUiDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        PersistentDataProtectionOptions options = configuration
            .GetSection(PersistentDataProtectionOptions.SectionName)
            .Get<PersistentDataProtectionOptions>() ?? new();
        services.Configure<PersistentDataProtectionOptions>(configuration.GetSection(PersistentDataProtectionOptions.SectionName));

        IDataProtectionBuilder builder = services.AddDataProtection()
            .SetApplicationName(options.ApplicationName.Trim());

        if (string.Equals(options.Mode, "Ephemeral", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseEphemeralDataProtectionProvider();
            return services;
        }

        builder.PersistKeysToFileSystem(new DirectoryInfo(options.KeyRingPath!));
        if (string.Equals(options.Mode, "FileSystemDpapi", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("FileSystemDpapi Data Protection requires Windows.");
            }

            builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
            return services;
        }

        builder.ProtectKeysWithCertificate(LoadCertificate(options.CertificateThumbprint!));
        return services;
    }

    private static bool RequiresPersistentProtection(string environmentName) =>
        string.Equals(environmentName, "Pilot", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);

    private static X509Certificate2 LoadCertificate(string configuredThumbprint)
    {
        string thumbprint = configuredThumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        using X509Store store = new(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        X509Certificate2? certificate = store.Certificates
            .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
            .OfType<X509Certificate2>()
            .SingleOrDefault(certificate => certificate.HasPrivateKey);
        return certificate ?? throw new InvalidOperationException(
            "The configured Data Protection certificate was not found with an accessible private key in LocalMachine/My.");
    }
}
