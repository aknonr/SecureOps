using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>A validated communication of an event (direction and kind already derived; one linked account).</summary>
public sealed record EventCommunicationWrite(CommunicationDirection Direction, CommunicationKind Kind, CreateCommunicationRequest Request);

/// <summary>A validated action report of an event (type and record kind derived; result is always Performed).</summary>
public sealed record EventActionWrite(ServiceAccountActionType Type, ServiceAccountRecordKind RecordKind, ReportActionRequest Request);

/// <summary>A request the event opens (type derived from the event).</summary>
public sealed record EventRequestWrite(ServiceAccountActionType Type, Guid? TargetTeamId, string? Notes);

/// <summary>An ownership proposal of the event (never a confirmation).</summary>
public sealed record EventOwnershipWrite(Guid? TeamId, Guid? PersonId, DateOnly? EffectiveFrom, string Reason);

/// <summary>
/// Everything one event commit writes, already validated and derived by the service (EVENT-GUIDANCE-DESIGN.md §5.4). The
/// repository writes it in one transaction after re-checking the idempotency key and the versions the preview was built on.
/// </summary>
public sealed record AccountEventWrite(
    Guid AccountId,
    AccountEventKind Kind,
    DateOnly? OccurredOn,
    AccountEventChannel Channel,
    Guid IdempotencyKey,
    string DraftDigest,
    string ExpectedAccountVersion,
    Guid? RequestId,
    string? ExpectedRequestVersion,
    EventCommunicationWrite? Communication,
    EventActionWrite? Action,
    IReadOnlyList<SaExternalRef> References,
    bool MoveLastReply,
    EventRequestWrite? CreateRequest,
    EventOwnershipWrite? Ownership,
    bool CloseRequest);

/// <summary>How an event commit ended.</summary>
public enum AccountEventStatus
{
    /// <summary>Everything was written.</summary>
    Committed,
    /// <summary>The same key and draft were already recorded; nothing new was written.</summary>
    Replayed,
    /// <summary>The key was used with a different draft; nothing was written.</summary>
    KeyReused,
    /// <summary>The account or the linked request changed (or the request is no longer open); nothing was written.</summary>
    Stale,
    /// <summary>The request cannot be closed after these records; nothing was written.</summary>
    CloseRefused,
    /// <summary>The mail is already recorded outside the caller's scope; nothing was written.</summary>
    CommunicationRefused
}

/// <summary>Result of an event commit; <see cref="Gaps"/> lists the missing close conditions of <see cref="AccountEventStatus.CloseRefused"/>.</summary>
public sealed record AccountEventOutcome(AccountEventStatus Status, Guid? EventId, IReadOnlyList<EventCreatedRecord> Created, IReadOnlyList<CloseGap> Gaps,
    ServiceAccountRequestStatus? RequestStatus = null);

/// <summary>A recorded event found by its idempotency key.</summary>
public sealed record StoredAccountEvent(Guid Id, string DraftDigest, IReadOnlyList<EventCreatedRecord> Items);

public sealed partial class SqlServiceAccountRepository
{
    /// <summary>Set once migration 036 was seen; tables are never dropped again, so only a positive answer is cached.</summary>
    private volatile bool _accountEvents;

    /// <summary>Whether migration 036 (account events) is installed. Binaries with this code keep working before it.</summary>
    public async Task<bool> AccountEventsInstalledAsync(CancellationToken cancellationToken)
    {
        if (_accountEvents)
        {
            return true;
        }

        await using SqlConnection connection = await OpenAsync(cancellationToken);
        bool present = await connection.ExecuteScalarAsync<int>(Cmd("""
            SELECT CASE WHEN OBJECT_ID(N'svcacct.AccountEvents', N'U') IS NULL OR OBJECT_ID(N'svcacct.AccountEventItems', N'U') IS NULL THEN 0 ELSE 1 END;
            """, null, null, cancellationToken)) == 1;
        _accountEvents = present;
        return present;
    }

