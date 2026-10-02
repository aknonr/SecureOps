using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Import;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

public sealed partial class SqlServiceAccountRepository
{
    // Column order matches the ImportBatchRecord constructor (Dapper positional record mapping).
    private static string BatchSelect(bool content) => $"""
        SELECT Id, Profile, FileName, ContentType, Sha256, {(content ? "Content" : "CAST(NULL AS varbinary(max)) AS Content")}, SourceReportDate,
            SourceDateProvenance, DeclaredScope, DeclaredDomain, MappingJson, MappingVersion, PreviewVersion, DecisionVersion, Status, ReplayKey,
            SummaryJson, ResultJson, CommitIdempotencyKey, UploadedBy, UploadedAt, CommittedAt FROM svcacct.ImportBatches
        """;

    /// <summary>Loads the current planning context (optionally inside the commit transaction).</summary>
    public async Task<ImportContext> LoadImportContextAsync(ServiceAccountScope scope, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await LoadImportContextAsync(connection, null, scope, cancellationToken);
    }

    private static async Task<ImportContext> LoadImportContextAsync(SqlConnection connection, SqlTransaction? transaction, ServiceAccountScope scope,
        CancellationToken cancellationToken)
    {
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT Id, IdentityKey, NormalizedName, NormalizedDomain, AccountName, ReportOrganizationId, CurrentOwnerTeamId, CurrentOwnerPersonId,
                ConsumerTeamId, Notes, RowVer, LastObservedOn FROM svcacct.Accounts;
            SELECT AccountId, TargetTeamId FROM svcacct.WorkRequests WHERE Status = 'Open' AND TargetTeamId IS NOT NULL;
            SELECT AccountId, TargetTeamId FROM svcacct.Handovers WHERE Status IN ('Proposed','Accepted');
            SELECT Id, NormalizedName, Name, OrganizationId FROM svcacct.Teams;
            SELECT Id, NormalizedName, Name, ParentId FROM svcacct.Organizations;
            SELECT Id, NormalizedName, DisplayName, VerificationState FROM svcacct.People;
            SELECT PersonId, AliasNormalized FROM svcacct.PersonAliases;
            SELECT 'Account|' + LegacyReference FROM svcacct.Accounts WHERE LegacyReference IS NOT NULL
            UNION ALL SELECT 'Request|' + LegacyReference FROM svcacct.WorkRequests WHERE LegacyReference IS NOT NULL
            UNION ALL SELECT 'Action|' + LegacyReference FROM svcacct.ActionEvents WHERE LegacyReference IS NOT NULL
            UNION ALL SELECT 'Communication|' + LegacyReference FROM svcacct.Communications WHERE LegacyReference IS NOT NULL
            UNION ALL SELECT 'Finding|' + LegacyReference FROM svcacct.Findings WHERE LegacyReference IS NOT NULL
            UNION ALL SELECT 'Handover|' + LegacyReference FROM svcacct.Handovers WHERE LegacyReference IS NOT NULL;
            SELECT SourceKey FROM svcacct.WorkRequests WHERE SourceKey IS NOT NULL
            UNION ALL SELECT SourceKey FROM svcacct.Handovers WHERE SourceKey IS NOT NULL
            UNION ALL SELECT SourceKey FROM svcacct.OwnershipAssignments WHERE SourceKey IS NOT NULL;
            SELECT c.LegacyReference, c.Id, ca.AccountId FROM svcacct.Communications c
                LEFT JOIN svcacct.CommunicationAccounts ca ON ca.CommunicationId = c.Id WHERE c.LegacyReference IS NOT NULL;
            SELECT AccountId, SourceProfile, SourceReportDate, Presence, PasswordLastSet, LastLogonAdOrLdap, LastLogonAd, Organization, GroupDirectorate,
                Comment, SourceTeam, ConsumerTeam, HandoverFlag
            FROM (SELECT *, ROW_NUMBER() OVER (PARTITION BY AccountId, SourceProfile ORDER BY SourceReportDate DESC, RecordedAt DESC) AS Rn
                  FROM svcacct.AccountObservations) o WHERE Rn = 1;
            SELECT AccountId FROM svcacct.IdentityTransitions WHERE Target = 'gMSA';
            SELECT TeamId, Role FROM svcacct.TeamRoles WHERE RevokedAt IS NULL;
            SELECT DISTINCT AccountId FROM svcacct.WorkRequests WHERE ActionType IN ('GmsaHandover','GmsaConversion');
            """, null, transaction, cancellationToken, _commitTimeoutSeconds));
        var accounts = (await grid.ReadAsync<AccountContextRow>()).ToList();
        ILookup<Guid, Guid> requestTeams = (await grid.ReadAsync<(Guid AccountId, Guid TeamId)>()).ToLookup(r => r.AccountId, r => r.TeamId);
        ILookup<Guid, Guid> handoverTeams = (await grid.ReadAsync<(Guid AccountId, Guid TeamId)>()).ToLookup(r => r.AccountId, r => r.TeamId);
        ContextNamed[] teams = [.. (await grid.ReadAsync<(Guid Id, string Key, string Name, Guid? Parent)>()).Select(t => new ContextNamed(t.Id, t.Key, t.Name, t.Parent))];
        ContextNamed[] orgs = [.. (await grid.ReadAsync<(Guid Id, string Key, string Name, Guid? Parent)>()).Select(t => new ContextNamed(t.Id, t.Key, t.Name, t.Parent))];
        var people = (await grid.ReadAsync<(Guid Id, string Key, string Name, string State)>()).ToList();
        ILookup<Guid, string> aliases = (await grid.ReadAsync<(Guid PersonId, string Key)>()).ToLookup(a => a.PersonId, a => a.Key);
        HashSet<string> legacy = [.. await grid.ReadAsync<string>()];
        HashSet<string> sourceKeys = [.. await grid.ReadAsync<string>()];
        var communications = (await grid.ReadAsync<(string Legacy, Guid Id, Guid? AccountId)>())
            .GroupBy(c => c.Legacy, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new ContextCommunication(g.First().Id, [.. g.Where(c => c.AccountId is not null).Select(c => c.AccountId!.Value)]),
                StringComparer.Ordinal);
        ContextObservation[] observations = [.. (await grid.ReadAsync<ObservationContextRow>()).Select(o => o.ToContext())];
        HashSet<Guid> gmsa = [.. await grid.ReadAsync<Guid>()];
        var roles = (await grid.ReadAsync<(Guid TeamId, string Role)>()).ToList();
        HashSet<Guid> gmsaRequests = [.. await grid.ReadAsync<Guid>()];
        return new ImportContext(
            [.. accounts.Select(a => new ContextAccount(a.Id, a.IdentityKey, a.NormalizedName, a.NormalizedDomain, a.AccountName, a.ReportOrganizationId,
                a.CurrentOwnerTeamId, a.CurrentOwnerPersonId, a.ConsumerTeamId, a.Notes, Version(a.RowVer), a.LastObservedOn,
                [.. requestTeams[a.Id]], [.. handoverTeams[a.Id]]))],
            teams, orgs,
            [.. people.Select(p => new ContextPerson(p.Id, p.Key, p.Name, [.. aliases[p.Id]], p.State == "Verified"))],
            legacy, sourceKeys, communications, observations, gmsa, scope,
            roles.Where(r => r.Role == ServiceAccountTeamRoles.SqlTeam).Select(r => r.TeamId).ToHashSet(),
            roles.Where(r => r.Role == ServiceAccountTeamRoles.GmsaExecutor).Select(r => (Guid?)r.TeamId).FirstOrDefault(), gmsaRequests);
    }

    /// <summary>Finds a committed batch with the same replay key.</summary>
    public async Task<ImportBatchRecord?> FindCommittedReplayAsync(string replayKey, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ImportBatchRecord>(Cmd(
            BatchSelect(false) + " WHERE ReplayKey = @replayKey AND Status = 'Committed';",
            new { replayKey }, null, cancellationToken));
    }

    /// <summary>Stores a new staged batch with its planned rows; history and audit in the same transaction.</summary>
    public async Task StageAsync(ImportBatchRecord batch, IReadOnlyList<ImportRowRecord> rows, SaActor actor, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken);
        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.ImportBatches(Id, Profile, FileName, ContentType, Sha256, Content, SourceReportDate, SourceDateProvenance, DeclaredScope,
                DeclaredDomain, MappingJson, MappingVersion, PreviewVersion, DecisionVersion, Status, ReplayKey, SummaryJson, UploadedBy, UploadedAt)
            VALUES(@Id, @Profile, @FileName, @ContentType, @Sha256, @Content, @SourceReportDate, @SourceDateProvenance, @DeclaredScope,
                @DeclaredDomain, @MappingJson, @MappingVersion, @PreviewVersion, @DecisionVersion, @Status, @ReplayKey, @SummaryJson, @UploadedBy, @UploadedAt);
            """, batch, transaction, cancellationToken, _commitTimeoutSeconds));
        await BulkRowsAsync(connection, transaction, batch.Id, rows, cancellationToken);
        await HistoryAsync(connection, transaction, "Import", batch.Id, null, "Staged", new { batch.Profile, batch.Sha256, Rows = rows.Count }, null, actor, batch.UploadedAt, cancellationToken);
        await AuditAsync(connection, transaction, "ImportStaged", new { BatchId = batch.Id, batch.Profile, batch.Sha256, Rows = rows.Count }, actor, batch.UploadedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Reads a batch (content only when requested).</summary>
    public async Task<ImportBatchRecord?> GetBatchAsync(Guid id, bool includeContent, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ImportBatchRecord>(Cmd(
            BatchSelect(includeContent) + " WHERE Id = @id;", new { id }, null, cancellationToken));
    }

    /// <summary>Lists recent batches.</summary>
    public async Task<IReadOnlyList<ImportBatchRecord>> ListBatchesAsync(int take, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<ImportBatchRecord>(Cmd(
            BatchSelect(false).Replace("SELECT Id,", "SELECT TOP (@take) Id,", StringComparison.Ordinal) + " ORDER BY UploadedAt DESC, Id;",
            new { take }, null, cancellationToken))];
    }

    /// <summary>Pages preview rows with stable ordering.</summary>
    public async Task<(IReadOnlyList<ImportRowRecord> Rows, int Total)> GetRowsAsync(Guid batchId, string? classification, string? kind,
        bool decisionsOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        const string filter = """
            WHERE BatchId = @batchId AND (@classification IS NULL OR Classification = @classification)
              AND (@kind IS NULL OR EntityKind = @kind) AND (@decisionsOnly = 0 OR RequiresDecision = 1)
            """;
        object parameters = new { batchId, classification, kind, decisionsOnly, skip = (page - 1) * pageSize, pageSize };
        int total = await connection.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM svcacct.ImportRows " + filter, parameters, null, cancellationToken));
        IEnumerable<ImportRowRecord> rows = await connection.QueryAsync<ImportRowRecord>(Cmd($"""
            SELECT RowKey, Sheet, RowNumber, EntityKind, OriginalJson, NormalizedJson, Classification, MatchAccountId, CandidatesJson, ErrorsJson, DiffJson,
                RequiresDecision, Decision, DecisionNote, DecidedBy, DecidedAt
            FROM svcacct.ImportRows {filter} ORDER BY RowKey OFFSET @skip ROWS FETCH NEXT @pageSize ROWS ONLY;
            """, parameters, null, cancellationToken));
        return ([.. rows], total);
    }

    /// <summary>Reads persisted decisions.</summary>
    public async Task<Dictionary<int, ImportRowDecision>> GetDecisionsAsync(Guid batchId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await DecisionsAsync(connection, null, batchId, cancellationToken);
    }

    private static async Task<Dictionary<int, ImportRowDecision>> DecisionsAsync(SqlConnection connection, SqlTransaction? transaction, Guid batchId,
        CancellationToken cancellationToken) =>
        (await connection.QueryAsync<(int RowKey, string Decision, string? Note)>(Cmd(
            "SELECT RowKey, Decision, DecisionNote FROM svcacct.ImportRows WHERE BatchId = @batchId AND Decision IS NOT NULL;",
            new { batchId }, transaction, cancellationToken)))
        .ToDictionary(d => d.RowKey, d =>
        {
            DecisionNote? note = d.Note is null ? null : JsonSerializer.Deserialize<DecisionNote>(d.Note);
            return new ImportRowDecision(d.RowKey, d.Decision, note?.Target, note?.Note);
        });

    /// <summary>Stored decision details.</summary>
    internal sealed record DecisionNote(Guid? Target, string? Note);

    /// <summary>
    /// Replaces the uncommitted plan rows (after decisions or a re-preview), bumping preview and decision versions.
    /// Stale versions return false.
    /// </summary>
    public async Task<bool> ReplacePlanAsync(Guid batchId, int expectedPreviewVersion, int expectedDecisionVersion, IReadOnlyList<ImportRowRecord> rows,
        string summaryJson, string action, SaActor actor, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken, IsolationLevel.Serializable);
        int updated = await connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.ImportBatches SET PreviewVersion = PreviewVersion + 1, DecisionVersion = DecisionVersion + 1, SummaryJson = @summaryJson,
                Status = 'Previewed'
            WHERE Id = @batchId AND Status IN ('Staged','Previewed') AND PreviewVersion = @expectedPreviewVersion AND DecisionVersion = @expectedDecisionVersion;
            """, new { batchId, summaryJson, expectedPreviewVersion, expectedDecisionVersion }, transaction, cancellationToken));
        if (updated != 1)
        {
            return false;
        }

        await connection.ExecuteAsync(Cmd("DELETE FROM svcacct.ImportRows WHERE BatchId = @batchId;", new { batchId }, transaction, cancellationToken, _commitTimeoutSeconds));
        await BulkRowsAsync(connection, transaction, batchId, rows, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await HistoryAsync(connection, transaction, "Import", batchId, null, action, new { Decisions = rows.Count(r => r.Decision is not null) }, null, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "Import" + action, new { BatchId = batchId, Decisions = rows.Count(r => r.Decision is not null) }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task BulkRowsAsync(SqlConnection connection, SqlTransaction transaction, Guid batchId, IReadOnlyList<ImportRowRecord> rows,
        CancellationToken cancellationToken)
    {
        using DataTable table = new();
        foreach ((string name, Type type) in new[]
        {
            ("BatchId", typeof(Guid)), ("RowKey", typeof(int)), ("Sheet", typeof(string)), ("RowNumber", typeof(int)), ("EntityKind", typeof(string)),
            ("OriginalJson", typeof(string)), ("NormalizedJson", typeof(string)), ("Classification", typeof(string)), ("MatchAccountId", typeof(Guid)),
            ("CandidatesJson", typeof(string)), ("ErrorsJson", typeof(string)), ("DiffJson", typeof(string)), ("RequiresDecision", typeof(bool)),
            ("Decision", typeof(string)), ("DecisionNote", typeof(string)), ("DecidedBy", typeof(Guid)), ("DecidedAt", typeof(DateTimeOffset))
        })
        {
            table.Columns.Add(name, type);
        }

        foreach (ImportRowRecord row in rows)
        {
            table.Rows.Add(batchId, row.RowKey, Truncate(row.Sheet, 64), row.RowNumber, row.EntityKind, row.OriginalJson, (object?)row.NormalizedJson ?? DBNull.Value,
                row.Classification, (object?)row.MatchAccountId ?? DBNull.Value, (object?)row.CandidatesJson ?? DBNull.Value, (object?)row.ErrorsJson ?? DBNull.Value,
                (object?)row.DiffJson ?? DBNull.Value, row.RequiresDecision, (object?)row.Decision ?? DBNull.Value, (object?)row.DecisionNote ?? DBNull.Value,
                (object?)row.DecidedBy ?? DBNull.Value, (object?)row.DecidedAt ?? DBNull.Value);
        }

        using SqlBulkCopy bulk = new(connection, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers, transaction)
        {
            DestinationTableName = "svcacct.ImportRows",
            BulkCopyTimeout = _commitTimeoutSeconds
        };
        foreach (DataColumn column in table.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(table, cancellationToken);
    }

    /// <summary>
    /// Atomic commit: application lock, batch lock, version check, in-transaction re-plan with fresh state,
    /// fingerprint comparison, business writes, batch result, history and audit in one transaction.
    /// A committed batch (or a committed twin with the same replay key) returns the stored result.
    /// </summary>
    public async Task<SaResult<ImportCommitOutcome>> CommitAsync(Guid batchId, ImportCommitRequest request, string idempotencyKey, SaActor actor,
        ServiceAccountScope scope, Func<ImportBatchRecord, ImportContext, IReadOnlyDictionary<int, ImportRowDecision>, ImportPlanResult> plan,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken, IsolationLevel.Serializable);
        await connection.ExecuteAsync(Cmd("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
            IF @result < 0 THROW 51310, 'Import commit lock unavailable.', 1;
            """, new { resource = _importCommitLock }, transaction, cancellationToken, _commitTimeoutSeconds));
        ImportBatchRecord? batch = await connection.QuerySingleOrDefaultAsync<ImportBatchRecord>(Cmd(
            BatchSelect(true) + " WITH (UPDLOCK, HOLDLOCK) WHERE Id = @batchId;", new { batchId }, transaction, cancellationToken));
        if (batch is null)
        {
            return SaResult<ImportCommitOutcome>.Fail(SaErrors.NotFound);
        }

        if (batch.Status == "Committed")
        {
            return new ImportCommitOutcome(JsonSerializer.Deserialize<ImportResultView>(batch.ResultJson!)!, true);
        }

        ImportBatchRecord? twin = await connection.QuerySingleOrDefaultAsync<ImportBatchRecord>(Cmd(
            BatchSelect(false) + " WHERE ReplayKey = @ReplayKey AND Status = 'Committed';", new { batch.ReplayKey }, transaction, cancellationToken));
        if (twin is not null)
        {
            return SaResult<ImportCommitOutcome>.Fail(SaErrors.AlreadyImported, current: JsonSerializer.Deserialize<ImportResultView>(twin.ResultJson!));
        }

        if (batch.PreviewVersion != request.PreviewVersion || batch.DecisionVersion != request.DecisionVersion)
        {
            return SaResult<ImportCommitOutcome>.Fail(SaErrors.PreviewStale, "previewVersion", new { batch.PreviewVersion, batch.DecisionVersion });
        }

        Dictionary<int, ImportRowDecision> decisions = await DecisionsAsync(connection, transaction, batchId, cancellationToken);
        Dictionary<int, (string Fingerprint, bool Required)> stored = (await connection.QueryAsync<(int RowKey, string DiffJson, bool Required)>(Cmd(
                "SELECT RowKey, DiffJson, RequiresDecision FROM svcacct.ImportRows WHERE BatchId = @batchId;", new { batchId }, transaction, cancellationToken, _commitTimeoutSeconds)))
            .ToDictionary(r => r.RowKey, r => (JsonDocument.Parse(r.DiffJson).RootElement.GetProperty("Fingerprint").GetString()!, r.Required));
        if (stored.Any(r => r.Value.Required && !decisions.ContainsKey(r.Key)))
        {
            return SaResult<ImportCommitOutcome>.Fail(SaErrors.DecisionsRequired, "decisions", stored.Count(r => r.Value.Required && !decisions.ContainsKey(r.Key)));
        }

        ImportContext context = await LoadImportContextAsync(connection, transaction, scope, cancellationToken);
        ImportPlanResult result = plan(batch, context, decisions);
        int stale = result.Rows.Count(r => !stored.TryGetValue(r.RowKey, out (string Fingerprint, bool Required) s) || s.Fingerprint != r.Fingerprint) + Math.Max(0, stored.Count - result.Rows.Count);
        if (stale > 0)
        {
            return SaResult<ImportCommitOutcome>.Fail(SaErrors.PreviewStale, "rows", stale);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ImportResultView view;
        try
        {
            view = await ExecuteWorkAsync(connection, transaction, batch, result.Work, actor, now, cancellationToken);
        }
        catch (StaleImportException)
        {
            // Disposing the transaction rolls back every write of this commit.
            return SaResult<ImportCommitOutcome>.Fail(SaErrors.PreviewStale, "rows");
        }

        await connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.ImportBatches SET Status = 'Committed', ResultJson = @ResultJson, CommitIdempotencyKey = @idempotencyKey,
                CommittedBy = @UserId, CommittedAt = @now WHERE Id = @batchId;
            """, new { ResultJson = JsonSerializer.Serialize(view), idempotencyKey, actor.UserId, now, batchId }, transaction, cancellationToken));
        await HistoryAsync(connection, transaction, "Import", batchId, null, "Committed", view, null, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, "ImportCommitted", new { BatchId = batchId, batch.Profile, batch.Sha256, view.AccountsCreated,
            view.ObservationsRecorded, view.RequestsCreated, view.ActionsCreated, view.CommunicationsCreated, view.HandoversCreated }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ImportCommitOutcome(view, false);
    }

    private sealed record AccountContextRow(Guid Id, string IdentityKey, string NormalizedName, string? NormalizedDomain, string AccountName,
        Guid? ReportOrganizationId, Guid? CurrentOwnerTeamId, Guid? CurrentOwnerPersonId, Guid? ConsumerTeamId, string? Notes, byte[] RowVer, DateOnly? LastObservedOn);

    private sealed record ObservationContextRow(Guid AccountId, string SourceProfile, DateTime? SourceReportDate, string Presence, DateTime? PasswordLastSet,
        DateTime? LastLogonAdOrLdap, DateTime? LastLogonAd, string? Organization, string? GroupDirectorate, string? Comment, string? SourceTeam,
        string? ConsumerTeam, string? HandoverFlag)
    {
        public ContextObservation ToContext() => new(AccountId, SourceProfile, SourceReportDate is { } d ? DateOnly.FromDateTime(d) : null, Presence,
            new Dictionary<string, string?>
            {
                [StagedFields.PasswordLastSet] = PasswordLastSet is { } p ? ImportValues.Naive(p) : null,
                [StagedFields.LastLogonAdOrLdap] = LastLogonAdOrLdap is { } l ? ImportValues.Naive(l) : null,
                [StagedFields.LastLogonAd] = LastLogonAd is { } a ? ImportValues.Naive(a) : null,
                [StagedFields.Organization] = Organization,
                [StagedFields.GroupDirectorate] = GroupDirectorate,
                [StagedFields.Comment] = Comment,
                [StagedFields.SourceTeam] = SourceTeam,
                [StagedFields.ConsumerTeam] = ConsumerTeam,
                [StagedFields.HandoverFlag] = HandoverFlag
            });
    }

    internal static string DateText(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}
