namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Resolves an identity to at most one exact Jira account.</summary>
public interface IJiraUserResolver
{
    /// <summary>Resolves without fuzzy matching.</summary>
    public Task<RequesterResolutionResult> ResolveExactAsync(string identity, CancellationToken cancellationToken);
}

/// <summary>Legacy requester-specific alias for the shared exact Jira user resolver.</summary>
public interface IRequesterResolver : IJiraUserResolver
{
}
