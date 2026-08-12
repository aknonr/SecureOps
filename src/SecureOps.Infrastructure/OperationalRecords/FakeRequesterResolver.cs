namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Fail-closed requester resolver that performs no external lookup.</summary>
public sealed class FakeRequesterResolver : IRequesterResolver
{
    /// <inheritdoc />
    public Task<RequesterResolutionResult> ResolveExactAsync(string requester, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(RequesterResolutionResult.NotFound());
    }
}
