using Dapper;
using Microsoft.Data.SqlClient;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>
    /// Account name and domain of the given ids (missing ids are absent). Read only and not scope-filtered: the caller decides
    /// which names may leave the service.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, (string Name, string? Domain)>> AccountNamesAsync(IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        IEnumerable<(Guid Id, string Name, string? Domain)> rows = await connection.QueryAsync<(Guid Id, string Name, string? Domain)>(Cmd("""
            SELECT Id, AccountName, Domain FROM svcacct.Accounts WHERE Id IN @accountIds;
            """, new { accountIds }, null, cancellationToken));
        return rows.ToDictionary(r => r.Id, r => (r.Name, r.Domain));
    }
}
