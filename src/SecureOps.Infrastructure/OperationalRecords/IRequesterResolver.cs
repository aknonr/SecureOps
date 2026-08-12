namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Resolves a source requester to at most one exact Jira account.</summary>
public interface IRequesterResolver
{
    /// <summary>Resolves without fuzzy matching.</summary>
    public Task<RequesterResolutionResult> ResolveExactAsync(string requester, CancellationToken cancellationToken);
}
