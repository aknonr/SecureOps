using Microsoft.Data.SqlClient;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>
    /// Records that a multi-account usage-scan upload was refused, as a whole or for some accounts (ADR-0027). The caller passes
    /// counts and a stable reason code only: never an account name or id, a file name, a hash or anything from the file.
    /// </summary>
    public async Task RecordUsageScanBatchRefusalAsync(UsageScanBatchRefusal refusal, SaActor actor, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        await AuditAsync(connection, transaction, "UsageScanBatchRefused", refusal, actor, DateTimeOffset.UtcNow, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

/// <summary>
/// Audit details of a refused (or partly refused) multi-account usage-scan upload: a stable reason (<c>accounts</c> when some
/// accounts were not linked, otherwise the request field or file code) and counts only.
/// </summary>
public sealed record UsageScanBatchRefusal(string Reason, int Requested, int Attached = 0, int AlreadyAttached = 0, int NotInScan = 0,
    int Ambiguous = 0, int Unavailable = 0, int Failed = 0);
