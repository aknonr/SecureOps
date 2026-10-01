namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Fail-closed requester resolver used when no approved resolver is configured.</summary>
public sealed class UnresolvedRequesterResolver : IRequesterResolver
{
    /// <inheritdoc />
    public Task<RequesterResolutionResult> ResolveExactAsync(string requester, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(RequesterResolutionResult.NotFound());
    }
}
