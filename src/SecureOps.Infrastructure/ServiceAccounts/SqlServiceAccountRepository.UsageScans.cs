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
    /// <summary>Scans shown on one account detail (newest links first); the total is reported separately.</summary>
    private const int _usageScanLimit = 10;

    /// <summary>
    /// Stores the scan once per uploader and file hash (append-only) and links it to the account, optionally through an open
    /// request of that account. Returns the scan id and whether a new link was written (false = this file was already
    /// attached to this account; nothing changes).
    /// </summary>
    public async Task<SaResult<(Guid ScanId, bool Attached)>> AttachUsageScanAsync(Guid accountId, UsageScanUpload upload, Guid? requestId, SaActor actor,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        int anchor = await connection.ExecuteScalarAsync<int>(Cmd("""
            SELECT CASE WHEN OBJECT_ID(N'svcacct.UsageScans', N'U') IS NULL THEN -1
                WHEN NOT EXISTS (SELECT 1 FROM svcacct.Accounts WHERE Id = @accountId) THEN 0
                WHEN @requestId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM svcacct.WorkRequests WITH (UPDLOCK)
                    WHERE Id = @requestId AND AccountId = @accountId AND Status = 'Open') THEN 1 ELSE 2 END;
            """, new { accountId, requestId }, transaction, cancellationToken));
        if (anchor < 2)
        {
            return SaResult<(Guid, bool)>.Fail(anchor == 0 ? SaErrors.NotFound : SaErrors.Invalid, anchor switch { -1 => "scanSchema", 0 => null, _ => "requestId" });
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
    /// The newest scans attached to the account, each reduced to this account's searched name: its items, the expected-gMSA
    /// items and the per-server coverage. Also returns how many scans are attached in total. Before migration 030 is applied
    /// the scans are null (unknown), so binaries can be installed ahead of the schema without breaking the account detail.
    /// </summary>
    public async Task<(IReadOnlyList<UsageScanView>? Scans, int Total)> UsageScansAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd($"""
            IF OBJECT_ID(N'svcacct.UsageScanLinks', N'U') IS NULL
            BEGIN
                SELECT CAST(-1 AS int);
                RETURN;
            END;
            SELECT COUNT(*) FROM svcacct.UsageScanLinks WHERE AccountId = @accountId;
            SELECT TOP ({_usageScanLimit}) l.Id AS LinkId, l.ScanId, l.MatchedAccount, l.RequestId, l.LinkedAt, s.Purpose, s.ExpectedAccount, s.FileName, s.Sha256,
                s.Tool, s.CombinedAt, s.FirstScannedAt, s.LastScannedAt, s.RunStatement, COALESCE(u.DisplayName, u.LoginName, 'Kullanıcı') AS UploadedBy, s.UploadedAt
            FROM svcacct.UsageScanLinks l JOIN svcacct.UsageScans s ON s.Id = l.ScanId LEFT JOIN security.Users u ON u.UserId = s.UploadedBy
            WHERE l.AccountId = @accountId ORDER BY l.LinkedAt DESC, l.Id;
            SELECT v.ScanId, v.ServerName, v.Result, v.WindowsServices, v.ScheduledTasks, v.Iis, v.ScannedAt, v.Warnings
            FROM svcacct.UsageScanServers v
            WHERE v.ScanId IN (SELECT TOP ({_usageScanLimit}) ScanId FROM svcacct.UsageScanLinks WHERE AccountId = @accountId ORDER BY LinkedAt DESC, Id);
            SELECT i.ScanId, i.Id, i.ServerName, i.Role, i.ComponentType, i.ComponentName, i.ConfiguredIdentity, i.State, i.Detail, d.Decision, d.UsageId,
                d.Reason AS DecisionReason, d.DecidedAt
            FROM (SELECT TOP ({_usageScanLimit}) ScanId, MatchedAccount FROM svcacct.UsageScanLinks WHERE AccountId = @accountId ORDER BY LinkedAt DESC, Id) l
            JOIN svcacct.UsageScanItems i ON i.ScanId = l.ScanId AND (i.Role = 'Expected' OR i.MatchedAccount = l.MatchedAccount)
            LEFT JOIN svcacct.UsageScanDecisions d ON d.ItemId = i.Id AND d.AccountId = @accountId
            ORDER BY i.ServerName, i.Role DESC, i.ComponentType, i.ComponentName, i.Id;
            """, new { accountId }, null, cancellationToken));
        int total = await grid.ReadSingleAsync<int>();
        if (total < 0)
        {
            return (null, 0);
        }

        List<ScanLinkRow> links = [.. await grid.ReadAsync<ScanLinkRow>()];
        ILookup<Guid, ScanServerRow> servers = (await grid.ReadAsync<ScanServerRow>()).ToLookup(s => s.ScanId);
        ILookup<Guid, ScanItemRow> items = (await grid.ReadAsync<ScanItemRow>()).ToLookup(i => i.ScanId);
        return ([.. links.Select(l => ScanView(l, [.. servers[l.ScanId]], [.. items[l.ScanId]]))], total);
    }

    internal static UsageScanView ScanView(ScanLinkRow link, IReadOnlyList<ScanServerRow> serverRows, IReadOnlyList<ScanItemRow> itemRows)
    {
        bool gmsaCheck = link.Purpose == "GmsaCheck";
        List<UsageScanServerView> servers = [];
        List<ScanGmsaServerState> states = [];
        foreach (ScanServerRow row in serverRows.OrderBy(s => s.ServerName, StringComparer.OrdinalIgnoreCase))
        {
            ScanServerResult result = Enum.Parse<ScanServerResult>(row.Result);
            int former = itemRows.Count(i => i.Role == "Former" && string.Equals(i.ServerName, row.ServerName, StringComparison.OrdinalIgnoreCase));
            int gmsa = itemRows.Count(i => i.Role == "Expected" && string.Equals(i.ServerName, row.ServerName, StringComparison.OrdinalIgnoreCase));
            ScanAccountOutcome outcome = UsageScanOutcomes.Outcome(result, former);
            ScanGmsaServerState? state = gmsaCheck ? UsageScanOutcomes.GmsaState(result, former, gmsa) : null;
            if (state is { } s)
            {
                states.Add(s);
            }

            servers.Add(new UsageScanServerView(row.ServerName, row.Result, UsageScanOutcomes.ResultLabel(result), row.WindowsServices, row.ScheduledTasks, row.Iis,
                row.ScannedAt, row.Warnings, former, outcome.ToString(), UsageScanOutcomes.OutcomeLabel(outcome), state?.ToString(),
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

        UsageScanItemView[] items = [.. itemRows.Select(i => new UsageScanItemView(i.Id, i.ServerName, i.Role, i.ComponentType,
            UsageScanOutcomes.ComponentLabel(i.ComponentType), i.ComponentName, i.ConfiguredIdentity, i.State, i.Detail,
            UsageScanOutcomes.SuggestedKind(i.ComponentType, i.Detail).ToString(), i.Decision, i.UsageId, i.DecisionReason, i.DecidedAt))];
        return new UsageScanView(link.ScanId, link.LinkId, link.Purpose, link.MatchedAccount, link.ExpectedAccount, link.FileName, link.Sha256, link.Tool,
            link.CombinedAt, link.FirstScannedAt, link.LastScannedAt, link.RunStatement, link.UploadedBy, link.UploadedAt, link.RequestId, link.LinkedAt, coverage,
            gmsaView, servers, items);
    }

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
            return SaResult<Guid>.Fail(SaErrors.Invalid, "alreadyDecided");
        }

        Guid id = await decide(connection, transaction, item, now);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    internal sealed record ScanLinkRow(Guid LinkId, Guid ScanId, string MatchedAccount, Guid? RequestId, DateTimeOffset LinkedAt, string Purpose,
        string? ExpectedAccount, string FileName, string Sha256, string Tool, DateTimeOffset CombinedAt, DateTimeOffset? FirstScannedAt,
        DateTimeOffset? LastScannedAt, string RunStatement, string UploadedBy, DateTimeOffset UploadedAt);

    internal sealed record ScanServerRow(Guid ScanId, string ServerName, string Result, string? WindowsServices, string? ScheduledTasks, string? Iis,
        DateTimeOffset? ScannedAt, string? Warnings);

    internal sealed record ScanItemRow(Guid ScanId, Guid Id, string ServerName, string Role, string ComponentType, string ComponentName, string ConfiguredIdentity,
        string? State, string? Detail, string? Decision, Guid? UsageId, string? DecisionReason, DateTimeOffset? DecidedAt);

    private sealed record DecidableItem(Guid ScanId, string ServerName, string ComponentType, string ComponentName);
}
