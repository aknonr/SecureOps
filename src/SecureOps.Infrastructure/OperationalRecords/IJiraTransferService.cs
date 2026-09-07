using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Coordinates preview, create, and retry operations.</summary>
public interface IJiraTransferService
{
    /// <summary>Reviews an explicit type declaration without granting publication eligibility.</summary>
    public Task<OperationalRecordResult<JiraIssueDraft>> ReviewAsync(Guid id,
        SecureOps.Shared.Contracts.OperationalRecords.JiraReviewRequest request,
        OperationalRecordCommandContext context, CancellationToken cancellationToken);
    /// <summary>Generates and persists a read-only preview state.</summary>
    public Task<OperationalRecordResult<JiraIssueDraft>> PreviewAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken);

    /// <summary>Executes an explicit authorized Jira transfer request.</summary>
    public Task<OperationalRecordResult<OperationalRecord>> CreateAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken);

    /// <summary>Resumes the safe stage of a failed workflow.</summary>
    public Task<OperationalRecordResult<OperationalRecord>> RetryAsync(Guid id, OperationalRecordCommandContext context, CancellationToken cancellationToken);
}
