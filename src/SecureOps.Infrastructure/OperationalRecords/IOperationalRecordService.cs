using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Imports, classifies, and queries operational records.</summary>
public interface IOperationalRecordService
{
    /// <summary>Browses persisted records; does not query or modify the source.</summary>
    public Task<OperationalRecordPage> BrowseAsync(SecureOps.Shared.Contracts.OperationalRecords.OperationalRecordQuery query, CancellationToken cancellationToken);
    /// <summary>Returns persisted records; does not query the source and writes nothing.</summary>
    public Task<IReadOnlyList<OperationalRecord>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Refreshes the bounded active list from the source and returns persisted records.</summary>
    public Task<OperationalRecordResult<IReadOnlyList<OperationalRecord>>> RefreshAsync(OperationalRecordCommandContext context, CancellationToken cancellationToken);

    /// <summary>Gets one imported record.</summary>
    public Task<OperationalRecordResult<OperationalRecord>> GetAsync(Guid id, CancellationToken cancellationToken);
}
