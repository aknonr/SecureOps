namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Resolves only the fixed synthetic requester used by TEST simulation.</summary>
public sealed class SimulationRequesterResolver : IRequesterResolver
{
    /// <inheritdoc />
    public Task<RequesterResolutionResult> ResolveExactAsync(
        string requester,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            string.Equals(requester, SimulationOperationalRecordClient.Requester, StringComparison.Ordinal)
                ? RequesterResolutionResult.Found("synthetic-simulation-account")
                : RequesterResolutionResult.NotFound());
    }
}
