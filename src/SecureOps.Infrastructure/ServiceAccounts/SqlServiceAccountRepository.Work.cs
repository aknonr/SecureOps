using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>Validated mutation input for account attributes; null = unchanged, Clear* = explicit reasoned clearing.</summary>
public sealed record AccountChange(string ExpectedVersion, string? Notes, Guid? ConsumerTeamId, Guid? ReportOrganizationId, string? Domain,
    bool ClearNotes, bool ClearConsumerTeam, string? Reason, IReadOnlyList<SaExternalRef> References);

/// <summary>Validated request change (null = unchanged; listed fields are cleared).</summary>
public sealed record RequestChange(string ExpectedVersion, ServiceAccountActionType? ActionType, Guid? TargetTeamId, Guid? FollowupPersonId,
    Guid? ContactPersonId, DateOnly? PlanStart, DateOnly? PlanEnd, DateOnly? PlanAnnouncedOn, DateOnly? NextFollowupOn, DateOnly? FirstSentOn,
    DateOnly? LastReplyOn, string? Notes, IReadOnlySet<string> Clear, string? Reason, IReadOnlyList<SaExternalRef> References);

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>Finds the owning account of a module entity (for scope checks on entity routes).</summary>
    public async Task<Guid?> AccountOfAsync(string entityType, Guid id, CancellationToken cancellationToken)
    {
        string table = entityType switch
        {
            "Request" => "svcacct.WorkRequests",
            "Action" => "svcacct.ActionEvents",
            "Finding" => "svcacct.Findings",
            "Handover" => "svcacct.Handovers",
            "Transition" => "svcacct.IdentityTransitions",
            "Ownership" => "svcacct.OwnershipAssignments",
            "Evidence" => "svcacct.Evidence",
            _ => throw new InvalidOperationException("Unsupported entity.")
        };
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        // Table name comes only from the closed switch above.
        return await connection.QuerySingleOrDefaultAsync<Guid?>(Cmd($"SELECT AccountId FROM {table} WHERE Id = @id;", new { id }, null, cancellationToken));
    }

    /// <summary>Creates an account manually (explicit, audited; no automatic provisioning from names).</summary>
    public Task<SaResult<Guid>> CreateAccountAsync(string name, string? domain, Guid? organizationId, string reason, SaActor actor, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        string nameKey = ServiceAccountText.AccountKey(name)!;
        string? domainKey = ServiceAccountText.DomainKey(domain);
        return MutateAsync(id, "Account", id, "Created", new { Name = ServiceAccountText.Clean(name), Domain = ServiceAccountText.Clean(domain), organizationId }, reason, actor,
            async (connection, transaction, now) =>
            {
                string identity = ServiceAccountText.IdentityKey(nameKey, domainKey);
                if (await connection.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM svcacct.Accounts WITH (UPDLOCK, HOLDLOCK) WHERE IdentityKey = @identity;",
                    new { identity }, transaction, cancellationToken)) > 0)
                {
                    return -1;
                }

                return await connection.ExecuteAsync(Cmd("""
                    INSERT INTO svcacct.Accounts(Id, AccountName, NormalizedName, Domain, NormalizedDomain, IdentityKey, IdentityState, ReportOrganizationId,
                        LifecycleState, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                    VALUES(@id, @name, @nameKey, @domain, @domainKey, @identity, @state, @organizationId, 'Active', @now, @UserId, @now, @UserId);
                    """, new { id, name = ServiceAccountText.Clean(name), nameKey, domain = ServiceAccountText.Clean(domain), domainKey, identity,
                    state = domainKey is null ? "Provisional" : "Confirmed", organizationId, now, actor.UserId }, transaction, cancellationToken));
            }, cancellationToken, duplicateField: "accountName");
    }

    /// <summary>Updates account attributes; blanks never erase, clearing is explicit with a reason.</summary>
    public Task<SaResult<Guid>> UpdateAccountAsync(Guid id, AccountChange change, SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(id, "Account", id, "Updated", new { change.Notes, change.ConsumerTeamId, change.ReportOrganizationId, change.Domain, change.ClearNotes,
            change.ClearConsumerTeam, References = change.References.Count }, change.Reason, actor, async (connection, transaction, now) =>
        {
            string? domainKey = ServiceAccountText.DomainKey(change.Domain);
            int updated = await connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.Accounts SET
                    Notes = CASE WHEN @ClearNotes = 1 THEN NULL ELSE COALESCE(@Notes, Notes) END,
                    ConsumerTeamId = CASE WHEN @ClearConsumerTeam = 1 THEN NULL ELSE COALESCE(@ConsumerTeamId, ConsumerTeamId) END,
                    ReportOrganizationId = COALESCE(@ReportOrganizationId, ReportOrganizationId),
                    Domain = CASE WHEN Domain IS NULL AND @Domain IS NOT NULL THEN @Domain ELSE Domain END,
                    NormalizedDomain = CASE WHEN NormalizedDomain IS NULL AND @domainKey IS NOT NULL THEN @domainKey ELSE NormalizedDomain END,
                    IdentityKey = CASE WHEN NormalizedDomain IS NULL AND @domainKey IS NOT NULL THEN 'D:' + @domainKey + '|' + NormalizedName ELSE IdentityKey END,
                    IdentityState = CASE WHEN NormalizedDomain IS NULL AND @domainKey IS NOT NULL THEN 'Confirmed' ELSE IdentityState END,
                    UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @id AND RowVer = @RowVer;
                """, new { change.ClearNotes, change.Notes, change.ClearConsumerTeam, change.ConsumerTeamId, change.ReportOrganizationId,
                Domain = ServiceAccountText.Clean(change.Domain), domainKey, now, actor.UserId, id, RowVer = Version(change.ExpectedVersion) }, transaction, cancellationToken));
            await ReferencesAsync(connection, transaction, change.References, "Account", id, id, actor, now, cancellationToken);
            return updated;
        }, cancellationToken, duplicateField: "domain");

    /// <summary>Proposes or confirms ownership. Confirmation ends the previous confirmed assignment (history kept).</summary>
    public Task<SaResult<Guid>> ChangeOwnershipAsync(Guid accountId, string expectedVersion, Guid? teamId, Guid? personId, bool confirm, string reason,
        DateOnly? effectiveFrom, string? evidence, DateOnly today, SaActor actor, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        return MutateAsync(accountId, "Ownership", id, confirm ? "OwnershipConfirmed" : "OwnershipProposed", new { teamId, personId, effectiveFrom }, reason, actor,
            async (connection, transaction, now) =>
            {
                int touched = await connection.ExecuteAsync(Cmd("""
                    UPDATE svcacct.Accounts SET UpdatedAt = @now, UpdatedBy = @UserId,
                        CurrentOwnerTeamId = CASE WHEN @confirm = 1 THEN @teamId ELSE CurrentOwnerTeamId END,
                        CurrentOwnerPersonId = CASE WHEN @confirm = 1 THEN @personId ELSE CurrentOwnerPersonId END
                    WHERE Id = @accountId AND RowVer = @RowVer;
                    """, new { now, actor.UserId, confirm, teamId, personId, accountId, RowVer = Version(expectedVersion) }, transaction, cancellationToken));
                if (touched != 1)
                {
                    return 0;
                }

                if (confirm)
                {
                    await EndConfirmedAsync(connection, transaction, accountId, today, reason, actor, now, cancellationToken);
                }

                return await connection.ExecuteAsync(Cmd("""
                    INSERT INTO svcacct.OwnershipAssignments(Id, AccountId, TeamId, PersonId, State, Source, EffectiveFrom, EvidenceNote, ProposedBy, ProposedAt,
                        DecidedBy, DecidedAt, DecisionReason)
                    VALUES(@id, @accountId, @teamId, @personId, @State, N'Uygulama içi karar', @effectiveFrom, @evidence, @UserId, @now, @DecidedBy, @DecidedAt, @reason);
                    """, new { id, accountId, teamId, personId, State = confirm ? "Confirmed" : "Proposed", effectiveFrom = effectiveFrom ?? (confirm ? today : null),
                    evidence, actor.UserId, now, DecidedBy = confirm ? actor.UserId : (Guid?)null, DecidedAt = confirm ? now : (DateTimeOffset?)null, reason },
                    transaction, cancellationToken));
            }, cancellationToken);
    }

    /// <summary>Confirms or rejects a proposed ownership at its expected version.</summary>
    public Task<SaResult<Guid>> DecideOwnershipAsync(Guid accountId, Guid assignmentId, string expectedVersion, bool confirm, string reason, DateOnly today,
        SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Ownership", assignmentId, confirm ? "OwnershipConfirmed" : "OwnershipRejected", null, reason, actor, async (connection, transaction, now) =>
        {
            (Guid? Team, Guid? Person)? proposal = await connection.QuerySingleOrDefaultAsync<(Guid? Team, Guid? Person)?>(Cmd("""
                SELECT TeamId, PersonId FROM svcacct.OwnershipAssignments WITH (UPDLOCK) WHERE Id = @assignmentId AND AccountId = @accountId
                  AND State = 'Proposed' AND RowVer = @RowVer;
                """, new { assignmentId, accountId, RowVer = Version(expectedVersion) }, transaction, cancellationToken));
            if (proposal is not { } p)
            {
                return 0;
            }

            if (confirm)
            {
                await EndConfirmedAsync(connection, transaction, accountId, today, reason, actor, now, cancellationToken);
                await connection.ExecuteAsync(Cmd("""
                    UPDATE svcacct.Accounts SET CurrentOwnerTeamId = @Team, CurrentOwnerPersonId = @Person, UpdatedAt = @now, UpdatedBy = @UserId WHERE Id = @accountId;
                    """, new { p.Team, p.Person, now, actor.UserId, accountId }, transaction, cancellationToken));
            }

            return await connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.OwnershipAssignments SET State = @State, DecidedBy = @UserId, DecidedAt = @now, DecisionReason = @reason,
                    EffectiveFrom = CASE WHEN @confirm = 1 THEN COALESCE(EffectiveFrom, @today) ELSE EffectiveFrom END
                WHERE Id = @assignmentId;
                """, new { State = confirm ? "Confirmed" : "Rejected", actor.UserId, now, reason, confirm, today, assignmentId }, transaction, cancellationToken));
        }, cancellationToken);

    private static Task EndConfirmedAsync(SqlConnection connection, SqlTransaction transaction, Guid accountId, DateOnly today, string reason, SaActor actor,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(Cmd("""
            UPDATE svcacct.OwnershipAssignments SET State = 'Ended', EffectiveTo = @today, DecisionReason = CONCAT(DecisionReason, N' | Sonlandırıldı: ', @reason),
                DecidedBy = COALESCE(DecidedBy, @UserId), DecidedAt = COALESCE(DecidedAt, @now)
            WHERE AccountId = @accountId AND State = 'Confirmed';
            """, new { today, reason, actor.UserId, now, accountId }, transaction, cancellationToken));

    /// <summary>Creates a request (expected work).</summary>
    public Task<SaResult<Guid>> CreateRequestAsync(Guid accountId, CreateWorkRequest request, ServiceAccountActionType type, SaActor actor, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        return MutateAsync(accountId, "Request", id, "RequestCreated", request with { References = null }, null, actor, async (connection, transaction, now) =>
        {
            int inserted = await connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.WorkRequests(Id, AccountId, ActionType, Status, TargetTeamId, FollowupPersonId, ContactPersonId, PlanStart, PlanEnd,
                    PlanAnnouncedOn, NextFollowupOn, FirstSentOn, LastReplyOn, Notes, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                SELECT @id, @accountId, @Type, 'Open', @TargetTeamId, @FollowupPersonId, @ContactPersonId, @PlanStart, @PlanEnd, @PlanAnnouncedOn,
                    @NextFollowupOn, @FirstSentOn, @LastReplyOn, @Notes, @now, @UserId, @now, @UserId
                WHERE EXISTS (SELECT 1 FROM svcacct.Accounts WHERE Id = @accountId);
                """, new { id, accountId, Type = type.ToString(), request.TargetTeamId, request.FollowupPersonId, request.ContactPersonId, request.PlanStart,
                request.PlanEnd, request.PlanAnnouncedOn, request.NextFollowupOn, request.FirstSentOn, request.LastReplyOn, Notes = ServiceAccountText.Clean(request.Notes),
                now, actor.UserId }, transaction, cancellationToken));
            await ReferencesAsync(connection, transaction, request.References ?? [], "Request", id, accountId, actor, now, cancellationToken);
            return inserted;
        }, cancellationToken);
    }

    /// <summary>Partial request update at the expected version.</summary>
    public Task<SaResult<Guid>> UpdateRequestAsync(Guid accountId, Guid requestId, RequestChange change, SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Request", requestId, "RequestUpdated", new { change.ActionType, change.TargetTeamId, change.PlanStart, change.PlanEnd,
            change.NextFollowupOn, change.LastReplyOn, Cleared = change.Clear }, change.Reason, actor, async (connection, transaction, now) =>
        {
            int updated = await connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.WorkRequests SET
                    ActionType = COALESCE(@ActionType, ActionType),
                    TargetTeamId = CASE WHEN @cTarget = 1 THEN NULL ELSE COALESCE(@TargetTeamId, TargetTeamId) END,
                    FollowupPersonId = CASE WHEN @cFollowup = 1 THEN NULL ELSE COALESCE(@FollowupPersonId, FollowupPersonId) END,
                    ContactPersonId = CASE WHEN @cContact = 1 THEN NULL ELSE COALESCE(@ContactPersonId, ContactPersonId) END,
                    PlanStart = CASE WHEN @cPlan = 1 THEN NULL ELSE COALESCE(@PlanStart, PlanStart) END,
                    PlanEnd = CASE WHEN @cPlan = 1 THEN NULL ELSE COALESCE(@PlanEnd, PlanEnd) END,
                    PlanAnnouncedOn = CASE WHEN @cAnnounced = 1 THEN NULL ELSE COALESCE(@PlanAnnouncedOn, PlanAnnouncedOn) END,
                    NextFollowupOn = CASE WHEN @cNext = 1 THEN NULL ELSE COALESCE(@NextFollowupOn, NextFollowupOn) END,
                    FirstSentOn = COALESCE(@FirstSentOn, FirstSentOn),
                    LastReplyOn = COALESCE(@LastReplyOn, LastReplyOn),
                    Notes = CASE WHEN @cNotes = 1 THEN NULL ELSE COALESCE(@Notes, Notes) END,
                    UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @requestId AND AccountId = @accountId AND Status = 'Open' AND RowVer = @RowVer
                  AND (COALESCE(@PlanEnd, PlanEnd) IS NULL OR COALESCE(@PlanStart, PlanStart) IS NULL OR @cPlan = 1 OR COALESCE(@PlanEnd, PlanEnd) >= COALESCE(@PlanStart, PlanStart));
                """, new
            {
                ActionType = change.ActionType?.ToString(), change.TargetTeamId, change.FollowupPersonId, change.ContactPersonId, change.PlanStart, change.PlanEnd,
                change.PlanAnnouncedOn, change.NextFollowupOn, change.FirstSentOn, change.LastReplyOn, Notes = ServiceAccountText.Clean(change.Notes),
                cTarget = change.Clear.Contains("targetTeam"), cFollowup = change.Clear.Contains("followupPerson"), cContact = change.Clear.Contains("contactPerson"),
                cPlan = change.Clear.Contains("plan"), cAnnounced = change.Clear.Contains("planAnnouncedOn"), cNext = change.Clear.Contains("nextFollowupOn"),
                cNotes = change.Clear.Contains("notes"), now, actor.UserId, requestId, accountId, RowVer = Version(change.ExpectedVersion)
            }, transaction, cancellationToken));
            await ReferencesAsync(connection, transaction, change.References, "Request", requestId, accountId, actor, now, cancellationToken);
            return updated;
        }, cancellationToken);

    /// <summary>Linked-action facts for the explicit close rule.</summary>
    public async Task<(ServiceAccountRequestStatus Status, ServiceAccountActionType Type, IReadOnlyList<ActionFacts> Actions, bool OwnershipConfirmed)?> RequestCloseFactsAsync(
        Guid requestId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT r.Status, r.ActionType, CAST(CASE WHEN a.CurrentOwnerTeamId IS NOT NULL OR a.CurrentOwnerPersonId IS NOT NULL THEN 1 ELSE 0 END AS bit)
            FROM svcacct.WorkRequests r JOIN svcacct.Accounts a ON a.Id = r.AccountId WHERE r.Id = @requestId;
            SELECT e.ActionType, e.Result, e.RecordKind, e.ActualOn, e.VerifiedOn,
                CAST(CASE WHEN e.VerifiedByUserId IS NOT NULL OR e.VerifiedByPersonId IS NOT NULL THEN 1 ELSE 0 END AS bit),
                CAST(CASE WHEN NULLIF(e.EvidenceNote, '') IS NOT NULL OR NULLIF(e.VerificationNote, '') IS NOT NULL OR e.VerificationEvidenceId IS NOT NULL THEN 1 ELSE 0 END AS bit),
                CAST(CASE WHEN EXISTS (SELECT 1 FROM svcacct.ExternalRecordLinks l JOIN svcacct.ExternalRecords x ON x.Id = l.ExternalRecordId
                    WHERE l.EntityType = 'Action' AND l.EntityId = e.Id AND x.RecordType = 'OR') THEN 1 ELSE 0 END AS bit),
                CAST(CASE WHEN e.VoidedAt IS NOT NULL THEN 1 ELSE 0 END AS bit)
            FROM svcacct.ActionEvents e WHERE e.RequestId = @requestId;
            """, new { requestId }, null, cancellationToken));
        (string Status, string Type, bool Owned)? request = await grid.ReadSingleOrDefaultAsync<(string Status, string Type, bool Owned)?>();
        if (request is not { } r)
        {
            return null;
        }

        ActionFacts[] actions = [.. (await grid.ReadAsync<(string Type, string Result, string Kind, DateOnly? Actual, DateOnly? Verified, bool HasVerifier, bool HasEvidence, bool HasOr, bool Voided)>())
            .Select(a => new ActionFacts(Enum.Parse<ServiceAccountActionType>(a.Type), Enum.Parse<ServiceAccountActionResult>(a.Result),
                Enum.Parse<ServiceAccountRecordKind>(a.Kind), a.Actual, a.Verified, a.HasVerifier, a.HasEvidence, a.HasOr, a.Voided))];
        return (Enum.Parse<ServiceAccountRequestStatus>(r.Status), Enum.Parse<ServiceAccountActionType>(r.Type), actions, r.Owned);
    }

    /// <summary>Closes exactly one request; other requests of the account are untouched.</summary>
    public Task<SaResult<Guid>> CloseRequestAsync(Guid accountId, Guid requestId, string expectedVersion, ServiceAccountCloseOutcome outcome, string? reason,
        SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Request", requestId, "RequestClosed", new { Outcome = outcome.ToString() }, reason, actor, (connection, transaction, now) =>
            connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.WorkRequests SET Status = 'Closed', CloseOutcome = @Outcome, CloseReason = @reason, ClosedAt = @now, ClosedBy = @UserId,
                    UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @requestId AND AccountId = @accountId AND Status = 'Open' AND RowVer = @RowVer;
                """, new { Outcome = outcome.ToString(), reason, now, actor.UserId, requestId, accountId, RowVer = Version(expectedVersion) }, transaction, cancellationToken)),
            cancellationToken);

    /// <summary>Records a planned or performed action; never closes a request.</summary>
    public Task<SaResult<Guid>> ReportActionAsync(Guid accountId, ReportActionRequest request, ServiceAccountActionType type, ServiceAccountActionResult result,
        ServiceAccountRecordKind kind, SaActor actor, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        return MutateAsync(accountId, "Action", id, "ActionReported", new { Type = type.ToString(), Result = result.ToString(), Kind = kind.ToString(), request.ActualOn },
            null, actor, async (connection, transaction, now) =>
            {
                string precision = request.ActualAt is not null ? "Instant" : request.ActualOn is not null ? "DateOnly" : "Unknown";
                DateOnly? actualOn = request.ActualAt is { } at ? ReportCalendar.LocalDate(at) : request.ActualOn;
                int inserted = await connection.ExecuteAsync(Cmd("""
                    INSERT INTO svcacct.ActionEvents(Id, AccountId, RequestId, ActionType, Result, RecordKind, ActualOn, ActualAt, ActualPrecision, PerformerTeamId,
                        PerformerPersonId, EvidenceNote, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                    SELECT @id, @accountId, @RequestId, @Type, @Result, @Kind, @actualOn, @ActualAt, @precision, @PerformerTeamId, @PerformerPersonId, @EvidenceNote,
                        @now, @UserId, @now, @UserId
                    WHERE @RequestId IS NULL OR EXISTS (SELECT 1 FROM svcacct.WorkRequests WHERE Id = @RequestId AND AccountId = @accountId);
                    """, new { id, accountId, request.RequestId, Type = type.ToString(), Result = result.ToString(), Kind = kind.ToString(), actualOn, request.ActualAt,
                    precision, request.PerformerTeamId, request.PerformerPersonId, EvidenceNote = ServiceAccountText.Clean(request.EvidenceNote), now, actor.UserId },
                    transaction, cancellationToken));
                await ReferencesAsync(connection, transaction, request.References ?? [], "Action", id, accountId, actor, now, cancellationToken);
                return inserted;
            }, cancellationToken);
    }

    /// <summary>Moves a planned action to performed (same identity) or completes missing fields.</summary>
    public Task<SaResult<Guid>> UpdateActionAsync(Guid accountId, Guid actionId, UpdateActionRequest request, ServiceAccountActionResult? result,
        ServiceAccountRecordKind? kind, SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Action", actionId, "ActionUpdated", new { Result = result?.ToString(), request.ActualOn, Kind = kind?.ToString() }, null, actor,
            async (connection, transaction, now) =>
            {
                DateOnly? actualOn = request.ActualAt is { } at ? ReportCalendar.LocalDate(at) : request.ActualOn;
                int updated = await connection.ExecuteAsync(Cmd("""
                    UPDATE svcacct.ActionEvents SET
                        Result = CASE WHEN @Result = 'Performed' AND Result = 'Planned' THEN 'Performed' ELSE Result END,
                        RecordKind = COALESCE(@Kind, RecordKind),
                        ActualOn = COALESCE(@actualOn, ActualOn),
                        ActualAt = COALESCE(@ActualAt, ActualAt),
                        ActualPrecision = CASE WHEN @ActualAt IS NOT NULL THEN 'Instant' WHEN @actualOn IS NOT NULL AND ActualAt IS NULL THEN 'DateOnly' ELSE ActualPrecision END,
                        PerformerTeamId = COALESCE(@PerformerTeamId, PerformerTeamId),
                        PerformerPersonId = COALESCE(@PerformerPersonId, PerformerPersonId),
                        EvidenceNote = COALESCE(@EvidenceNote, EvidenceNote),
                        UpdatedAt = @now, UpdatedBy = @UserId
                    WHERE Id = @actionId AND AccountId = @accountId AND VoidedAt IS NULL AND Result <> 'Verified' AND RowVer = @RowVer;
                    """, new { Result = result?.ToString(), Kind = kind?.ToString(), actualOn, request.ActualAt, request.PerformerTeamId, request.PerformerPersonId,
                    EvidenceNote = ServiceAccountText.Clean(request.EvidenceNote), now, actor.UserId, actionId, accountId, RowVer = Version(request.ExpectedVersion) },
                    transaction, cancellationToken));
                await ReferencesAsync(connection, transaction, request.AddReferences ?? [], "Action", actionId, accountId, actor, now, cancellationToken);
                return updated;
            }, cancellationToken);

    /// <summary>Verifies the same action identity; a qualifying closure marks the account verified-closed.</summary>
    public Task<SaResult<Guid>> VerifyActionAsync(Guid accountId, Guid actionId, VerifyActionRequest request, bool closure, SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Action", actionId, "ActionVerified", new { request.VerifiedOn, request.VerifierPersonId, Closure = closure }, request.VerificationNote, actor,
            async (connection, transaction, now) =>
            {
                int updated = await connection.ExecuteAsync(Cmd("""
                    UPDATE svcacct.ActionEvents SET Result = 'Verified', VerifiedOn = @VerifiedOn, VerifiedByUserId = @UserId, VerifiedByPersonId = @VerifierPersonId,
                        VerificationNote = @VerificationNote, VerificationEvidenceId = @EvidenceId, UpdatedAt = @now, UpdatedBy = @UserId
                    WHERE Id = @actionId AND AccountId = @accountId AND Result = 'Performed' AND VoidedAt IS NULL AND RowVer = @RowVer;
                    """, new { request.VerifiedOn, actor.UserId, request.VerifierPersonId, VerificationNote = ServiceAccountText.Clean(request.VerificationNote),
                    request.EvidenceId, now, actionId, accountId, RowVer = Version(request.ExpectedVersion) }, transaction, cancellationToken));
                if (updated == 1 && closure)
                {
                    await connection.ExecuteAsync(Cmd("""
                        UPDATE svcacct.Accounts SET LifecycleState = 'ClosureVerified', UpdatedAt = @now, UpdatedBy = @UserId WHERE Id = @accountId;
                        """, new { now, actor.UserId, accountId }, transaction, cancellationToken));
                    await HistoryAsync(connection, transaction, "Account", accountId, accountId, "ClosureVerified", new { ActionId = actionId }, null, actor, now, cancellationToken);
                }

                return updated;
            }, cancellationToken);

    /// <summary>Voids an action with a reason (never deleted; excluded from reports by rule).</summary>
    public Task<SaResult<Guid>> VoidActionAsync(Guid accountId, Guid actionId, VoidActionRequest request, SaActor actor, CancellationToken cancellationToken) =>
        MutateAsync(accountId, "Action", actionId, "ActionVoided", null, request.Reason, actor, (connection, transaction, now) =>
            connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.ActionEvents SET VoidedAt = @now, VoidedBy = @UserId, VoidReason = @Reason, UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @actionId AND AccountId = @accountId AND VoidedAt IS NULL AND RowVer = @RowVer;
                """, new { now, actor.UserId, request.Reason, actionId, accountId, RowVer = Version(request.ExpectedVersion) }, transaction, cancellationToken)),
            cancellationToken);

    /// <summary>Generic mutation: caller-supplied write + business history + platform audit in one transaction.</summary>
    private async Task<SaResult<Guid>> MutateAsync(Guid? accountId, string entityType, Guid entityId, string action, object? changes, string? reason, SaActor actor,
        Func<SqlConnection, SqlTransaction, DateTimeOffset, Task<int>> write, CancellationToken cancellationToken, string? duplicateField = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginAsync(connection, cancellationToken, IsolationLevel.ReadCommitted);
        int affected;
        try
        {
            affected = await write(connection, transaction, now);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627 && duplicateField is not null)
        {
            return SaResult<Guid>.Fail(SaErrors.Invalid, duplicateField);
        }

        if (affected == -1)
        {
            return SaResult<Guid>.Fail(SaErrors.Invalid, duplicateField ?? "duplicate");
        }

        if (affected < 1)
        {
            return SaResult<Guid>.Fail(SaErrors.Conflict, "expectedVersion");
        }

        await HistoryAsync(connection, transaction, entityType, entityId, accountId, action, changes, reason, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, action, new { EntityType = entityType, EntityId = entityId, AccountId = accountId }, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entityId;
    }

    private static async Task ReferencesAsync(SqlConnection connection, SqlTransaction transaction, IEnumerable<SaExternalRef> references, string entityType, Guid entityId,
        Guid? accountId, SaActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (SaExternalRef reference in references)
        {
            await connection.ExecuteAsync(Cmd("""
                DECLARE @recordId uniqueidentifier = (SELECT Id FROM svcacct.ExternalRecords WITH (UPDLOCK, HOLDLOCK) WHERE RecordType = @Type AND NormalizedNumber = @Number);
                IF @recordId IS NULL
                BEGIN
                    SET @recordId = NEWID();
                    INSERT INTO svcacct.ExternalRecords(Id, RecordType, Number, NormalizedNumber, Url, CreatedAt, CreatedBy)
                    VALUES(@recordId, @Type, @Display, @Number, @Url, @now, @UserId);
                END;
                IF NOT EXISTS (SELECT 1 FROM svcacct.ExternalRecordLinks WHERE ExternalRecordId = @recordId AND EntityType = @entityType AND EntityId = @entityId)
                    INSERT INTO svcacct.ExternalRecordLinks(ExternalRecordId, EntityType, EntityId, AccountId, LinkedAt, LinkedBy)
                    VALUES(@recordId, @entityType, @entityId, @accountId, @now, @UserId);
                """, new { reference.Type, Number = ServiceAccountText.RecordNumber(reference.Number), Display = ServiceAccountText.Clean(reference.Number), reference.Url,
                entityType, entityId, accountId, now, actor.UserId }, transaction, cancellationToken));
        }
    }
}
