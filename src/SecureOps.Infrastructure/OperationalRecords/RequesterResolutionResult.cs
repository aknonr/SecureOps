namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Exact requester resolution outcome.</summary>
public sealed record RequesterResolutionResult(RequesterResolutionStatus Status, string? JiraAccountId)
{
    /// <summary>Creates an exact unique match.</summary>
    public static RequesterResolutionResult Found(string jiraAccountId) => new(RequesterResolutionStatus.Found, jiraAccountId);
    /// <summary>Creates a no-match result.</summary>
    public static RequesterResolutionResult NotFound() => new(RequesterResolutionStatus.NotFound, null);
    /// <summary>Creates an ambiguous exact-match result.</summary>
    public static RequesterResolutionResult Ambiguous() => new(RequesterResolutionStatus.Ambiguous, null);
    /// <summary>Creates a provider failure result.</summary>
    public static RequesterResolutionResult Failed() => new(RequesterResolutionStatus.Failed, null);
}

/// <summary>Requester resolution status.</summary>
public enum RequesterResolutionStatus
{
    /// <summary>One exact Jira account was found.</summary>
    Found,
    /// <summary>No exact Jira account was found.</summary>
    NotFound,
    /// <summary>Multiple exact candidates were returned.</summary>
    Ambiguous,
    /// <summary>The resolver failed safely.</summary>
    Failed
}
