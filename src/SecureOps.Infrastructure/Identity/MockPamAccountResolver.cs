namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Mock PAM resolver that leaves the account unchanged.
/// </summary>
public sealed class MockPamAccountResolver : IPamAccountResolver
{
    /// <inheritdoc />
    public Task<PamAccountResolution> ResolveAsync(string normalizedAccount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PamAccountResolution(normalizedAccount, "Mock"));
    }
}
