using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Builds a safe Jira preview from configured mappings.</summary>
public interface IJiraIssueDraftService
{
    /// <summary>Builds a non-publishable operator declaration without remote identity calls.</summary>
    public JiraIssueDraft BuildReview(OperationalRecord record, OperationalRecordClassification requestType);
    /// <summary>Builds a draft without creating or modifying remote data.</summary>
    public Task<OperationalRecordResult<JiraIssueDraft>> BuildAsync(
        OperationalRecord record,
        string actor,
        CancellationToken cancellationToken);
}
