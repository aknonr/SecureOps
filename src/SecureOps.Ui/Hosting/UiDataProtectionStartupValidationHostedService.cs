using Microsoft.AspNetCore.DataProtection;

namespace SecureOps.Ui.Hosting;

/// <summary>Forces a startup Data Protection round trip for UI authentication and antiforgery.</summary>
public sealed class UiDataProtectionStartupValidationHostedService : IHostedService
{
    private const string _validationPurpose = "SecureOps.Ui.DataProtection.StartupValidation.v1";
    private readonly IDataProtectionProvider _provider;

    /// <summary>Initializes the startup validator.</summary>
    public UiDataProtectionStartupValidationHostedService(IDataProtectionProvider provider)
    {
        _provider = provider;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IDataProtector protector = _provider.CreateProtector(_validationPurpose);
        const string sentinel = "secureops-ui-data-protection-startup-validation";
        string protectedValue = protector.Protect(sentinel);
        if (!string.Equals(protector.Unprotect(protectedValue), sentinel, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("UI Data Protection startup validation failed.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
