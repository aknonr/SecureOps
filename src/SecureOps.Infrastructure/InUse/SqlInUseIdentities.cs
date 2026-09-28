using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Bounded trusted-profile projections. No directory scan and no name-based identity assignment.</summary>
public sealed class SqlInUseIdentities(IConfiguration configuration)
{
    /// <summary>Returns at most fifty eligible matches, or exact labels for a bounded record page.</summary>
    public async Task<IReadOnlyList<InUseAssignee>> ReadAsync(Guid[]? ids, string? search, CancellationToken token)
    {
        if (ids?.Length > 100 || search?.Length > 100)
        { throw new ArgumentException("Identity query exceeds its bound."); }
        if (ids is { Length: 0 })
        { return []; }
        await using var sql = new SqlConnection(configuration.GetConnectionString("SecureOpsDb"));
        const string name = "COALESCE(NULLIF(LTRIM(RTRIM(DisplayName)),''),NULLIF(LTRIM(RTRIM(LoginName)),''),N'Kullanıcı adı çözümlenemedi')";
        string selector = ids is null ? """
            WHERE u.AccessStatus='Approved' AND (@search IS NULL OR CHARINDEX(@search,n.Name)>0 OR CHARINDEX(@search,u.LoginName)>0)
              AND EXISTS(SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId CROSS APPLY OPENJSON(r.CapabilitiesJson) c
                  WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND c.value='InUse.View')
              AND EXISTS(SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId CROSS APPLY OPENJSON(r.CapabilitiesJson) c
                  WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND c.value='InUse.Review')
            """ : "WHERE u.UserId IN @ids";
        string query = $"""
            SELECT TOP({(ids is null ? 50 : 100)}) u.UserId AS Id,n.Name,u.DisplayName,u.LoginName,
                (SELECT COUNT(*) FROM security.Users WHERE {name}=n.Name) AS NameCount,
                (SELECT COUNT(*) FROM security.Users WHERE LEFT(CONVERT(varchar(36),UserId),8)=LEFT(CONVERT(varchar(36),u.UserId),8)) AS PrefixCount
            FROM security.Users u CROSS APPLY(SELECT {name} AS Name) n {selector} ORDER BY n.Name,u.UserId;
            """;
        IEnumerable<IdentityRow> rows = await sql.QueryAsync<IdentityRow>(new CommandDefinition(query,
            new { ids, search = string.IsNullOrWhiteSpace(search) ? null : search.Trim() }, commandTimeout: 15, cancellationToken: token));
        return rows.Select(row => new InUseAssignee(row.Id, InUsePersonLabel.Format(row.DisplayName, row.LoginName)
            + (row.NameCount == 1 || !string.IsNullOrWhiteSpace(row.LoginName) ? ""
                : " · kullanıcı " + (row.PrefixCount > 1 ? row.Id.ToString("D") : row.Id.ToString("N")[..8])))).ToArray();
    }
    private sealed record IdentityRow(Guid Id, string Name, string? DisplayName, string? LoginName, int NameCount, int PrefixCount);
}
