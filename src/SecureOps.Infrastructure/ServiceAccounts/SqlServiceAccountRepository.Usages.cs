using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>Usages of one account, active first; removed usages stay listed with their reason.</summary>
    public async Task<IReadOnlyList<UsageView>> UsagesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<UsageRow>(Cmd("""
            SELECT Id, UsageKind, DatabaseEngine, NeedVerified, Server, Component, Notes, ExceptionReason, ExceptionAt, RemovedAt, RemovedReason, CreatedAt, RowVer
            FROM svcacct.AccountUsages WHERE AccountId = @accountId
            ORDER BY CASE WHEN RemovedAt IS NULL THEN 0 ELSE 1 END, CreatedAt, Id;
            """, new { accountId }, null, cancellationToken)))
            .Select(u => new UsageView(u.Id, u.UsageKind, ServiceAccountUsageRules.UsageLabel(Enum.Parse<UsageKind>(u.UsageKind)), u.DatabaseEngine, u.NeedVerified,
                u.Server, u.Component, u.Notes, u.ExceptionReason, u.ExceptionAt, u.RemovedAt is not null, u.RemovedReason, u.CreatedAt, Version(u.RowVer)))];
    }

    /// <summary>Records a usage for an existing account.</summary>
    public Task<SaResult<Guid>> CreateUsageAsync(Guid accountId, UsageKind kind, DatabaseEngine? engine, bool? needVerified, CreateUsageRequest request, SaActor actor,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        return MutateAsync(accountId, "Usage", id, "UsageRecorded", new { Kind = kind.ToString(), Engine = engine?.ToString(), needVerified }, request.Notes, actor,
            (connection, transaction, now) => connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.AccountUsages(Id, AccountId, UsageKind, DatabaseEngine, NeedVerified, Server, Component, Notes, Source, CreatedAt, CreatedBy,
                    UpdatedAt, UpdatedBy)
                SELECT @id, @accountId, @Kind, @Engine, @needVerified, @Server, @Component, @Notes, 'Manual', @now, @UserId, @now, @UserId
                WHERE EXISTS (SELECT 1 FROM svcacct.Accounts WHERE Id = @accountId);
                """, new { id, accountId, Kind = kind.ToString(), Engine = engine?.ToString(), needVerified, Server = ServiceAccountText.Clean(request.Server),
                Component = ServiceAccountText.Clean(request.Component), Notes = ServiceAccountText.Clean(request.Notes), now, actor.UserId }, transaction,
                cancellationToken)), cancellationToken);
    }

    /// <summary>Updates an active usage at the expected version; blank values keep the current value.</summary>
    public Task<SaResult<Guid>> UpdateUsageAsync(Guid accountId, Guid id, DatabaseEngine? engine, UpdateUsageRequest request, SaActor actor,
        CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Usage", id, "UsageUpdated", new { Engine = engine?.ToString(), request.NeedVerified }, request.Notes, actor,
            (connection, transaction, now) => connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.AccountUsages SET DatabaseEngine = CASE WHEN UsageKind = 'Database' THEN COALESCE(@Engine, DatabaseEngine) ELSE NULL END,
                    NeedVerified = CASE WHEN UsageKind = 'WindowsService' THEN COALESCE(@NeedVerified, NeedVerified) ELSE NULL END,
                    Server = COALESCE(@Server, Server), Component = COALESCE(@Component, Component), Notes = COALESCE(@Notes, Notes),
                    UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @id AND AccountId = @accountId AND RemovedAt IS NULL AND RowVer = @RowVer;
                """, new { Engine = engine?.ToString(), request.NeedVerified, Server = ServiceAccountText.Clean(request.Server),
                Component = ServiceAccountText.Clean(request.Component), Notes = ServiceAccountText.Clean(request.Notes), now, actor.UserId, id, accountId,
                RowVer = Version(request.ExpectedVersion) }, transaction, cancellationToken)), cancellationToken);

    /// <summary>Removes an active usage with a reason; the row is kept.</summary>
    public Task<SaResult<Guid>> RemoveUsageAsync(Guid accountId, Guid id, RemoveUsageRequest request, SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Usage", id, "UsageRemoved", null, request.Reason.Trim(), actor, (connection, transaction, now) => connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.AccountUsages SET RemovedAt = @now, RemovedBy = @UserId, RemovedReason = @Reason, UpdatedAt = @now, UpdatedBy = @UserId
            WHERE Id = @id AND AccountId = @accountId AND RemovedAt IS NULL AND RowVer = @RowVer;
            """, new { now, actor.UserId, Reason = request.Reason.Trim(), id, accountId, RowVer = Version(request.ExpectedVersion) }, transaction, cancellationToken)),
            cancellationToken);

    /// <summary>Records or clears a reasoned rule exception on an active usage.</summary>
    public Task<SaResult<Guid>> SetUsageExceptionAsync(Guid accountId, Guid id, UsageExceptionRequest request, SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Usage", id, request.Clear ? "UsageExceptionCleared" : "UsageExceptionRecorded", null, request.Reason.Trim(), actor,
            (connection, transaction, now) => connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.AccountUsages SET ExceptionReason = CASE WHEN @Clear = 1 THEN NULL ELSE @Reason END,
                    ExceptionBy = CASE WHEN @Clear = 1 THEN NULL ELSE @UserId END, ExceptionAt = CASE WHEN @Clear = 1 THEN NULL ELSE @now END,
                    UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @id AND AccountId = @accountId AND RemovedAt IS NULL AND RowVer = @RowVer
                  AND ((@Clear = 1 AND ExceptionReason IS NOT NULL) OR (@Clear = 0 AND ExceptionReason IS NULL));
                """, new { request.Clear, Reason = request.Reason.Trim(), now, actor.UserId, id, accountId, RowVer = Version(request.ExpectedVersion) }, transaction,
                cancellationToken)), cancellationToken);

    /// <summary>Active team roles.</summary>
    public async Task<IReadOnlyList<TeamRoleView>> TeamRolesAsync(CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. (await connection.QueryAsync<(Guid Id, Guid TeamId, string TeamName, string Role, string Reason, DateTimeOffset CreatedAt)>(Cmd("""
            SELECT r.Id, r.TeamId, t.Name, r.Role, r.Reason, r.CreatedAt FROM svcacct.TeamRoles r JOIN svcacct.Teams t ON t.Id = r.TeamId
            WHERE r.RevokedAt IS NULL ORDER BY r.Role, t.Name, r.Id;
            """, null, null, cancellationToken)))
            .Select(r => new TeamRoleView(r.Id, new SaRef(r.TeamId, r.TeamName), r.Role, r.Reason, r.CreatedAt))];
    }

    /// <summary>Assigns a team role; an active duplicate (or a second executor) is rejected by the unique indexes.</summary>
    public Task<SaResult<Guid>> CreateTeamRoleAsync(CreateTeamRoleRequest request, SaActor actor, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        return MutateAsync(null, "TeamRole", id, "TeamRoleAssigned", new { request.TeamId, request.Role }, request.Reason.Trim(), actor,
            (connection, transaction, now) => connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.TeamRoles(Id, TeamId, Role, Reason, CreatedAt, CreatedBy)
                SELECT @id, @TeamId, @Role, @Reason, @now, @UserId WHERE EXISTS (SELECT 1 FROM svcacct.Teams WHERE Id = @TeamId);
                """, new { id, request.TeamId, request.Role, Reason = request.Reason.Trim(), now, actor.UserId }, transaction, cancellationToken)),
            cancellationToken, duplicateField: "role");
    }

    /// <summary>Revokes an active team role with a reason.</summary>
    public Task<SaResult<Guid>> RevokeTeamRoleAsync(Guid id, string reason, SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(null, "TeamRole", id, "TeamRoleRevoked", null, reason.Trim(), actor, (connection, transaction, now) => connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.TeamRoles SET RevokedAt = @now, RevokedBy = @UserId, RevokedReason = @Reason WHERE Id = @id AND RevokedAt IS NULL;
            """, new { now, actor.UserId, Reason = reason.Trim(), id }, transaction, cancellationToken)), cancellationToken);

    private sealed record UsageRow(Guid Id, string UsageKind, string? DatabaseEngine, bool? NeedVerified, string? Server, string? Component, string? Notes,
        string? ExceptionReason, DateTimeOffset? ExceptionAt, DateTimeOffset? RemovedAt, string? RemovedReason, DateTimeOffset CreatedAt, byte[] RowVer);
}
