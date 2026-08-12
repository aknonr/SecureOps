using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Imports, classifies, and queries operational records.</summary>
public interface IOperationalRecordService
{
    /// <summary>Refreshes the bounded active list and returns persisted records.</summary>
    public Task<OperationalRecordResult<IReadOnlyList<OperationalRecord>>> ListAsync(OperationalRecordCommandContext context, CancellationToken cancellationToken);

    /// <summary>Gets one imported record.</summary>
    public Task<OperationalRecordResult<OperationalRecord>> GetAsync(Guid id, CancellationToken cancellationToken);
}
