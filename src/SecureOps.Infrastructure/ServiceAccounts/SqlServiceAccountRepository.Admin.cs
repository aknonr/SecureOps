using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>Organization references for the given IDs.</summary>
    public async Task<IReadOnlyList<SaRef>> OrganizationRefsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        Guid[] list = [.. ids];
        if (list.Length == 0)
        {
            return [];
        }

        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<SaRef>(Cmd("SELECT Id, Name AS Label, CAST(NULL AS nvarchar(16)) AS State FROM svcacct.Organizations WHERE Id IN @list ORDER BY Name;",
            new { list }, null, cancellationToken))];
    }

    /// <summary>Team references for the given IDs.</summary>
    public async Task<IReadOnlyList<SaRef>> TeamRefsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        Guid[] list = [.. ids];
        if (list.Length == 0)
        {
            return [];
        }

        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<SaRef>(Cmd("SELECT Id, Name AS Label, CAST(NULL AS nvarchar(16)) AS State FROM svcacct.Teams WHERE Id IN @list ORDER BY Name;",
            new { list }, null, cancellationToken))];
    }

    /// <summary>All organizations.</summary>
    public async Task<IReadOnlyList<OrganizationView>> OrganizationsAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<(Guid Id, string Name, Guid? ParentId, string Kind, byte[] RowVer)>(Cmd(
            "SELECT Id, Name, ParentId, Kind, RowVer FROM svcacct.Organizations ORDER BY Name;", null, null, cancellationToken)))
            .Select(o => new OrganizationView(o.Id, o.Name, o.ParentId, o.Kind, Version(o.RowVer)))];
    }

    /// <summary>All teams.</summary>
    public async Task<IReadOnlyList<TeamView>> TeamsAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<(Guid Id, string Name, Guid? OrganizationId, bool Provisional, byte[] RowVer)>(Cmd(
            "SELECT Id, Name, OrganizationId, Provisional, RowVer FROM svcacct.Teams ORDER BY Name;", null, null, cancellationToken)))
            .Select(t => new TeamView(t.Id, t.Name, t.OrganizationId, t.Provisional, Version(t.RowVer)))];
    }

    /// <summary>Module person references matching a label prefix (never a directory search).</summary>
    public async Task<IReadOnlyList<PersonView>> PeopleAsync(string? search, int take, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        string? key = ServiceAccountText.LabelKey(search);
        var people = (await connection.QueryAsync<(Guid Id, string DisplayName, string State, bool Directory, byte[] RowVer)>(Cmd("""
            SELECT TOP (@take) p.Id, p.DisplayName, p.VerificationState,
                CAST(CASE WHEN p.Upn IS NOT NULL OR p.DirectoryObjectId IS NOT NULL THEN 1 ELSE 0 END AS bit), p.RowVer
            FROM svcacct.People p
            WHERE @key IS NULL OR p.NormalizedName LIKE @prefix
               OR EXISTS (SELECT 1 FROM svcacct.PersonAliases pa WHERE pa.PersonId = p.Id AND pa.AliasNormalized LIKE @prefix)
            ORDER BY p.DisplayName, p.Id;
            """, new { take, key, prefix = key is null ? null : EscapeLike(key) + "%" }, null, cancellationToken))).ToList();
        Guid[] ids = [.. people.Select(p => p.Id)];
        ILookup<Guid, string> aliases = ids.Length == 0 ? Array.Empty<(Guid, string)>().ToLookup(a => a.Item1, a => a.Item2)
            : (await connection.QueryAsync<(Guid PersonId, string Alias)>(Cmd("SELECT PersonId, Alias FROM svcacct.PersonAliases WHERE PersonId IN @ids;",
                new { ids }, null, cancellationToken))).ToLookup(a => a.PersonId, a => a.Alias);
        return [.. people.Select(p => new PersonView(p.Id, p.DisplayName, p.State, p.Directory, [.. aliases[p.Id].Order(StringComparer.Ordinal)], Version(p.RowVer)))];
    }

    /// <summary>Creates or updates an organization with rowversion concurrency.</summary>
    public Task<SaResult<Guid>> SaveOrganizationAsync(Guid? id, SaveOrganizationRequest request, SaActor actor, CancellationToken cancellationToken) =>
        SaveNamedAsync("Organizations", "Organization", id, request.Name, request.ExpectedVersion, actor, cancellationToken,
            "ParentId = @ParentId, Kind = @Kind", "ParentId, Kind", "@ParentId, @Kind", new { request.ParentId, request.Kind });

    /// <summary>Creates or updates a team.</summary>
    public Task<SaResult<Guid>> SaveTeamAsync(Guid? id, SaveTeamRequest request, SaActor actor, CancellationToken cancellationToken) =>
        SaveNamedAsync("Teams", "Team", id, request.Name, request.ExpectedVersion, actor, cancellationToken,
            "OrganizationId = @OrganizationId, Provisional = @Provisional", "OrganizationId, Provisional", "@OrganizationId, @Provisional",
            new { request.OrganizationId, request.Provisional });

    private async Task<SaResult<Guid>> SaveNamedAsync(string table, string entity, Guid? id, string name, string? expectedVersion, SaActor actor,
        CancellationToken cancellationToken, string updateSet, string insertColumns, string insertValues, object extra)
    {
        // table/entity/column fragments come only from the two closed callers above.
        string clean = ServiceAccountText.Clean(name)!;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DynamicParameters parameters = new(extra);
        Guid key = id ?? Guid.NewGuid();
        parameters.AddDynamicParams(new { Id = key, Name = clean, Key = ServiceAccountText.LabelKey(clean), now, actor.UserId, RowVer = Version(expectedVersion) });
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken, IsolationLevel.Serializable);
        if (await connection.ExecuteScalarAsync<int>(Cmd($"SELECT COUNT(*) FROM svcacct.{table} WHERE NormalizedName = @Key AND Id <> @Id;", parameters, transaction, cancellationToken)) > 0)
        {
            return SaResult<Guid>.Fail(SaErrors.Invalid, "name");
        }

        int changed = id is null
            ? await connection.ExecuteAsync(Cmd($"""
                INSERT INTO svcacct.{table}(Id, Name, NormalizedName, {insertColumns}, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@Id, @Name, @Key, {insertValues}, @now, @UserId, @now, @UserId);
                """, parameters, transaction, cancellationToken))
            : await connection.ExecuteAsync(Cmd($"""
                UPDATE svcacct.{table} SET Name = @Name, NormalizedName = @Key, {updateSet}, UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @Id AND RowVer = @RowVer;
                """, parameters, transaction, cancellationToken));
        if (changed != 1)
        {
            return SaResult<Guid>.Fail(SaErrors.Conflict, "expectedVersion");
        }

        await HistoryAsync(connection, transaction, entity, key, null, id is null ? "Created" : "Updated", new { Name = clean }, null, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, entity + (id is null ? "Created" : "Updated"), new { Id = key }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return key;
    }

    /// <summary>Creates a provisional person reference (grants nothing).</summary>
    public async Task<Guid> CreatePersonAsync(string displayName, SaActor actor, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string clean = ServiceAccountText.Clean(displayName)!;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken);
        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.People(Id, DisplayName, NormalizedName, VerificationState, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
            VALUES(@id, @clean, @key, 'Provisional', @now, @UserId, @now, @UserId);
            """, new { id, clean, key = ServiceAccountText.LabelKey(clean), now, actor.UserId }, transaction, cancellationToken));
        await HistoryAsync(connection, transaction, "Person", id, null, "Created", new { DisplayName = clean }, null, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "PersonCreated", new { Id = id }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>Records a verified directory identity supplied by an authorized administrator with evidence.</summary>
    public async Task<SaResult<Guid>> VerifyPersonAsync(Guid id, VerifyPersonRequest request, SaActor actor, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken, IsolationLevel.Serializable);
        string? upn = ServiceAccountText.Clean(request.Upn)?.ToLowerInvariant();
        string? objectId = ServiceAccountText.Clean(request.DirectoryObjectId);
        if (await connection.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM svcacct.People WHERE Id <> @id AND ((@upn IS NOT NULL AND Upn = @upn) OR (@objectId IS NOT NULL AND DirectoryObjectId = @objectId));
            """, new { id, upn, objectId }, transaction, cancellationToken)) > 0)
        {
            return SaResult<Guid>.Fail(SaErrors.Invalid, "upn");
        }

        int changed = await connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.People SET Upn = @upn, DirectoryObjectId = @objectId, VerificationState = 'Verified', VerifiedBy = @UserId, VerifiedAt = @now,
                UpdatedAt = @now, UpdatedBy = @UserId WHERE Id = @id AND RowVer = @RowVer;
            """, new { upn, objectId, actor.UserId, now, id, RowVer = Version(request.ExpectedVersion) }, transaction, cancellationToken));
        if (changed != 1)
        {
            return SaResult<Guid>.Fail(SaErrors.Conflict, "expectedVersion");
        }

        await HistoryAsync(connection, transaction, "Person", id, null, "Verified", new { HasUpn = upn is not null, HasObjectId = objectId is not null },
            request.Evidence, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "PersonVerified", new { Id = id }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>Adds an evidenced alias to a person; aliases never merge people automatically.</summary>
    public async Task<SaResult<Guid>> AddPersonAliasAsync(Guid id, AddPersonAliasRequest request, SaActor actor, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string alias = ServiceAccountText.Clean(request.Alias)!;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken, IsolationLevel.Serializable);
        int inserted = await connection.ExecuteAsync(Cmd("""
            IF EXISTS (SELECT 1 FROM svcacct.People WHERE Id = @id)
               AND NOT EXISTS (SELECT 1 FROM svcacct.PersonAliases WHERE PersonId = @id AND AliasNormalized = @key)
                INSERT INTO svcacct.PersonAliases(Id, PersonId, Alias, AliasNormalized, Evidence, CreatedAt, CreatedBy)
                VALUES(NEWID(), @id, @alias, @key, @evidence, @now, @UserId);
            """, new { id, alias, key = ServiceAccountText.LabelKey(alias), evidence = request.Evidence.Trim(), now, actor.UserId }, transaction, cancellationToken));
        if (inserted != 1)
        {
            return SaResult<Guid>.Fail(SaErrors.Invalid, "alias");
        }

        await HistoryAsync(connection, transaction, "Person", id, null, "AliasAdded", new { Alias = alias }, request.Evidence, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "PersonAliasAdded", new { Id = id }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>Active scope grants with user labels.</summary>
    public async Task<IReadOnlyList<ScopeGrantView>> GrantsAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<GrantRow>(Cmd("""
            SELECT g.Id, g.UserId, COALESCE(u.DisplayName, u.LoginName, u.CorporateIdentity) AS UserLabel, g.ScopeKind, g.OrganizationId, o.Name AS OrganizationName,
                g.TeamId, t.Name AS TeamName, g.Reason, g.GrantedAt, g.RowVer
            FROM svcacct.ScopeGrants g JOIN security.Users u ON u.UserId = g.UserId
            LEFT JOIN svcacct.Organizations o ON o.Id = g.OrganizationId LEFT JOIN svcacct.Teams t ON t.Id = g.TeamId
            WHERE g.RevokedAt IS NULL ORDER BY UserLabel, g.GrantedAt;
            """, null, null, cancellationToken))).Select(g => new ScopeGrantView(g.Id, g.UserId, g.UserLabel, g.ScopeKind,
                g.OrganizationId is { } o ? new SaRef(o, g.OrganizationName ?? "?") : null, g.TeamId is { } t ? new SaRef(t, g.TeamName ?? "?") : null,
                g.Reason, g.GrantedAt, Version(g.RowVer)))];
    }

    private sealed record GrantRow(Guid Id, Guid UserId, string UserLabel, string ScopeKind, Guid? OrganizationId, string? OrganizationName, Guid? TeamId,
        string? TeamName, string Reason, DateTimeOffset GrantedAt, byte[] RowVer);

    /// <summary>Creates a scope grant for an approved application user; self-grants are rejected by the schema.</summary>
    public async Task<SaResult<Guid>> CreateGrantAsync(Guid userId, ScopeKind kind, Guid? organizationId, Guid? teamId, string reason, SaActor actor,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken, IsolationLevel.Serializable);
        if (await connection.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM svcacct.ScopeGrants WHERE UserId = @userId AND RevokedAt IS NULL AND ScopeKind = @Kind
              AND ISNULL(OrganizationId, '00000000-0000-0000-0000-000000000000') = ISNULL(@organizationId, '00000000-0000-0000-0000-000000000000')
              AND ISNULL(TeamId, '00000000-0000-0000-0000-000000000000') = ISNULL(@teamId, '00000000-0000-0000-0000-000000000000');
            """, new { userId, Kind = kind.ToString(), organizationId, teamId }, transaction, cancellationToken)) > 0)
        {
            return SaResult<Guid>.Fail(SaErrors.Invalid, "duplicate");
        }

        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.ScopeGrants(Id, UserId, ScopeKind, OrganizationId, TeamId, Reason, GrantedBy, GrantedAt)
            VALUES(@id, @userId, @Kind, @organizationId, @teamId, @reason, @grantor, @now);
            """, new { id, userId, Kind = kind.ToString(), organizationId, teamId, reason, grantor = actor.UserId, now }, transaction, cancellationToken));
        await HistoryAsync(connection, transaction, "ScopeGrant", id, null, "Granted", new { userId, Kind = kind.ToString(), organizationId, teamId }, reason, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "ScopeGranted", new { Id = id, TargetUserId = userId, Kind = kind.ToString(), organizationId, teamId }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>Revokes a grant (history kept).</summary>
    public async Task<SaResult<Guid>> RevokeGrantAsync(Guid id, RevokeScopeGrantRequest request, SaActor actor, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken);
        int changed = await connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.ScopeGrants SET RevokedAt = @now, RevokedBy = @UserId, RevokeReason = @Reason
            WHERE Id = @id AND RevokedAt IS NULL AND RowVer = @RowVer;
            """, new { now, actor.UserId, request.Reason, id, RowVer = Version(request.ExpectedVersion) }, transaction, cancellationToken));
        if (changed != 1)
        {
            return SaResult<Guid>.Fail(SaErrors.Conflict, "expectedVersion");
        }

        await HistoryAsync(connection, transaction, "ScopeGrant", id, null, "Revoked", null, request.Reason, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "ScopeRevoked", new { Id = id }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>Human-readable label of an application user (exports show labels; IDs stay in audit).</summary>
    public async Task<string> UserLabelAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string>(Cmd(
            "SELECT COALESCE(DisplayName, LoginName, N'Uygulama kullanıcısı') FROM security.Users WHERE UserId = @userId;", new { userId }, null, cancellationToken))
            ?? "Uygulama kullanıcısı";
    }

    private static string EscapeLike(string value) => value.Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal)
        .Replace("_", "[_]", StringComparison.Ordinal);
}
