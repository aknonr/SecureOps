using Microsoft.AspNetCore.DataProtection;

namespace SecureOps.Api.Security;

/// <summary>Forces a startup Data Protection round-trip so key-ring failures are not deferred to a request.</summary>
public sealed class DataProtectionStartupValidationHostedService : IHostedService
{
    private const string _validationPurpose = "SecureOps.DataProtection.StartupValidation.v1";
    private readonly IDataProtectionProvider _provider;

    /// <summary>Initializes the startup validator.</summary>
    public DataProtectionStartupValidationHostedService(IDataProtectionProvider provider)
    {
        _provider = provider;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IDataProtector protector = _provider.CreateProtector(_validationPurpose);
        const string sentinel = "secureops-data-protection-startup-validation";
        string protectedValue = protector.Protect(sentinel);
        if (!string.Equals(protector.Unprotect(protectedValue), sentinel, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Data Protection startup validation failed.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