    /// <summary>The event recorded on this account under <paramref name="key"/>, with the records it produced; null when none.</summary>
    public async Task<StoredAccountEvent?> AccountEventByKeyAsync(Guid accountId, Guid key, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await StoredEventAsync(connection, null, accountId, key, false, cancellationToken);
    }

    /// <summary>The communication already recorded under this provider message id, if any (a repeated mail is linked, not copied).</summary>
    public async Task<Guid?> CommunicationByProviderIdAsync(string providerMessageId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Guid?>(Cmd("SELECT Id FROM svcacct.Communications WHERE ProviderMessageId = @providerMessageId;",
            new { providerMessageId }, null, cancellationToken));
    }

    /// <summary>Whether every given team and person id exists in the module directory (an event never accepts a free name).</summary>
    public async Task<bool> DirectoryIdsKnownAsync(IReadOnlyCollection<Guid> teams, IReadOnlyCollection<Guid> people, CancellationToken cancellationToken)
    {
        if (teams.Count == 0 && people.Count == 0)
        {
            return true;
        }

        await using SqlConnection connection = await OpenAsync(cancellationToken);
        (int Teams, int People) found = await connection.QuerySingleAsync<(int, int)>(Cmd("""
            SELECT (SELECT COUNT(*) FROM svcacct.Teams WHERE Id IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@teams))),
                (SELECT COUNT(*) FROM svcacct.People WHERE Id IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@people)));
            """, new
        {
            teams = System.Text.Json.JsonSerializer.Serialize(teams.Distinct().Select(t => t.ToString("D"))),
            people = System.Text.Json.JsonSerializer.Serialize(people.Distinct().Select(p => p.ToString("D")))
        }, null, cancellationToken));
        return found.Teams == teams.Distinct().Count() && found.People == people.Distinct().Count();
    }

