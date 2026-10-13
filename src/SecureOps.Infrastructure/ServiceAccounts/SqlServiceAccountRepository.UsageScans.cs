using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>A validated usage-scan upload ready to store (ADR-0027).</summary>
public sealed record UsageScanUpload(ParsedUsageScan Parsed, byte[] Content, string Sha256, string FileName, string RunStatement, string MatchedAccount);

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>
    /// Stores the scan once per uploader and file hash (append-only) and links it to the account, optionally through an open
    /// request of that account. Returns the scan id and whether a new link was written (false = this file was already
    /// attached to this account; nothing changes).
    /// <para>
    /// The caller's authority is decided again inside the write transaction, after the service's own check: the account row
    /// and the request row are locked (UPDLOCK, HOLDLOCK) and the caller's active scope grants and the organization/team tree
    /// are read with HOLDLOCK, so a revocation, an owner-team or organization change, or a re-parented team either committed
    /// before (and is seen here) or waits until this link is committed. Outside the re-read scope is NotFound (same as
    /// missing); in scope but <paramref name="allowed"/> refuses (scope, account anchor, the request's target team) is
    /// Forbidden. Nothing is written in either case.
    /// </para>
    /// </summary>
    public async Task<SaResult<(Guid ScanId, bool Attached)>> AttachUsageScanAsync(Guid accountId, UsageScanUpload upload, Guid? requestId, SaActor actor,
        Func<ServiceAccountScope, AccountScopeAnchor, Guid?, bool> allowed, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        int state = await connection.ExecuteScalarAsync<int>(Cmd("""
            IF OBJECT_ID(N'svcacct.UsageScans', N'U') IS NULL
                SELECT -1;
            ELSE
                SELECT COUNT(*) FROM svcacct.Accounts WITH (UPDLOCK, HOLDLOCK) WHERE Id = @accountId;
            """, new { accountId }, transaction, cancellationToken));
        if (state < 1)
        {
            return SaResult<(Guid, bool)>.Fail(state == 0 ? SaErrors.NotFound : SaErrors.Invalid, state == 0 ? null : "scanTablesMissing");
        }

        Guid? requestTeam = null;
        if (requestId is not null)
        {
            (bool Open, Guid? TargetTeamId)? request = await connection.QuerySingleOrDefaultAsync<(bool Open, Guid? TargetTeamId)?>(Cmd("""
                SELECT CAST(CASE WHEN Status = 'Open' THEN 1 ELSE 0 END AS bit), TargetTeamId
                FROM svcacct.WorkRequests WITH (UPDLOCK, HOLDLOCK) WHERE Id = @requestId AND AccountId = @accountId;
                """, new { accountId, requestId }, transaction, cancellationToken));
            if (request is not { Open: true } open)
            {
                return SaResult<(Guid, bool)>.Fail(SaErrors.Invalid, "requestId");
            }

            requestTeam = open.TargetTeamId;
        }

        ServiceAccountScope scope;
        using (SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT ScopeKind, OrganizationId, TeamId FROM svcacct.ScopeGrants WITH (HOLDLOCK) WHERE UserId = @UserId AND RevokedAt IS NULL;
            SELECT Id, ParentId FROM svcacct.Organizations WITH (HOLDLOCK);
            SELECT Id, OrganizationId FROM svcacct.Teams WITH (HOLDLOCK);
            """, new { actor.UserId }, transaction, cancellationToken)))
        {
            ScopeGrant[] grants = [.. (await grid.ReadAsync<(string Kind, Guid? OrganizationId, Guid? TeamId)>())
                .Select(g => new ScopeGrant(Enum.Parse<ScopeKind>(g.Kind), g.OrganizationId, g.TeamId))];
            OrganizationNode[] orgs = [.. (await grid.ReadAsync<(Guid Id, Guid? ParentId)>()).Select(o => new OrganizationNode(o.Id, o.ParentId))];
            TeamNode[] teams = [.. (await grid.ReadAsync<(Guid Id, Guid? OrganizationId)>()).Select(t => new TeamNode(t.Id, t.OrganizationId))];
            scope = ServiceAccountScope.Resolve(grants, orgs, teams);
        }

        AccountScopeAnchor anchor = (await AnchorAsync(connection, transaction, accountId, cancellationToken))!.Value.Anchor;
        if (!scope.Covers(anchor))
        {
            return SaResult<(Guid, bool)>.Fail(SaErrors.NotFound);
        }

        if (!allowed(scope, anchor, requestTeam))
        {
            return SaResult<(Guid, bool)>.Fail(SaErrors.Forbidden, "requestId");
        }

        Guid? existing = await connection.QuerySingleOrDefaultAsync<Guid?>(Cmd("""
            SELECT Id FROM svcacct.UsageScans WITH (UPDLOCK, HOLDLOCK) WHERE UploadedBy = @UserId AND Sha256 = @Sha256;
            """, new { actor.UserId, upload.Sha256 }, transaction, cancellationToken));
        Guid scanId = existing ?? Guid.NewGuid();
        if (existing is null)
        {
            await InsertScanAsync(connection, transaction, scanId, upload, actor, now, cancellationToken);
        }
        else if (await connection.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM svcacct.UsageScanLinks WITH (UPDLOCK, HOLDLOCK) WHERE ScanId = @scanId AND AccountId = @accountId;
            """, new { scanId, accountId }, transaction, cancellationToken)) > 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return (scanId, false);
        }

        var linkId = Guid.NewGuid();
        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.UsageScanLinks(Id, ScanId, AccountId, MatchedAccount, RequestId, LinkedBy, LinkedAt)
            VALUES(@linkId, @scanId, @accountId, @MatchedAccount, @requestId, @UserId, @now);
            """, new { linkId, scanId, accountId, upload.MatchedAccount, requestId, actor.UserId, now }, transaction, cancellationToken));
        ParsedUsageScan parsed = upload.Parsed;
        await HistoryAsync(connection, transaction, "UsageScan", scanId, accountId, "UsageScanAttached", new
        {
            parsed.Purpose,
            Planned = parsed.Servers.Count,
            Answered = parsed.AnsweredServers,
            Matches = parsed.Items.Count(i => i.Role == "Former" && i.MatchedAccount == upload.MatchedAccount),
            RequestId = requestId,
            upload.Sha256
        }, null, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "UsageScanAttached", new { EntityType = "UsageScan", EntityId = scanId, AccountId = accountId, LinkId = linkId },
            actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (scanId, true);
    }

    private static async Task InsertScanAsync(SqlConnection connection, SqlTransaction transaction, Guid scanId, UsageScanUpload upload, SaActor actor,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        ParsedUsageScan parsed = upload.Parsed;
        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.UsageScans(Id, Sha256, FileName, SizeBytes, Content, Purpose, ExpectedAccount, AccountsJson, Tool, CombinedAt, FirstScannedAt,
                LastScannedAt, PlannedServers, AnsweredServers, RunStatement, UploadedBy, UploadedAt)
            VALUES(@scanId, @Sha256, @FileName, @SizeBytes, @Content, @Purpose, @ExpectedAccount, @AccountsJson, @Tool, @CombinedAt, @FirstScannedAt,
                @LastScannedAt, @PlannedServers, @AnsweredServers, @RunStatement, @UserId, @now);
            """, new
        {
            scanId,
            upload.Sha256,
            upload.FileName,
            SizeBytes = upload.Content.Length,
            upload.Content,
            parsed.Purpose,
            parsed.ExpectedAccount,
            AccountsJson = JsonSerializer.Serialize(parsed.Accounts),
            parsed.Tool,
            parsed.CombinedAt,
            parsed.FirstScannedAt,
            parsed.LastScannedAt,
            PlannedServers = parsed.Servers.Count,
            parsed.AnsweredServers,
            upload.RunStatement,
            actor.UserId,
            now
        }, transaction, cancellationToken, _commitTimeoutSeconds));
        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.UsageScanServers(ScanId, ServerName, Result, WindowsServices, ScheduledTasks, Iis, ScannedAt, Warnings)
            SELECT @scanId, ServerName, Result, WindowsServices, ScheduledTasks, Iis, ScannedAt, Warnings
            FROM OPENJSON(@servers) WITH (ServerName nvarchar(255), Result varchar(16), WindowsServices varchar(16), ScheduledTasks varchar(16),
                Iis varchar(16), ScannedAt datetimeoffset(7), Warnings nvarchar(1000));
            """, new
        {
            scanId,
            servers = JsonSerializer.Serialize(parsed.Servers.Select(s => new
            {
                s.ServerName,
                Result = s.Result.ToString(),
                s.WindowsServices,
                s.ScheduledTasks,
                s.Iis,
                s.ScannedAt,
                s.Warnings
            }))
        }, transaction, cancellationToken, _commitTimeoutSeconds));
        if (parsed.Items.Count > 0)
        {
            await connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.UsageScanItems(Id, ScanId, ServerName, Role, MatchedAccount, ComponentType, ComponentName, ConfiguredIdentity, State, Detail)
                SELECT Id, @scanId, ServerName, Role, MatchedAccount, ComponentType, ComponentName, ConfiguredIdentity, State, Detail
                FROM OPENJSON(@items) WITH (Id uniqueidentifier, ServerName nvarchar(255), Role varchar(16), MatchedAccount nvarchar(100),
                    ComponentType varchar(24), ComponentName nvarchar(1024), ConfiguredIdentity nvarchar(256), State nvarchar(64), Detail nvarchar(1024));
                """, new
            {
                scanId,
                items = JsonSerializer.Serialize(parsed.Items.Select(i => new
                {
                    Id = Guid.NewGuid(),
                    i.ServerName,
                    i.Role,
                    i.MatchedAccount,
                    i.ComponentType,
                    i.ComponentName,
                    ConfiguredIdentity = i.Identity,
                    i.State,
                    i.Detail
                }))
            }, transaction, cancellationToken, _commitTimeoutSeconds));
        }
    }

    /// <summary>
    /// One page of the scans attached to the account (newest links first), each reduced to this account's searched name.
    /// Per-server counts, coverage and the gMSA evidence are aggregated in SQL over every matched item, so paging the items
    /// never changes an outcome ("not found" still needs a fully scanned server); only the first page of each role's items
    /// is loaded. Also returns how many scans are attached and how many former-account items still wait for a decision
    /// across all of them. Before migration 030 is applied the scans are null (unknown), so binaries can be installed ahead
    /// of the schema without breaking the account detail.
    /// </summary>
    public async Task<(IReadOnlyList<UsageScanView>? Scans, int Total, int Pending)> UsageScansAsync(Guid accountId, int page, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            IF OBJECT_ID(N'svcacct.UsageScanLinks', N'U') IS NULL
            BEGIN
                SELECT CAST(-1 AS int);
                RETURN;
            END;
            SELECT COUNT(*) FROM svcacct.UsageScanLinks WHERE AccountId = @accountId;
            SELECT COUNT(*) FROM svcacct.UsageScanLinks l
            JOIN svcacct.UsageScanItems i ON i.ScanId = l.ScanId AND i.Role = 'Former' AND i.MatchedAccount = l.MatchedAccount
            WHERE l.AccountId = @accountId
              AND NOT EXISTS (SELECT 1 FROM svcacct.UsageScanDecisions d WHERE d.ItemId = i.Id AND d.AccountId = @accountId);
            DECLARE @page TABLE(LinkId uniqueidentifier NOT NULL PRIMARY KEY, ScanId uniqueidentifier NOT NULL, MatchedAccount nvarchar(100) NOT NULL, Ord bigint NOT NULL);
            INSERT INTO @page(LinkId, ScanId, MatchedAccount, Ord)
            SELECT Id, ScanId, MatchedAccount, ROW_NUMBER() OVER (ORDER BY LinkedAt DESC, Id) FROM svcacct.UsageScanLinks WHERE AccountId = @accountId
            ORDER BY LinkedAt DESC, Id OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            SELECT l.Id AS LinkId, l.ScanId, l.MatchedAccount, l.RequestId, l.LinkedAt, s.Purpose, s.ExpectedAccount, s.FileName, s.Sha256,
                s.Tool, s.CombinedAt, s.FirstScannedAt, s.LastScannedAt, s.RunStatement, COALESCE(u.DisplayName, u.LoginName, 'Kullanıcı') AS UploadedBy, s.UploadedAt
            FROM @page p JOIN svcacct.UsageScanLinks l ON l.Id = p.LinkId JOIN svcacct.UsageScans s ON s.Id = l.ScanId
            LEFT JOIN security.Users u ON u.UserId = s.UploadedBy ORDER BY p.Ord;
            SELECT v.ScanId, v.ServerName, v.Result, v.WindowsServices, v.ScheduledTasks, v.Iis, v.ScannedAt, v.Warnings,
                COALESCE(c.Former, 0) AS Former, COALESCE(c.Expected, 0) AS Expected, COALESCE(c.Pending, 0) AS Pending
            FROM @page p JOIN svcacct.UsageScanServers v ON v.ScanId = p.ScanId
            LEFT JOIN (
                SELECT i.ScanId, i.ServerName,
                    SUM(CASE WHEN i.Role = 'Former' AND i.MatchedAccount = q.MatchedAccount THEN 1 ELSE 0 END) AS Former,
                    SUM(CASE WHEN i.Role = 'Expected' THEN 1 ELSE 0 END) AS Expected,
                    SUM(CASE WHEN i.Role = 'Former' AND i.MatchedAccount = q.MatchedAccount AND d.Id IS NULL THEN 1 ELSE 0 END) AS Pending
                FROM @page q JOIN svcacct.UsageScanItems i ON i.ScanId = q.ScanId
                LEFT JOIN svcacct.UsageScanDecisions d ON d.ItemId = i.Id AND d.AccountId = @accountId
                GROUP BY i.ScanId, i.ServerName) c ON c.ScanId = v.ScanId AND c.ServerName = v.ServerName;
            SELECT x.ScanId, x.Id, x.ServerName, x.Role, x.ComponentType, x.ComponentName, x.ConfiguredIdentity, x.State, x.Detail, x.Decision, x.UsageId,
                x.DecisionReason, x.DecidedAt
            FROM (
                SELECT p.ScanId, i.Id, i.ServerName, i.Role, i.ComponentType, i.ComponentName, i.ConfiguredIdentity, i.State, i.Detail, d.Decision, d.UsageId,
                    d.Reason AS DecisionReason, d.DecidedAt,
                    ROW_NUMBER() OVER (PARTITION BY p.ScanId, i.Role ORDER BY CASE WHEN i.Role = 'Former' AND d.Id IS NULL THEN 0 ELSE 1 END,
                        i.ServerName, i.ComponentType, i.ComponentName, i.Id) AS n
                FROM @page p JOIN svcacct.UsageScanItems i ON i.ScanId = p.ScanId AND (i.Role = 'Expected' OR i.MatchedAccount = p.MatchedAccount)
                LEFT JOIN svcacct.UsageScanDecisions d ON d.ItemId = i.Id AND d.AccountId = @accountId) x
            WHERE x.n <= @itemTake
            ORDER BY x.ScanId, x.Role DESC, x.n;
            """, new { accountId, skip = (page - 1) * UsageScanPaging.ScanPageSize, take = UsageScanPaging.ScanPageSize, itemTake = UsageScanPaging.DefaultItemPageSize },
            null, cancellationToken));
        int total = await grid.ReadSingleAsync<int>();
        if (total < 0)
        {
            return (null, 0, 0);
        }

        int pending = await grid.ReadSingleAsync<int>();
        List<ScanLinkRow> links = [.. await grid.ReadAsync<ScanLinkRow>()];
        ILookup<Guid, ScanServerRow> servers = (await grid.ReadAsync<ScanServerRow>()).ToLookup(s => s.ScanId);
        ILookup<Guid, ScanItemRow> items = (await grid.ReadAsync<ScanItemRow>()).ToLookup(i => i.ScanId);
        return ([.. links.Select(l => ScanView(l, [.. servers[l.ScanId]], [.. items[l.ScanId]]))], total, pending);
    }

    /// <summary>
    /// One page of a linked scan's items for this account (<c>Former</c>: undecided first; <c>Expected</c>: the expected gMSA).
    /// The page is null when the link does not belong to the account (the caller's scope was checked on the account);
    /// <c>Missing</c> is true before migration 030.
    /// </summary>
    public async Task<(bool Missing, UsageScanItemPage? Page)> UsageScanItemsAsync(Guid accountId, Guid linkId, string role, bool pendingOnly, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            IF OBJECT_ID(N'svcacct.UsageScanLinks', N'U') IS NULL
            BEGIN
                SELECT CAST(-1 AS int);
                RETURN;
            END;
            DECLARE @scan uniqueidentifier, @matched nvarchar(100);
            SELECT @scan = ScanId, @matched = MatchedAccount FROM svcacct.UsageScanLinks WHERE Id = @linkId AND AccountId = @accountId;
            IF @scan IS NULL
            BEGIN
                SELECT CAST(0 AS int);
                RETURN;
            END;
            SELECT CAST(1 AS int);
            SELECT COUNT(*) FROM svcacct.UsageScanItems i LEFT JOIN svcacct.UsageScanDecisions d ON d.ItemId = i.Id AND d.AccountId = @accountId
            WHERE i.ScanId = @scan AND i.Role = @role AND (@role = 'Expected' OR i.MatchedAccount = @matched) AND (@pendingOnly = 0 OR d.Id IS NULL);
            SELECT i.ScanId, i.Id, i.ServerName, i.Role, i.ComponentType, i.ComponentName, i.ConfiguredIdentity, i.State, i.Detail, d.Decision, d.UsageId,
                d.Reason AS DecisionReason, d.DecidedAt
            FROM svcacct.UsageScanItems i LEFT JOIN svcacct.UsageScanDecisions d ON d.ItemId = i.Id AND d.AccountId = @accountId
            WHERE i.ScanId = @scan AND i.Role = @role AND (@role = 'Expected' OR i.MatchedAccount = @matched) AND (@pendingOnly = 0 OR d.Id IS NULL)
            ORDER BY CASE WHEN i.Role = 'Former' AND d.Id IS NULL THEN 0 ELSE 1 END, i.ServerName, i.ComponentType, i.ComponentName, i.Id
            OFFSET @skip ROWS FETCH NEXT @pageSize ROWS ONLY;
            """, new { accountId, linkId, role, pendingOnly, skip = (page - 1) * pageSize, pageSize }, null, cancellationToken));
        int state = await grid.ReadSingleAsync<int>();
        if (state <= 0)
        {
            return (state < 0, null);
        }

        int total = await grid.ReadSingleAsync<int>();
        UsageScanItemView[] rows = [.. (await grid.ReadAsync<ScanItemRow>()).Select(ItemView)];
        return (false, new UsageScanItemPage(rows, total, page, pageSize, role, pendingOnly));
    }

    /// <summary>
    /// Everything needed to compare a linked scan with an older scan of the same account and purpose (G-33): both links, every
    /// planned server with its former-account match count, and every former-account item matched to each link's searched name.
    /// Bounded by the upload limits (500 servers, 10 000 components per scan). Read only. <c>Missing</c> is true before migration
    /// 030; <c>Error</c> is <c>linkId</c> / <c>against</c> (unknown for this account: not found) or <c>againstPurpose</c>.
    /// </summary>
    public Task<(bool Missing, string? Error, ScanDiffData? Data)> UsageScanDiffDataAsync(Guid accountId, Guid linkId, Guid? againstLinkId,
        CancellationToken cancellationToken) =>
        RetryReadOnDeadlockAsync(() => UsageScanDiffDataOnceAsync(accountId, linkId, againstLinkId, cancellationToken));

    private async Task<(bool Missing, string? Error, ScanDiffData? Data)> UsageScanDiffDataOnceAsync(Guid accountId, Guid linkId, Guid? againstLinkId,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        if (await connection.ExecuteScalarAsync<int>(Cmd("SELECT CASE WHEN OBJECT_ID(N'svcacct.UsageScanLinks', N'U') IS NULL THEN 0 ELSE 1 END;", null, null,
            cancellationToken)) == 0)
        {
            return (true, null, null);
        }

        const string side = """
            SELECT l.Id AS LinkId, l.ScanId, l.MatchedAccount, l.LinkedAt, s.Purpose, s.FileName, s.LastScannedAt, s.ExpectedAccount
            FROM svcacct.UsageScanLinks l JOIN svcacct.UsageScans s ON s.Id = l.ScanId
            """;
        ScanDiffLink? current = await connection.QuerySingleOrDefaultAsync<ScanDiffLink>(Cmd(side + " WHERE l.Id = @linkId AND l.AccountId = @accountId;",
            new { linkId, accountId }, null, cancellationToken));
        if (current is null)
        {
            return (false, "linkId", null);
        }

        ScanDiffLink? previous;
        if (againstLinkId is { } against)
        {
            previous = against == linkId ? null : await connection.QuerySingleOrDefaultAsync<ScanDiffLink>(Cmd(side + " WHERE l.Id = @against AND l.AccountId = @accountId;",
                new { against, accountId }, null, cancellationToken));
            if (previous is null)
            {
                return (false, "against", null);
            }

            if (previous.Purpose != current.Purpose)
            {
                return (false, "againstPurpose", null);
            }
        }
        else
        {
            // The next older link in the order the account detail uses (LinkedAt DESC, Id).
            previous = await connection.QuerySingleOrDefaultAsync<ScanDiffLink>(Cmd(side + """
                 WHERE l.AccountId = @accountId AND s.Purpose = @Purpose AND l.Id <> @LinkId
                   AND (l.LinkedAt < @LinkedAt OR (l.LinkedAt = @LinkedAt AND l.Id > @LinkId))
                ORDER BY l.LinkedAt DESC, l.Id OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;
                """, new { accountId, current.Purpose, current.LinkId, current.LinkedAt }, null, cancellationToken));
        }

        if (previous is null)
        {
            return (false, null, new ScanDiffData(current, null, [], [], [], []));
        }

        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT v.ScanId, v.ServerName, v.Result,
                (SELECT COUNT(*) FROM svcacct.UsageScanItems i WHERE i.ScanId = v.ScanId AND i.ServerName = v.ServerName AND i.Role = 'Former'
                    AND i.MatchedAccount = CASE WHEN v.ScanId = @curScan THEN @curMatched ELSE @prevMatched END) AS Former,
                (SELECT COUNT(*) FROM svcacct.UsageScanItems i WHERE i.ScanId = v.ScanId AND i.ServerName = v.ServerName AND i.Role = 'Expected') AS Expected
            FROM svcacct.UsageScanServers v WHERE v.ScanId IN (@curScan, @prevScan);
            SELECT i.ScanId, i.ServerName, i.ComponentType, i.ComponentName, i.ConfiguredIdentity
            FROM svcacct.UsageScanItems i
            WHERE i.Role = 'Former' AND ((i.ScanId = @curScan AND i.MatchedAccount = @curMatched) OR (i.ScanId = @prevScan AND i.MatchedAccount = @prevMatched));
            """, new
        {
            curScan = current.ScanId,
            curMatched = current.MatchedAccount,
            prevScan = previous.ScanId,
            prevMatched = previous.MatchedAccount
        }, null, cancellationToken));
        DiffServerRow[] servers = [.. await grid.ReadAsync<DiffServerRow>()];
        DiffItemRow[] items = [.. await grid.ReadAsync<DiffItemRow>()];

        // The same scan file can be linked twice only to different accounts (UQ ScanId, AccountId), so both sides differ here.
        return (false, null, new ScanDiffData(current, previous, [.. servers.Where(s => s.ScanId == current.ScanId)], [.. items.Where(i => i.ScanId == current.ScanId)],
            [.. servers.Where(s => s.ScanId == previous.ScanId)], [.. items.Where(i => i.ScanId == previous.ScanId)]));
    }

    internal static UsageScanView ScanView(ScanLinkRow link, IReadOnlyList<ScanServerRow> serverRows, IReadOnlyList<ScanItemRow> itemRows)
    {
        bool gmsaCheck = link.Purpose == "GmsaCheck";
        List<UsageScanServerView> servers = [];
        List<ScanGmsaServerState> states = [];
        foreach (ScanServerRow row in serverRows.OrderBy(s => s.ServerName, StringComparer.OrdinalIgnoreCase))
        {
            ScanServerResult result = Enum.Parse<ScanServerResult>(row.Result);
            ScanAccountOutcome outcome = UsageScanOutcomes.Outcome(result, row.Former);
            ScanGmsaServerState? state = gmsaCheck ? UsageScanOutcomes.GmsaState(result, row.Former, row.Expected) : null;
            if (state is { } s)
            {
                states.Add(s);
            }

            servers.Add(new UsageScanServerView(row.ServerName, row.Result, UsageScanOutcomes.ResultLabel(result), row.WindowsServices, row.ScheduledTasks, row.Iis,
                row.ScannedAt, row.Warnings, row.Former, outcome.ToString(), UsageScanOutcomes.OutcomeLabel(outcome), state?.ToString(),
                state is { } label ? UsageScanOutcomes.GmsaStateLabel(label) : null));
        }

        UsageScanCoverageView coverage = new(servers.Count, Count(servers, s => s.Result, "Success"), Count(servers, s => s.Result, "Partial"),
            Count(servers, s => s.Result, "Failed"), Count(servers, s => s.Result, "Unreachable"), Count(servers, s => s.Result, "NoResult"),
            Count(servers, s => s.Outcome, "Found"), Count(servers, s => s.Outcome, "NotFound"), Count(servers, s => s.Outcome, "Uncertain"),
            Count(servers, s => s.Outcome, "NotCovered"));
        UsageScanGmsaView? gmsaView = null;
        if (gmsaCheck)
        {
            ScanGmsaConclusion conclusion = UsageScanOutcomes.Conclusion(states);
            gmsaView = new UsageScanGmsaView(link.ExpectedAccount!, conclusion.ToString(), UsageScanOutcomes.ConclusionLabel(conclusion),
                states.Count(s => s == ScanGmsaServerState.StillFormer), states.Count(s => s == ScanGmsaServerState.RunsAsGmsa),
                states.Count(s => s == ScanGmsaServerState.NoComponents), states.Count(s => s == ScanGmsaServerState.Unknown));
        }

        return new UsageScanView(link.ScanId, link.LinkId, link.Purpose, link.MatchedAccount, link.ExpectedAccount, link.FileName, link.Sha256, link.Tool,
            link.CombinedAt, link.FirstScannedAt, link.LastScannedAt, link.RunStatement, link.UploadedBy, link.UploadedAt, link.RequestId, link.LinkedAt, coverage,
            gmsaView, servers, [.. itemRows.Select(ItemView)], serverRows.Sum(s => s.Former), serverRows.Sum(s => s.Pending), serverRows.Sum(s => s.Expected));
    }

    private static UsageScanItemView ItemView(ScanItemRow i) => new(i.Id, i.ServerName, i.Role, i.ComponentType, UsageScanOutcomes.ComponentLabel(i.ComponentType),
        i.ComponentName, i.ConfiguredIdentity, i.State, i.Detail, UsageScanOutcomes.SuggestedKind(i.ComponentType, i.Detail).ToString(), i.Decision, i.UsageId,
        i.DecisionReason, i.DecidedAt);

    private static int Count(IEnumerable<UsageScanServerView> servers, Func<UsageScanServerView, string> field, string value) =>
        servers.Count(s => field(s) == value);

    /// <summary>
    /// Records one matched component of an attached scan as a usage (a person's decision): the item must belong to a scan
    /// linked to this account under the same searched name, and be undecided for this account.
    /// </summary>
    public Task<SaResult<Guid>> RecordScanUsageAsync(Guid accountId, Guid itemId, UsageKind kind, DatabaseEngine? engine, bool? needVerified, string? notes,
        SaActor actor, CancellationToken cancellationToken) =>
        DecideScanItemAsync(accountId, itemId, actor, cancellationToken, async (connection, transaction, item, now) =>
        {
            var usageId = Guid.NewGuid();
            string component = Truncate($"{UsageScanOutcomes.ComponentLabel(item.ComponentType)}: {item.ComponentName}", 256)!;
            await connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.AccountUsages(Id, AccountId, UsageKind, DatabaseEngine, NeedVerified, Server, Component, Notes, Source, CreatedAt, CreatedBy,
                    UpdatedAt, UpdatedBy)
                VALUES(@usageId, @accountId, @Kind, @Engine, @needVerified, @ServerName, @component, @Notes, 'Manual', @now, @UserId, @now, @UserId);
                INSERT INTO svcacct.UsageScanDecisions(Id, ItemId, AccountId, Decision, UsageId, Reason, DecidedBy, DecidedAt)
                VALUES(NEWID(), @itemId, @accountId, 'UsageRecorded', @usageId, NULL, @UserId, @now);
                """, new
            {
                usageId,
                accountId,
                Kind = kind.ToString(),
                Engine = engine?.ToString(),
                needVerified,
                item.ServerName,
                component,
                Notes = ServiceAccountText.Clean(notes),
                now,
                actor.UserId,
                itemId
            }, transaction, cancellationToken));
            await HistoryAsync(connection, transaction, "Usage", usageId, accountId, "UsageRecordedFromScan", new
            {
                Kind = kind.ToString(),
                Engine = engine?.ToString(),
                needVerified,
                ScanItemId = itemId,
                item.ScanId
            }, notes, actor, now, cancellationToken);
            await AuditAsync(connection, transaction, "UsageRecordedFromScan", new { EntityType = "Usage", EntityId = usageId, AccountId = accountId, ScanItemId = itemId },
                actor, now, cancellationToken);
            return usageId;
        });

    /// <summary>Leaves one matched component out of the usage records, with a reason (a person's decision, kept).</summary>
    public Task<SaResult<Guid>> DismissScanItemAsync(Guid accountId, Guid itemId, string reason, SaActor actor, CancellationToken cancellationToken) =>
        DecideScanItemAsync(accountId, itemId, actor, cancellationToken, async (connection, transaction, item, now) =>
        {
            await connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.UsageScanDecisions(Id, ItemId, AccountId, Decision, UsageId, Reason, DecidedBy, DecidedAt)
                VALUES(NEWID(), @itemId, @accountId, 'Dismissed', NULL, @reason, @UserId, @now);
                """, new { itemId, accountId, reason, actor.UserId, now }, transaction, cancellationToken));
            await HistoryAsync(connection, transaction, "UsageScanItem", itemId, accountId, "UsageScanItemDismissed", new { item.ScanId }, reason, actor, now,
                cancellationToken);
            await AuditAsync(connection, transaction, "UsageScanItemDismissed", new { EntityType = "UsageScanItem", EntityId = itemId, AccountId = accountId },
                actor, now, cancellationToken);
            return itemId;
        });

    private async Task<SaResult<Guid>> DecideScanItemAsync(Guid accountId, Guid itemId, SaActor actor, CancellationToken cancellationToken,
        Func<SqlConnection, SqlTransaction, DecidableItem, DateTimeOffset, Task<Guid>> decide)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        DecidableItem? item = await connection.QuerySingleOrDefaultAsync<DecidableItem>(Cmd("""
            SELECT i.ScanId, i.ServerName, i.ComponentType, i.ComponentName FROM svcacct.UsageScanItems i
            JOIN svcacct.UsageScanLinks l ON l.ScanId = i.ScanId AND l.AccountId = @accountId AND l.MatchedAccount = i.MatchedAccount
            WHERE i.Id = @itemId AND i.Role = 'Former';
            """, new { accountId, itemId }, transaction, cancellationToken));
        if (item is null)
        {
            return SaResult<Guid>.Fail(SaErrors.NotFound, "itemId");
        }

        if (await connection.ExecuteScalarAsync<int>(Cmd("""
            SELECT COUNT(*) FROM svcacct.UsageScanDecisions WITH (UPDLOCK, HOLDLOCK) WHERE ItemId = @itemId AND AccountId = @accountId;
            """, new { itemId, accountId }, transaction, cancellationToken)) > 0)
        {
            return SaResult<Guid>.Fail(SaErrors.AlreadyDecided, "alreadyDecided");
        }

        Guid id = await decide(connection, transaction, item, now);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    internal sealed record ScanLinkRow(Guid LinkId, Guid ScanId, string MatchedAccount, Guid? RequestId, DateTimeOffset LinkedAt, string Purpose,
        string? ExpectedAccount, string FileName, string Sha256, string Tool, DateTimeOffset CombinedAt, DateTimeOffset? FirstScannedAt,
        DateTimeOffset? LastScannedAt, string RunStatement, string UploadedBy, DateTimeOffset UploadedAt);

    internal sealed record ScanServerRow(Guid ScanId, string ServerName, string Result, string? WindowsServices, string? ScheduledTasks, string? Iis,
        DateTimeOffset? ScannedAt, string? Warnings, int Former, int Expected, int Pending);

    internal sealed record ScanItemRow(Guid ScanId, Guid Id, string ServerName, string Role, string ComponentType, string ComponentName, string ConfiguredIdentity,
        string? State, string? Detail, string? Decision, Guid? UsageId, string? DecisionReason, DateTimeOffset? DecidedAt);

    /// <summary>One side of a scan comparison as stored.</summary>
    public sealed record ScanDiffLink(Guid LinkId, Guid ScanId, string MatchedAccount, DateTimeOffset LinkedAt, string Purpose, string FileName,
        DateTimeOffset? LastScannedAt, string? ExpectedAccount);

    /// <summary>A planned server with its match counts for the side's searched name.</summary>
    public sealed record DiffServerRow(Guid ScanId, string ServerName, string Result, int Former, int Expected);

    /// <summary>A former-account item matched to the side's searched name.</summary>
    public sealed record DiffItemRow(Guid ScanId, string ServerName, string ComponentType, string ComponentName, string ConfiguredIdentity);

    /// <summary>Raw comparison input; <c>Previous</c> null when there is no older scan of the same purpose.</summary>
    public sealed record ScanDiffData(ScanDiffLink Current, ScanDiffLink? Previous, IReadOnlyList<DiffServerRow> CurrentServers,
        IReadOnlyList<DiffItemRow> CurrentItems, IReadOnlyList<DiffServerRow> PreviousServers, IReadOnlyList<DiffItemRow> PreviousItems);

    private sealed record DecidableItem(Guid ScanId, string ServerName, string ComponentType, string ComponentName);
}
