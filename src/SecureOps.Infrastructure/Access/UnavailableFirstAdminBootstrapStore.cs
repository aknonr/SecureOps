namespace SecureOps.Infrastructure.Access;

/// <summary>Fail-closed store used when SQL access persistence is not configured.</summary>
public sealed class UnavailableFirstAdminBootstrapStore : IFirstAdminBootstrapStore
{
    /// <inheritdoc />
    public Task<FirstAdminBootstrapDisposition> TryGrantAsync(
        FirstAdminBootstrapCommand command,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("First-Admin bootstrap requires SQL Server persistence.");
}