    /// <summary>
    /// Writes one event in one transaction: communication → action → references → request update → new request → ownership
    /// proposal → (optional) close → event header and items, each with its history and audit row. The account row is locked
    /// first so a concurrent commit of the same key waits and then replays. Any refusal or failure leaves nothing behind.
    /// </summary>
    public async Task<AccountEventOutcome> CommitAccountEventAsync(AccountEventWrite write, SaActor actor, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        await using SqlTransaction transaction = await BeginWriteAsync(connection, cancellationToken, IsolationLevel.ReadCommitted);
        Guid accountId = write.AccountId;

        byte[]? accountVersion = await connection.QuerySingleOrDefaultAsync<byte[]?>(Cmd(
            "SELECT RowVer FROM svcacct.Accounts WITH (UPDLOCK, ROWLOCK) WHERE Id = @accountId;", new { accountId }, transaction, cancellationToken));
        if (accountVersion is null)
        {
            return new AccountEventOutcome(AccountEventStatus.Stale, null, [], []);
        }

        if (await StoredEventAsync(connection, transaction, accountId, write.IdempotencyKey, true, cancellationToken) is { } stored)
        {
            return stored.DraftDigest == write.DraftDigest
                ? new AccountEventOutcome(AccountEventStatus.Replayed, stored.Id, stored.Items, [])
                : new AccountEventOutcome(AccountEventStatus.KeyReused, null, [], []);
        }

        if (!accountVersion.AsSpan().SequenceEqual(Version(write.ExpectedAccountVersion)))
        {
            return new AccountEventOutcome(AccountEventStatus.Stale, null, [], []);
        }

        if (write.RequestId is { } linked)
        {
            (byte[] RowVer, string Status)? request = await connection.QuerySingleOrDefaultAsync<(byte[] RowVer, string Status)?>(Cmd(
                "SELECT RowVer, Status FROM svcacct.WorkRequests WITH (UPDLOCK, ROWLOCK) WHERE Id = @linked AND AccountId = @accountId;",
                new { linked, accountId }, transaction, cancellationToken));
            if (request is not { Status: "Open" } open || !open.RowVer.AsSpan().SequenceEqual(Version(write.ExpectedRequestVersion)))
            {
                return new AccountEventOutcome(AccountEventStatus.Stale, null, [], []);
            }
        }

        // The event changes the account's records, so the account version moves: other open previews of this account go stale.
        await connection.ExecuteAsync(Cmd("UPDATE svcacct.Accounts SET UpdatedAt = @now, UpdatedBy = @UserId WHERE Id = @accountId;",
            new { now, actor.UserId, accountId }, transaction, cancellationToken));

        var eventId = Guid.NewGuid();
        List<EventCreatedRecord> items = [];
        Guid? communicationId = null;
        if (write.Communication is { } communication)
        {
            string? provider = ServiceAccountText.Clean(communication.Request.ProviderMessageId);
            Guid? existing = provider is null ? null : await connection.QuerySingleOrDefaultAsync<Guid?>(Cmd(
                "SELECT Id FROM svcacct.Communications WITH (UPDLOCK, HOLDLOCK) WHERE ProviderMessageId = @provider;", new { provider }, transaction, cancellationToken));
            if (existing is { } known && !await CommunicationVisibleAsync(connection, transaction,
                await LockedScopeAsync(connection, transaction, actor.UserId, cancellationToken), known, cancellationToken))
            {
                return new AccountEventOutcome(AccountEventStatus.CommunicationRefused, null, [], []);
            }

            communicationId = existing ?? Guid.NewGuid();
            if (existing is null)
            {
                await InsertCommunicationAsync(connection, transaction, communicationId.Value, provider, communication.Request, communication.Direction, communication.Kind,
                    actor, now, cancellationToken);
            }

            await LinkAsync(connection, transaction, communicationId.Value, [accountId], actor, now, cancellationToken);
            string action = existing is null ? "CommunicationRecorded" : "CommunicationLinked";
            await RecordAsync(connection, transaction, "Communication", communicationId.Value, accountId, action,
                new { Direction = communication.Direction.ToString(), Kind = communication.Kind.ToString(), communication.Request.OccurredOn, EventId = eventId }, null,
                actor, now, cancellationToken);
            items.Add(new EventCreatedRecord("Communication", "Communication", communicationId.Value, existing is null ? "Create" : "Link"));
        }

        Guid? actionId = null;
        if (write.Action is { } report)
        {
            actionId = Guid.NewGuid();
            if (await InsertActionAsync(connection, transaction, actionId.Value, accountId, report.Request, report.Type, ServiceAccountEventRules.ActionResult,
                report.RecordKind, actor, now, cancellationToken) != 1)
            {
                return new AccountEventOutcome(AccountEventStatus.Stale, null, [], []);
            }

            await RecordAsync(connection, transaction, "Action", actionId.Value, accountId, "ActionReported", new
            {
                Type = report.Type.ToString(),
                Result = ServiceAccountEventRules.ActionResult.ToString(),
                Kind = report.RecordKind.ToString(),
                report.Request.ActualOn,
                EventId = eventId
            }, null, actor, now, cancellationToken);
            items.Add(new EventCreatedRecord("Action", "Action", actionId.Value, "Create"));
        }

        if (write.References.Count > 0)
        {
            // OR/OCO numbers belong to the reported action and the linked request; without either, to the mail; else to the account.
            List<(string Type, Guid Id)> targets = [];
            if (actionId is { } a)
            {
                targets.Add(("Action", a));
            }

            if (write.RequestId is { } r)
            {
                targets.Add(("Request", r));
            }

            if (targets.Count == 0)
            {
                targets.Add(communicationId is { } c ? ("Communication", c) : ("Account", accountId));
            }

            foreach ((string type, Guid id) in targets)
            {
                foreach (Guid record in await ReferencesAsync(connection, transaction, write.References, type, id, accountId, actor, now, cancellationToken))
                {
                    if (!items.Any(i => i.Kind == "Reference" && i.Id == record))
                    {
                        items.Add(new EventCreatedRecord("Reference", "ExternalRecord", record, "Link"));
                    }
                }
            }
        }

        if (write.MoveLastReply && write.RequestId is { } replied && write.OccurredOn is { } repliedOn
            && await connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.WorkRequests SET LastReplyOn = @repliedOn, UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @replied AND AccountId = @accountId AND Status = 'Open' AND (LastReplyOn IS NULL OR LastReplyOn < @repliedOn);
                """, new { repliedOn, now, actor.UserId, replied, accountId }, transaction, cancellationToken)) == 1)
        {
            await RecordAsync(connection, transaction, "Request", replied, accountId, "RequestUpdated", new { LastReplyOn = repliedOn, EventId = eventId }, null,
                actor, now, cancellationToken);
            items.Add(new EventCreatedRecord("RequestUpdate", "Request", replied, "Update"));
        }

        if (write.CreateRequest is { } create)
        {
            var requestId = Guid.NewGuid();
            await connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.WorkRequests(Id, AccountId, ActionType, Status, TargetTeamId, Notes, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES(@requestId, @accountId, @Type, 'Open', @TargetTeamId, @Notes, @now, @UserId, @now, @UserId);
                """, new { requestId, accountId, Type = create.Type.ToString(), create.TargetTeamId, Notes = ServiceAccountText.Clean(create.Notes), now, actor.UserId },
                transaction, cancellationToken));
            await RecordAsync(connection, transaction, "Request", requestId, accountId, "RequestCreated",
                new { ActionType = create.Type.ToString(), create.TargetTeamId, EventId = eventId }, null, actor, now, cancellationToken);
            items.Add(new EventCreatedRecord("RequestCreate", "Request", requestId, "Create"));
        }

        if (write.Ownership is { } ownership)
        {
            var assignmentId = Guid.NewGuid();
            await connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.OwnershipAssignments(Id, AccountId, TeamId, PersonId, State, Source, EffectiveFrom, EvidenceNote, ProposedBy, ProposedAt,
                    DecidedBy, DecidedAt, DecisionReason)
                VALUES(@assignmentId, @accountId, @TeamId, @PersonId, 'Proposed', N'Olay bildirimi', @EffectiveFrom, NULL, @UserId, @now, NULL, NULL, @Reason);
                """, new { assignmentId, accountId, ownership.TeamId, ownership.PersonId, ownership.EffectiveFrom, actor.UserId, now, ownership.Reason },
                transaction, cancellationToken));
            await RecordAsync(connection, transaction, "Ownership", assignmentId, accountId, "OwnershipProposed",
                new { ownership.TeamId, ownership.PersonId, ownership.EffectiveFrom, EventId = eventId }, ownership.Reason, actor, now, cancellationToken);
            items.Add(new EventCreatedRecord("OwnershipProposal", "Ownership", assignmentId, "Create"));
        }

        if (write.CloseRequest && write.RequestId is { } closing)
        {
            // The close rule runs on the records as now written in this transaction (single source: EvaluateClose).
            if (await RequestCloseFactsAsync(connection, transaction, closing, cancellationToken) is not { } facts)
            {
                return new AccountEventOutcome(AccountEventStatus.Stale, null, [], []);
            }

            IReadOnlyList<CloseGap> gaps = ServiceAccountRules.EvaluateClose(facts.Status, facts.Type, ServiceAccountCloseOutcome.Completed, null, facts.Actions,
                facts.OwnershipConfirmed);
            if (gaps.Count > 0)
            {
                return new AccountEventOutcome(AccountEventStatus.CloseRefused, null, [], gaps, facts.Status);
            }

            await connection.ExecuteAsync(Cmd("""
                UPDATE svcacct.WorkRequests SET Status = 'Closed', CloseOutcome = 'Completed', CloseReason = NULL, ClosedAt = @now, ClosedBy = @UserId,
                    UpdatedAt = @now, UpdatedBy = @UserId
                WHERE Id = @closing AND AccountId = @accountId AND Status = 'Open';
                """, new { now, actor.UserId, closing, accountId }, transaction, cancellationToken));
            await RecordAsync(connection, transaction, "Request", closing, accountId, "RequestClosed", new { Outcome = "Completed", EventId = eventId }, null,
                actor, now, cancellationToken);
            items.Add(new EventCreatedRecord("RequestClose", "Request", closing, "Update"));
        }

        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.AccountEvents(Id, AccountId, EventKind, OccurredOn, DatePrecision, Channel, IdempotencyKey, DraftDigest, CreatedBy, CreatedAt)
            VALUES(@eventId, @accountId, @Kind, @OccurredOn, @Precision, @Channel, @IdempotencyKey, @DraftDigest, @UserId, @now);
            """, new
        {
            eventId,
            accountId,
            Kind = write.Kind.ToString(),
            write.OccurredOn,
            Precision = write.OccurredOn is null ? "Unknown" : "DateOnly",
            Channel = write.Channel.ToString(),
            write.IdempotencyKey,
            write.DraftDigest,
            actor.UserId,
            now
        }, transaction, cancellationToken));
        foreach (EventCreatedRecord item in items)
        {
            await connection.ExecuteAsync(Cmd("""
                INSERT INTO svcacct.AccountEventItems(EventId, ItemKind, EntityType, EntityId, Operation) VALUES(@eventId, @Kind, @EntityType, @Id, @Operation);
                """, new { eventId, item.Kind, item.EntityType, item.Id, item.Operation }, transaction, cancellationToken));
        }

        await RecordAsync(connection, transaction, "Event", eventId, accountId, "EventRecorded",
            new { EventKind = write.Kind.ToString(), write.OccurredOn, Channel = write.Channel.ToString(), Items = items.Count }, null, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AccountEventOutcome(AccountEventStatus.Committed, eventId, items, []);
    }

    /// <summary>Business history and platform audit (safe identifiers only) for one record of the event.</summary>
    private static async Task RecordAsync(SqlConnection connection, SqlTransaction transaction, string entityType, Guid entityId, Guid accountId, string action,
        object changes, string? reason, SaActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await HistoryAsync(connection, transaction, entityType, entityId, accountId, action, changes, reason, actor, now, cancellationToken);
        await AuditAsync(connection, transaction, action, new { EntityType = entityType, EntityId = entityId, AccountId = accountId }, actor, now, cancellationToken);
    }

    private static async Task<StoredAccountEvent?> StoredEventAsync(SqlConnection connection, SqlTransaction? transaction, Guid accountId, Guid key, bool lockKey,
        CancellationToken cancellationToken)
    {
        // Only the closed lock hint below is interpolated; every value is a parameter.
        (Guid Id, string Digest)? stored = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Digest)?>(Cmd($"""
            SELECT Id, DraftDigest FROM svcacct.AccountEvents {(lockKey ? "WITH (UPDLOCK, HOLDLOCK)" : "")} WHERE AccountId = @accountId AND IdempotencyKey = @key;
            """, new { accountId, key }, transaction, cancellationToken));
        if (stored is not { } found)
        {
            return null;
        }

        EventCreatedRecord[] items = [.. (await connection.QueryAsync<(string Kind, string EntityType, Guid Id, string Operation)>(Cmd("""
            SELECT ItemKind, EntityType, EntityId, Operation FROM svcacct.AccountEventItems WHERE EventId = @Id ORDER BY Id;
            """, new { found.Id }, transaction, cancellationToken))).Select(i => new EventCreatedRecord(i.Kind, i.EntityType, i.Id, i.Operation))];
        return new StoredAccountEvent(found.Id, found.Digest, items);
    }
}
