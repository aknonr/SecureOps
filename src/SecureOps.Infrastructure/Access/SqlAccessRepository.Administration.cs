using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.Access;

public sealed partial class SqlAccessRepository
{
    private const string _roleSelect = "SELECT RoleCode AS Code,DisplayName AS Name,Purpose,Version,IsProtected AS Protected,CapabilitiesJson FROM security.Roles";

    /// <summary>Reads persisted role definitions; mutations require a reviewed impact.</summary>
    public async Task<IReadOnlyList<AccessRoleDefinition>> GetRoleDefinitionsAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        return (await connection.QueryAsync<RoleRow>(Command($"{_roleSelect} ORDER BY RoleId", null, null, cancellationToken)))
            .Select(Role).ToArray();
    }

    /// <summary>Previews or applies one definition under the same administrative serialization boundary.</summary>
    public async Task<AccessServiceResult<AccessRoleImpact>> ChangeRoleAsync(AccessRoleChange change, string actor, bool apply, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(change.Code) || change.Code.Length > 64 ||
            !change.Code.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ||
            string.IsNullOrWhiteSpace(change.Name) || change.Name.Length > 100 ||
            string.IsNullOrWhiteSpace(change.Purpose) || change.Purpose.Length > 500 ||
            change.ExpectedVersion < 0 || change.Capabilities is not { Count: <= 64 })
        {
            return AccessServiceResult<AccessRoleImpact>.Fail(OperationalErrorCodes.AccessValidationFailed);
        }

        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockAdministrationAsync(connection, transaction, cancellationToken);
        UserRow? actorRow = await connection.QuerySingleOrDefaultAsync<UserRow>(Command(
            $"{_readUserSql} WHERE u.CorporateIdentity=@actor {_userGroupBy}", new { actor }, transaction, cancellationToken));
        ApplicationUser? human = actorRow is null ? null : Map(actorRow);
        if (human is not { Status: AccessStatus.Approved } || !human.Capabilities.Contains(Capabilities.AccessManageUsers) || !human.Capabilities.Contains(Capabilities.AccessAssignRoles))
        {
            return AccessServiceResult<AccessRoleImpact>.Fail(OperationalErrorCodes.AccessDenied);
        }

        RoleRow? row = await connection.QuerySingleOrDefaultAsync<RoleRow>(Command($"{_roleSelect} WHERE RoleCode=@Code", change, transaction, cancellationToken));
        if ((row?.Version ?? 0) != change.ExpectedVersion)
        {
            return AccessServiceResult<AccessRoleImpact>.Fail(OperationalErrorCodes.AccessConcurrencyConflict);
        }

        if (row is { Protected: true })
        {
            return AccessServiceResult<AccessRoleImpact>.Fail(OperationalErrorCodes.AccessProtectedRole);
        }

        AccessRoleDefinition? previous = row is null ? null : Role(row);
        string[] capabilities = change.Capabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (capabilities.Any(capability => !AccessActionCatalog.Actions.Any(action => action.Code == capability &&
            (action.Assignable || previous?.Capabilities.Contains(capability) == true))))
        {
            return AccessServiceResult<AccessRoleImpact>.Fail(OperationalErrorCodes.AccessValidationFailed);
        }

        if (human.Roles.Contains(change.Code, StringComparer.OrdinalIgnoreCase) && capabilities.Except(human.Capabilities).Any())
        {
            return AccessServiceResult<AccessRoleImpact>.Fail(OperationalErrorCodes.AccessSelfEscalationDenied);
        }

        string code = previous?.Code ?? change.Code;
        string json = JsonSerializer.Serialize(capabilities);
        string[] added = capabilities.Except(previous?.Capabilities ?? []).ToArray();
        string[] removed = (previous?.Capabilities ?? []).Except(capabilities).ToArray();
        const string impactSql = """
            WITH Affected AS (SELECT u.UserId FROM security.Users u WHERE u.AccessStatus='Approved' AND EXISTS (
                SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId
                WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND r.RoleCode=@code))
            SELECT (SELECT COUNT_BIG(*) FROM Affected) AS AffectedUsers,
                (SELECT COUNT_BIG(*) FROM Affected u WHERE EXISTS (
                    SELECT value FROM OPENJSON(@added) EXCEPT
                    SELECT value FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId
                    CROSS APPLY OPENJSON(r.CapabilitiesJson) WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL)
                    ) AS UsersGaining,
                (SELECT COUNT_BIG(*) FROM Affected u WHERE EXISTS (
                    SELECT value FROM OPENJSON(@removed) EXCEPT
                    SELECT value FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId
                    CROSS APPLY OPENJSON(r.CapabilitiesJson) WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND r.RoleCode<>@code)
                    ) AS UsersLosing;
            SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE(STRING_AGG(CONVERT(nvarchar(max),
                CONCAT(u.UserId,':',u.AccessVersion)),',') WITHIN GROUP (ORDER BY u.UserId),'')),2)
            FROM security.Users u WHERE EXISTS (
                SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId
                WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND r.RoleCode=@code);
            """;
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Command(impactSql,
            new { code, added = JsonSerializer.Serialize(added), removed = JsonSerializer.Serialize(removed) }, transaction, cancellationToken));
        ImpactRow impact = await grid.ReadSingleAsync<ImpactRow>();
        string assignmentHash = await grid.ReadSingleAsync<string>();
        AccessRoleDefinition proposed = new(code, change.Name.Trim(), change.Purpose.Trim(), change.ExpectedVersion + 1, false, capabilities);
        string token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { proposed, assignmentHash, human.Id, human.Version }))));
        AccessRoleImpact result = new(proposed, impact.AffectedUsers, impact.UsersGaining, impact.UsersLosing, added, removed, token);
        if (!apply)
        {
            return AccessServiceResult<AccessRoleImpact>.Success(result);
        }

        if (!string.Equals(token, change.PreviewToken, StringComparison.Ordinal))
        {
            return AccessServiceResult<AccessRoleImpact>.Fail(OperationalErrorCodes.AccessConcurrencyConflict);
        }

        const string write = """
            IF @ExpectedVersion=0
                INSERT INTO security.Roles(RoleId,RoleCode,DisplayName,Purpose,Version,IsProtected,CapabilitiesJson)
                SELECT COALESCE(MAX(RoleId),0)+1,@code,@Name,@Purpose,1,0,@json FROM security.Roles;
            ELSE
                UPDATE security.Roles SET DisplayName=@Name,Purpose=@Purpose,CapabilitiesJson=@json,Version=Version+1 WHERE RoleCode=@code;
            UPDATE u SET AccessVersion=AccessVersion+1 FROM security.Users u WHERE EXISTS(
                SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId
                WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND r.RoleCode=@code);
            INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson)
            VALUES(SYSUTCDATETIME(),@actor,'AccessRoleDefinitionChanged',@correlation,@details);
            """;
        await connection.ExecuteAsync(Command(write, new
        {
            code,
            proposed.Name,
            proposed.Purpose,
            change.ExpectedVersion,
            json,
            actor,
            correlation = $"role:{code}:{proposed.Version}",
            details = JsonSerializer.Serialize(new { schemaVersion = 1, actorId = human.Id, actorKind = "Human", human.DisplayName, human.LoginName, human.Mail, previous, result, outcome = "Applied" })
        }, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return AccessServiceResult<AccessRoleImpact>.Success(result);
    }

    internal static async Task LockAdministrationAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=N'SecureOps.Access.Administration.v1',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
            IF @result<0 THROW 51000,'Access administration lock unavailable.',1;
            """;
        await connection.ExecuteAsync(Command(sql, null, transaction, cancellationToken));
    }

    private static async Task<bool> KnownRolesAsync(SqlConnection connection, SqlTransaction transaction, string[] roles, CancellationToken cancellationToken) =>
        roles.Length == await connection.ExecuteScalarAsync<int>(Command("SELECT COUNT(*) FROM security.Roles WHERE RoleCode IN @roles", new { roles }, transaction, cancellationToken));

    private static async Task<bool> ActorAuthorizedAsync(SqlConnection connection, SqlTransaction transaction, string actor, string capability, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(Command("""
            SELECT COUNT(*) FROM security.Users u WHERE u.CorporateIdentity=@actor AND u.AccessStatus='Approved' AND EXISTS (
                SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId CROSS APPLY OPENJSON(r.CapabilitiesJson) c
                WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND c.value=@capability)
            """, new { actor, capability }, transaction, cancellationToken)) == 1;

    private static async Task AuditAccessChangeAsync(SqlConnection connection, SqlTransaction transaction, string actor, string action, Guid userId, long version, object change, CancellationToken cancellationToken)
    {
        object? profile = await connection.QuerySingleOrDefaultAsync(Command(
            "SELECT UserId,DisplayName,LoginName,Mail FROM security.Users WHERE CorporateIdentity=@actor", new { actor }, transaction, cancellationToken));
        await connection.ExecuteAsync(Command("""
            INSERT INTO audit.AuditLog(OccurredAt,Actor,Action,CorrelationId,DetailsJson)
            VALUES(SYSUTCDATETIME(),@actor,@action,@correlation,@details);
            """, new
        {
            actor,
            action,
            correlation = $"access:{action}:{userId:D}:{version + 1}",
            details = JsonSerializer.Serialize(new
            { schemaVersion = 1, profile, actorKind = profile is null ? "System" : "Human", recordId = userId, inputVersion = version, outcome = "Applied", change })
        }, transaction, cancellationToken));
    }

    private static async Task<string[]> CapabilitiesAsync(SqlConnection connection, SqlTransaction transaction, string[] roles, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<string>(Command("SELECT DISTINCT value FROM security.Roles CROSS APPLY OPENJSON(CapabilitiesJson) WHERE RoleCode IN @roles ORDER BY value", new { roles }, transaction, cancellationToken))).ToArray();

    private static async Task<bool> ReviewedRolesAsync(SqlConnection connection, SqlTransaction transaction, string[] roles, IReadOnlyDictionary<string, long>? versions, CancellationToken cancellationToken)
    {
        IEnumerable<RoleRow> definitions = await connection.QueryAsync<RoleRow>(Command($"{_roleSelect} WHERE RoleCode IN @roles", new { roles }, transaction, cancellationToken));
        return definitions.All(role => versions is null
            ? role.Version == 1 && AccessRoleCatalog.IsKnownRole(role.Code)
            : versions.TryGetValue(role.Code, out long reviewed) && reviewed == role.Version);
    }

    private static async Task<bool> RemovesLastAdminAsync(SqlConnection connection, SqlTransaction transaction, ApplicationUser current, string[] roles, CancellationToken cancellationToken) =>
        current.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase) && !roles.Contains("Admin", StringComparer.OrdinalIgnoreCase) &&
        await connection.ExecuteScalarAsync<int>(Command("""
            SELECT COUNT(*) FROM security.Users u WHERE u.UserId<>@Id AND u.AccessStatus='Approved' AND EXISTS(
                SELECT 1 FROM security.RoleAssignments a JOIN security.Roles r ON r.RoleId=a.RoleId
                WHERE a.UserId=u.UserId AND a.RevokedAt IS NULL AND r.RoleCode='Admin')
            """, new { current.Id }, transaction, cancellationToken)) == 0;

    private static AccessRoleDefinition Role(RoleRow row) => new(row.Code, row.Name, row.Purpose, row.Version, row.Protected, JsonSerializer.Deserialize<string[]>(row.CapabilitiesJson)!);

    private sealed record RoleRow(string Code, string Name, string Purpose, long Version, bool Protected, string CapabilitiesJson);
    private sealed record ImpactRow(long AffectedUsers, long UsersGaining, long UsersLosing);
}
