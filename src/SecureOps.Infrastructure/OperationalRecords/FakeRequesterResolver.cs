namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Exact deterministic requester resolver for the synthetic workflow dataset.</summary>
public sealed class FakeRequesterResolver : IRequesterResolver
{
    /// <inheritdoc />
    public Task<RequesterResolutionResult> ResolveExactAsync(string requester, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(string.Equals(requester, FakeOperationalRecordClient.Requester, StringComparison.Ordinal)
            ? RequesterResolutionResult.Found("synthetic-jira-account")
            : RequesterResolutionResult.NotFound());
    }
}
