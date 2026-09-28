using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>Stored evidence file with its scope anchors.</summary>
public sealed record EvidenceFile(Guid Id, Guid? AccountId, Guid? ScopeTeamId, string FileName, string ContentType, byte[] Content, string Sha256);

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>
    /// Records one communication. A repeated provider message ID resolves to the same mail and only adds missing
    /// account links; two distinct messages with the same subject are both kept.
    /// </summary>
    public async Task<SaResult<(Guid Id, bool Existing, int Added)>> SaveCommunicationAsync(CreateCommunicationRequest request, CommunicationDirection direction,
        CommunicationKind kind, SaActor actor, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string? provider = ServiceAccountText.Clean(request.ProviderMessageId);
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken, IsolationLevel.Serializable);
        Guid? existing = provider is null ? null : await connection.QuerySingleOrDefaultAsync<Guid?>(Cmd(
            "SELECT Id FROM svcacct.Communications WITH (UPDLOCK, HOLDLOCK) WHERE ProviderMessageId = @provider;", new { provider }, transaction, cancellationToken));
        Guid id = existing ?? Guid.NewGuid();
        if (existing is null)
        {
            string precision = request.OccurredAt is not null ? "Instant" : request.OccurredOn is not null ? "DateOnly" : "Unknown";
            await connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.Communications(Id, ProviderMessageId, Direction, Kind, OccurredOn, OccurredAt, Precision, ContactTeamId, ContactPersonId,
                    Subject, Summary, Link, RecordScope, MeaningfulReply, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@id, @provider, @Direction, @Kind, @OccurredOn, @OccurredAt, @precision, @ContactTeamId, @ContactPersonId, @Subject, @Summary, @Link,
                    @Scope, @MeaningfulReply, @now, @UserId, @now, @UserId);
                """, new
            {
                id, provider, Direction = direction.ToString(), Kind = kind.ToString(),
                OccurredOn = request.OccurredAt is { } at ? ReportCalendar.LocalDate(at) : request.OccurredOn, request.OccurredAt, precision,
                request.ContactTeamId, request.ContactPersonId, Subject = ServiceAccountText.Clean(request.Subject), Summary = ServiceAccountText.Clean(request.Summary),
                Link = ServiceAccountText.Clean(request.Link), Scope = request.AccountIds.Count == 0 ? "Team" : "Account", request.MeaningfulReply, now, actor.UserId
            }, transaction, cancellationToken));
        }

        int added = await LinkAsync(connection, transaction, id, request.AccountIds, actor, now, cancellationToken);
        await ReferencesAsync(connection, transaction, request.References ?? [], "Communication", id, request.AccountIds.Count == 1 ? request.AccountIds[0] : null,
            actor, now, cancellationToken);
        foreach (Guid accountId in request.AccountIds.Distinct())
        {
            await HistoryAsync(connection, transaction, "Communication", id, accountId, existing is null ? "CommunicationRecorded" : "CommunicationLinked",
                new { Direction = direction.ToString(), Kind = kind.ToString(), request.OccurredOn }, null, actor, now, cancellationToken);
        }

        await AuditAsync(connection, transaction, existing is null ? "CommunicationRecorded" : "CommunicationLinked",
            new { EntityId = id, Accounts = request.AccountIds.Count, Added = added }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (id, existing is not null, added);
    }

    /// <summary>Reads one communication with its linked accounts.</summary>
    public async Task<CommunicationView?> CommunicationAsync(Guid id, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT c.Id, c.Direction, c.Kind, c.OccurredOn, c.OccurredAt, c.Precision, c.ContactTeamId, t.Name AS ContactTeamName, c.ContactPersonId,
                p.DisplayName AS ContactPersonName, c.Subject, c.Summary, c.Link, c.RecordScope, c.MeaningfulReply,
                CAST(CASE WHEN c.ProviderMessageId IS NULL THEN 0 ELSE 1 END AS bit) AS HasProvider, c.RowVer
            FROM svcacct.Communications c LEFT JOIN svcacct.Teams t ON t.Id = c.ContactTeamId LEFT JOIN svcacct.People p ON p.Id = c.ContactPersonId WHERE c.Id = @id;
            SELECT a.Id, a.AccountName FROM svcacct.CommunicationAccounts ca JOIN svcacct.Accounts a ON a.Id = ca.AccountId WHERE ca.CommunicationId = @id ORDER BY a.AccountName;
            """, new { id }, null, cancellationToken));
        CommunicationRow? row = await grid.ReadSingleOrDefaultAsync<CommunicationRow>();
        return row is null ? null : CommunicationView(row, [.. (await grid.ReadAsync<(Guid Id, string Name)>()).Select(a => new SaRef(a.Id, a.Name))]);
    }

    /// <summary>Records a technical finding (never an action or proof of non-use).</summary>
    public Task<SaResult<Guid>> CreateFindingAsync(CreateFindingRequest request, SaActor actor, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        return MutateAsync(request.AccountId, "Finding", id, "FindingRecorded", new { request.ScanResult, request.MatchResult, request.Status }, null, actor,
            (connection, transaction, now) => connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.Findings(Id, AccountId, Server, ComponentType, ComponentName, Environment, ScanAt, ScanOn, ScanResult, MatchResult, CoverageWindow,
                    EvidenceNote, OwningTeamId, Status, JobReference, Notes, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                SELECT @id, @AccountId, @Server, @ComponentType, @ComponentName, @Environment, @ScanAt, @ScanOn, @ScanResult, @MatchResult, @CoverageWindow,
                    @EvidenceNote, @OwningTeamId, @Status, @JobReference, @Notes, @now, @UserId, @now, @UserId
                WHERE EXISTS (SELECT 1 FROM svcacct.Accounts WHERE Id = @AccountId);
                """, new
            {
                id, request.AccountId, Server = ServiceAccountText.Clean(request.Server), ComponentType = ServiceAccountText.Clean(request.ComponentType),
                ComponentName = ServiceAccountText.Clean(request.ComponentName), Environment = ServiceAccountText.Clean(request.Environment), request.ScanAt,
                ScanOn = request.ScanAt is { } at ? ReportCalendar.LocalDate(at) : request.ScanOn, request.ScanResult, request.MatchResult,
                CoverageWindow = ServiceAccountText.Clean(request.CoverageWindow), EvidenceNote = ServiceAccountText.Clean(request.EvidenceNote), request.OwningTeamId,
                request.Status, JobReference = ServiceAccountText.Clean(request.JobReference), Notes = ServiceAccountText.Clean(request.Notes), now, actor.UserId
            }, transaction, cancellationToken)), cancellationToken);
    }

    /// <summary>Changes a finding status.</summary>
    public Task<SaResult<Guid>> UpdateFindingAsync(Guid accountId, Guid id, UpdateFindingRequest request, SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Finding", id, "FindingUpdated", new { request.Status }, request.Notes, actor, (connection, transaction, now) =>
            connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.Findings SET Status = @Status, Notes = COALESCE(@Notes, Notes), UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @id AND AccountId = @accountId AND RowVer = @RowVer;
                """, new { request.Status, Notes = ServiceAccountText.Clean(request.Notes), now, actor.UserId, id, accountId, RowVer = Version(request.ExpectedVersion) },
                transaction, cancellationToken)), cancellationToken);

    /// <summary>Proposes a handover (and optional gMSA tracking); proposal is not acceptance.</summary>
    public Task<SaResult<Guid>> CreateHandoverAsync(Guid accountId, CreateHandoverRequest request, SaActor actor, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        string sourceKey = "handover:" + accountId.ToString("N") + ":" + request.TargetTeamId.ToString("N");
        return MutateAsync(accountId, "Handover", id, "HandoverProposed", new { request.TargetTeamId, request.SourceTeamId, request.TrackGmsa }, request.Note, actor,
            async (connection, transaction, now) =>
            {
                int inserted = await connection.ExecuteAsync(Cmd("""
                    INSERT INTO svcacct.Handovers(Id, AccountId, SourceTeamId, TargetTeamId, ConsumerTeamId, CohortLabel, ProposedOn, Status, SourceNote, SourceKey,
                        CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                    SELECT @id, @accountId, @SourceTeamId, @TargetTeamId, @ConsumerTeamId, @CohortLabel, @ProposedOn, 'Proposed', @Note, @sourceKey, @now, @UserId, @now, @UserId
                    WHERE EXISTS (SELECT 1 FROM svcacct.Accounts WHERE Id = @accountId);
                    """, new { id, accountId, request.SourceTeamId, request.TargetTeamId, request.ConsumerTeamId, CohortLabel = ServiceAccountText.Clean(request.CohortLabel),
                    request.ProposedOn, Note = ServiceAccountText.Clean(request.Note), sourceKey, now, actor.UserId }, transaction, cancellationToken));
                if (inserted == 1 && request.TrackGmsa)
                {
                    await connection.ExecuteAsync(Cmd("""
                        IF NOT EXISTS (SELECT 1 FROM svcacct.IdentityTransitions WHERE AccountId = @accountId AND Target = 'gMSA')
                            INSERT INTO svcacct.IdentityTransitions(Id, AccountId, HandoverId, Target, Suitability, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                            VALUES(NEWID(), @accountId, @id, 'gMSA', 'Unknown', @now, @UserId, @now, @UserId);
                        """, new { accountId, id, now, actor.UserId }, transaction, cancellationToken));
                }

                return inserted;
            }, cancellationToken, duplicateField: "targetTeamId");
    }

    /// <summary>Accepts or rejects a proposed handover. Acceptance does not change ownership or gMSA status.</summary>
    public Task<SaResult<Guid>> DecideHandoverAsync(Guid accountId, Guid id, HandoverDecisionRequest request, bool accept, SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Handover", id, accept ? "HandoverAccepted" : "HandoverRejected", new { request.DecidedOn }, request.Note, actor, (connection, transaction, now) =>
            connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.Handovers SET Status = @Status, DecidedOn = @DecidedOn, DecidedBy = @UserId, DecisionNote = @Note, UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @id AND AccountId = @accountId AND Status = 'Proposed' AND RowVer = @RowVer;
                """, new { Status = accept ? "Accepted" : "Rejected", request.DecidedOn, actor.UserId, Note = request.Note.Trim(), now, id, accountId,
                RowVer = Version(request.ExpectedVersion) }, transaction, cancellationToken)), cancellationToken);

    /// <summary>Updates gMSA suitability/plan/completion; completion must reference a valid gMSA conversion action.</summary>
    public Task<SaResult<Guid>> UpdateTransitionAsync(Guid accountId, Guid id, TransitionUpdateRequest request, SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Transition", id, "TransitionUpdated", new { request.Suitability, request.PlannedOn, request.CompletedActionId }, request.DecisionNote, actor,
            (connection, transaction, now) => connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.IdentityTransitions SET Suitability = @Suitability, DecisionNote = COALESCE(@DecisionNote, DecisionNote),
                    DecidedBy = CASE WHEN @Suitability IN ('Eligible','Ineligible') THEN @UserId ELSE DecidedBy END,
                    DecidedAt = CASE WHEN @Suitability IN ('Eligible','Ineligible') THEN @now ELSE DecidedAt END,
                    PlannedOn = COALESCE(@PlannedOn, PlannedOn), CompletedActionId = COALESCE(@CompletedActionId, CompletedActionId), UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @id AND AccountId = @accountId AND RowVer = @RowVer
                  AND (@CompletedActionId IS NULL OR EXISTS (SELECT 1 FROM svcacct.ActionEvents e WHERE e.Id = @CompletedActionId AND e.AccountId = @accountId
                        AND e.ActionType = 'GmsaConversion' AND e.Result IN ('Performed','Verified') AND e.VoidedAt IS NULL));
                """, new { request.Suitability, DecisionNote = ServiceAccountText.Clean(request.DecisionNote), actor.UserId, now, request.PlannedOn, request.CompletedActionId,
                id, accountId, RowVer = Version(request.ExpectedVersion) }, transaction, cancellationToken)), cancellationToken);

    /// <summary>Stores immutable evidence bytes anchored to an account (or a team for team-scope communications).</summary>
    public Task<SaResult<Guid>> AddEvidenceAsync(string ownerType, Guid ownerId, Guid? accountId, Guid? scopeTeamId, string fileName, string contentType,
        byte[] content, string sha256, string? label, SaActor actor, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        return MutateAsync(accountId, "Evidence", id, "EvidenceAdded", new { ownerType, ownerId, fileName, Size = content.Length }, label, actor,
            (connection, transaction, now) => connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.Evidence(Id, OwnerEntityType, OwnerEntityId, AccountId, ScopeTeamId, FileName, ContentType, Sha256, SizeBytes, Content, Label, CreatedAt, CreatedBy)
                VALUES(@id, @ownerType, @ownerId, @accountId, @scopeTeamId, @fileName, @contentType, @sha256, @Size, @content, @label, @now, @UserId);
                """, new { id, ownerType, ownerId, accountId, scopeTeamId, fileName, contentType, sha256, Size = content.Length, content, label, now, actor.UserId },
                transaction, cancellationToken)), cancellationToken);
    }

    /// <summary>Reads evidence bytes and anchors (scope checked by the service before returning).</summary>
    public async Task<EvidenceFile?> EvidenceAsync(Guid id, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<EvidenceFile>(Cmd(
            "SELECT Id, AccountId, ScopeTeamId, FileName, ContentType, Content, Sha256 FROM svcacct.Evidence WHERE Id = @id;", new { id }, null, cancellationToken));
    }

    /// <summary>Records an audit event for a privileged read (evidence download, export).</summary>
    public async Task AuditReadAsync(string action, object details, SaActor actor, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken);
        await AuditAsync(connection, transaction, action, details, actor, DateTimeOffset.UtcNow, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
