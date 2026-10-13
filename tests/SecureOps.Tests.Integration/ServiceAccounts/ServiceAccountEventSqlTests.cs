using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// OLAY-2c on a disposable database with 036 (EVENT-GUIDANCE-DESIGN.md §5, threat table §8): preview writes nothing; commit writes
/// every record, its history and audit, and the event in one transaction; replay, stale previews, refused closes, participant
/// limits and an injected failure leave nothing behind. Synthetic names only.
/// </summary>
public sealed class ServiceAccountEventSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All.Where(c => c != ServiceAccountCapabilities.Administer)];
    private static readonly DateOnly _on = new(2026, 10, 1);

    [ServiceAccountSqlFact]
    public async Task PasswordChanged_PreviewWritesNothing_CommitRecordsEverythingOnce_AndReplays()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "PWD");
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id, new CreateWorkRequest("PasswordChange"), _token));
        Guid accountId = account.Summary.Id;
        RequestView request = account.Requests.Single();
        (await fx.Service.MeAsync(coordinator.Principal, fx.Context, _token)).Value!.EventGuidanceAvailable.Should().BeTrue();

        EventDraft draft = new("PasswordChanged", _on, "DateOnly", "Mail", account.Summary.Version, request.Id,
            Communication: new EventCommunication(Summary: "Sahip parolanın değiştiğini bildirdi (sentetik)", ProviderMessageId: "<syn-" + fx.Suffix + "@example.invalid>"),
            Action: new EventAction(EvidenceNote: "OCO ile yapıldı (sentetik)"),
            References: [new SaExternalRef("OCO", "OCO-SYN-" + fx.Suffix)]);
        int before = await WrittenAsync(fx, accountId);
        EventPreview preview = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, draft, _token));
        (await WrittenAsync(fx, accountId)).Should().Be(before, "a preview writes and audits nothing");

        preview.CanCommit.Should().BeTrue();
        preview.Items.Select(i => i.Kind).Should().Equal("Communication", "Action", "Reference");
        EventPreviewItem action = preview.Items.Single(i => i.Kind == "Action");
        action.Label.Should().Contain("Bildirildi").And.Contain("doğrulanmadı");
        Field(action, "actionType").Should().Be("PasswordChange");
        Field(action, "result").Should().Be("Performed");
        Field(action, "recordKind").Should().Be("Intermediate");
        Field(preview.Items[0], "direction").Should().Be("Incoming");
        Field(preview.Items[0], "kind").Should().Be("Information");
        preview.NotRecorded.Should().Contain(["Verification", "AccountClosure", "RequestClose"]);
        preview.FollowUps.Select(f => f.Kind).Should().Contain(["Verify", "CloseRequest"]);
        preview.CloseReadiness!.CanClose.Should().BeTrue("a reported password change completes a password change request (rule 9)");

        var key = Guid.NewGuid();
        EventCommitResult committed = Ok(await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            draft with { IdempotencyKey = key, PreviewDigest = preview.PreviewDigest }, _token));
        committed.Replayed.Should().BeFalse();
        committed.Created.Select(c => c.Kind).Should().Equal("Communication", "Action", "Reference");
        ActionView reported = committed.Detail.Actions.Should().ContainSingle().Subject;
        reported.Result.Should().Be("Performed", "E1: an event never verifies");
        reported.VerifiedOn.Should().BeNull();
        reported.VerifiedByPerson.Should().BeNull();
        reported.RecordKind.Should().Be("Intermediate");
        reported.RequestId.Should().Be(request.Id);
        reported.ActualOn.Should().Be(_on);
        reported.References.Should().ContainSingle(r => r.Type == "OCO");
        committed.Detail.Requests.Single().Status.Should().Be("Open", "an event closes a request only when asked (rule 9)");
        committed.Detail.Requests.Single().References.Should().ContainSingle(r => r.Type == "OCO");
        committed.Detail.Communications.Should().ContainSingle().Which.Kind.Should().Be("Information");
        committed.Detail.Summary.LifecycleState.Should().Be("Active");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.AccountEvents WHERE AccountId = @accountId AND EventKind = 'PasswordChanged';", new { accountId })).Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.AccountEventItems WHERE EventId = @EventId;", new { committed.EventId })).Should().Be(3);
        string[] audit = await AuditAsync(fx);
        audit.Should().Contain(["ServiceAccount.CommunicationRecorded", "ServiceAccount.ActionReported", "ServiceAccount.EventRecorded"]);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId = @c AND DetailsJson LIKE N'%sentetik%';", new { c = fx.Context.CorrelationId }))
            .Should().Be(0, "E8: audit carries identifiers only, never the person's text");

        // E5: the same key and draft replay the first result; nothing new is written.
        int afterCommit = await WrittenAsync(fx, accountId);
        EventCommitResult replay = Ok(await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            draft with { IdempotencyKey = key, PreviewDigest = preview.PreviewDigest }, _token));
        replay.Replayed.Should().BeTrue();
        replay.EventId.Should().Be(committed.EventId);
        replay.Created.Should().BeEquivalentTo(committed.Created);
        (await WrittenAsync(fx, accountId)).Should().Be(afterCommit);
        (await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            draft with { IdempotencyKey = key, PreviewDigest = preview.PreviewDigest, Action = new EventAction(EvidenceNote: "başka") }, _token))
            .ErrorCode.Should().Be(SaErrors.IdempotencyKeyReused);
        (await WrittenAsync(fx, accountId)).Should().Be(afterCommit);
    }

    [ServiceAccountSqlFact]
    public async Task StalePreview_TamperedDigest_AndMissingKey_WriteNothing()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "STALE");
        Guid accountId = account.Summary.Id;
        EventDraft draft = new("Other", null, "Unknown", "Phone", account.Summary.Version, Communication: new EventCommunication("Incoming", "Information"));
        EventPreview preview = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, draft, _token));
        preview.Warnings.Should().ContainSingle(w => w.Code == "DateUnknown");
        int before = await WrittenAsync(fx, accountId);

        (await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId, draft with { PreviewDigest = preview.PreviewDigest }, _token))
            .ErrorCode.Should().Be(SaErrors.IdempotencyKey);
        (await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            draft with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = new string('0', 64) }, _token)).ErrorCode.Should().Be(SaErrors.EventStale);
        // The person saw one set of records; a different draft under the same digest is stale too.
        (await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            draft with { Channel = "Mail", IdempotencyKey = Guid.NewGuid(), PreviewDigest = preview.PreviewDigest }, _token)).ErrorCode.Should().Be(SaErrors.EventStale);

        // E6: the account changed after the preview.
        Ok(await fx.Service.UpdateAccountAsync(coordinator.Principal, fx.Context, accountId, new UpdateAccountRequest(account.Summary.Version, Notes: "araya giren değişiklik"), _token));
        before = await WrittenAsync(fx, accountId);
        SaResult<EventCommitResult> stale = await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            draft with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = preview.PreviewDigest }, _token);
        stale.ErrorCode.Should().Be(SaErrors.EventStale);
        stale.Field.Should().Be("accountVersion");
        (await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, draft, _token)).ErrorCode.Should().Be(SaErrors.EventStale);
        (await WrittenAsync(fx, accountId)).Should().Be(before);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.AccountEvents WHERE AccountId = @accountId;", new { accountId })).Should().Be(0);
    }

    [ServiceAccountSqlFact]
    public async Task CloseRequest_IsCheckedByTheCloseRule_OnTheWrittenRecords()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "CLOSE");
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id, new CreateWorkRequest("Deletion"), _token));
        Guid accountId = account.Summary.Id;
        RequestView deletion = account.Requests.Single();

        // E10: a reported deletion never completes a deletion request (verified closure needed) — 422 and nothing is written.
        EventDraft deleted = new("AccountDeleted", _on, "DateOnly", "Mail", account.Summary.Version, deletion.Id, CloseRequest: new EventCloseRequest("Completed"));
        EventPreview preview = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, deleted, _token));
        preview.Warnings.Should().Contain(w => w.Code == "OrMissingForDeletion");
        preview.CloseReadiness!.CanClose.Should().BeFalse();
        preview.CloseReadiness.Missing.Should().ContainSingle().Which.Code.Should().Be(ServiceAccountRules.Errors.VerifiedClosureRequired);
        Field(preview.Items.Single(i => i.Kind == "Action"), "recordKind").Should().Be("Closure");
        int before = await WrittenAsync(fx, accountId);
        SaResult<EventCommitResult> refused = await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            deleted with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = preview.PreviewDigest }, _token);
        refused.ErrorCode.Should().Be(SaErrors.CloseNotAllowed);
        ((CloseReadiness)refused.Current!).Missing.Select(m => m.Code).Should().Equal(ServiceAccountRules.Errors.VerifiedClosureRequired);
        (await WrittenAsync(fx, accountId)).Should().Be(before, "E7: the action written before the refused close is rolled back");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ActionEvents WHERE AccountId = @accountId;", new { accountId })).Should().Be(0);

        // Without the close the same event is recorded; the account stays Active (a report is not a verified closure).
        EventDraft reportOnly = deleted with { CloseRequest = null };
        EventPreview reportPreview = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, reportOnly, _token));
        EventCommitResult recorded = Ok(await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            reportOnly with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = reportPreview.PreviewDigest }, _token));
        recorded.Detail.Summary.LifecycleState.Should().Be("Active");
        recorded.Detail.Requests.Single().Status.Should().Be("Open");
        recorded.Detail.Actions.Single().Result.Should().Be("Performed");

        // A password change request closes with its reported change when the person asks for it.
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, accountId, new CreateWorkRequest("PasswordChange"), _token));
        RequestView password = account.Requests.Single(r => r.ActionType == "PasswordChange");
        EventDraft changed = new("PasswordChanged", _on, "DateOnly", "Phone", account.Summary.Version, password.Id, CloseRequest: new EventCloseRequest("Completed"));
        EventPreview closing = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, changed, _token));
        closing.NotRecorded.Should().NotContain("RequestClose");
        EventCommitResult closed = Ok(await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            changed with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = closing.PreviewDigest }, _token));
        closed.Created.Select(c => c.Kind).Should().Equal("Action", "RequestClose");
        closed.Detail.Requests.Single(r => r.Id == password.Id).Status.Should().Be("Closed");
        closed.Detail.Requests.Single(r => r.Id == password.Id).CloseOutcome.Should().Be("Completed");
        closed.Detail.Requests.Single(r => r.Id == deletion.Id).Status.Should().Be("Open", "closing one request never closes another");
    }

    [ServiceAccountSqlFact]
    public async Task ServerDerivesKinds_AndRefusesBlocksDatesAndRequestsOutsideTheTable()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "RULES");
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id, new CreateWorkRequest("Deletion"), _token));
        AccountDetail other = await CreateAccountAsync(fx, coordinator, org, "OTHER");
        other = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, other.Summary.Id, new CreateWorkRequest("PasswordChange"), _token));
        Guid accountId = account.Summary.Id;
        string version = account.Summary.Version;
        EventDraft password = new("PasswordChanged", _on, "DateOnly", "Mail", version);

        async Task<string?> Refusal(EventDraft draft) => (await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, draft, _token)).Field;
        (await Refusal(new EventDraft("OwnerReplied", _on, "DateOnly", "Mail", version, Communication: new(), Action: new()))).Should().Be("blockNotAllowed:action");
        (await Refusal(password with { Ownership = new EventOwnership(null, null, "r") })).Should().Be("blockNotAllowed:ownership");
        (await Refusal(password with { CreateRequest = new EventCreateRequest() })).Should().Be("blockNotAllowed:createRequest");
        (await Refusal(new EventDraft("OwnerChanged", _on, "DateOnly", "Mail", version, References: [new SaExternalRef("OR", "OR-1")],
            Ownership: new EventOwnership(null, null, "r")))).Should().Be("blockNotAllowed:references");
        (await Refusal(new EventDraft("OwnerReplied", _on, "DateOnly", "Mail", version))).Should().Be("communication");
        (await Refusal(new EventDraft("OwnerReplied", _on, "DateOnly", "Mail", version, Communication: new EventCommunication("Outgoing"))))
            .Should().Be("communication.direction", "the table fixes an owner reply as incoming");
        (await Refusal(new EventDraft("Other", null, "Unknown", "Mail", version, Communication: new EventCommunication("Incoming", "Verified"))))
            .Should().Be("communication.kind");
        (await Refusal(password with { EventKind = "passwordchanged" })).Should().Be("eventKind");
        (await Refusal(password with { EventKind = "1" })).Should().Be("eventKind");
        (await Refusal(password with { Channel = "Fax" })).Should().Be("channel");
        (await Refusal(password with { OccurredOn = null })).Should().Be("occurredOn");
        (await Refusal(password with { DatePrecision = "Unknown" })).Should().Be("occurredOn", "an unknown date is never invented or kept");
        (await Refusal(password with { OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3) })).Should().Be(ServiceAccountRules.Errors.DateInFuture);
        (await Refusal(password with { RequestId = other.Requests.Single().Id })).Should().Be("requestAccountMismatch", "E3: another account's request");
        (await Refusal(password with { RequestId = account.Requests.Single().Id })).Should().Be("requestTypeMismatch");
        (await Refusal(password with { CloseRequest = new EventCloseRequest("Completed") })).Should().Be("closeRequest");
        (await Refusal(password with { Action = new EventAction(PerformerTeamId: Guid.NewGuid()) })).Should().Be("directory", "only directory identities");
        (await Refusal(new EventDraft("Other", null, "Unknown", "Mail", version, Communication: new EventCommunication("Incoming", "Information",
            Summary: new string('x', ServiceAccountService.EventSummaryMax + 1))))).Should().Be("communication.summary");
        (await Refusal(new EventDraft("ServiceMoved", null, "Unknown", "Mail", version, Communication: new EventCommunication("Incoming", "Information"),
            CreateRequest: new EventCreateRequest("Deletion")))).Should().Be("createRequest", "the event decides the new request's type");
        (await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, other.Summary.Id, password, _token)).ErrorCode.Should().Be(SaErrors.EventStale);

        // E2: whatever kind the event is, the server writes its own action type, result and record kind (Unknown date stays unknown).
        EventDraft unknown = password with { OccurredOn = null, DatePrecision = "Unknown" };
        EventPreview preview = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, unknown, _token));
        EventCommitResult done = Ok(await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            unknown with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = preview.PreviewDigest }, _token));
        ActionView action = done.Detail.Actions.Single();
        (action.ActionType, action.Result, action.RecordKind, action.ActualOn, action.Precision)
            .Should().Be(("PasswordChange", "Performed", "Intermediate", (DateOnly?)null, "Unknown"));

        // Out of scope is indistinguishable from missing (404).
        SynUser outsider = await fx.UserAsync(_all);
        await fx.GrantAsync(outsider, ScopeKind.Organization, await fx.OrganizationAsync("SYN EVT DIS " + fx.Suffix));
        (await fx.Service.PreviewEventAsync(outsider.Principal, fx.Context, accountId, password, _token)).ErrorCode.Should().Be(SaErrors.NotFound);
        (await fx.Service.CommitEventAsync(outsider.Principal, fx.Context, accountId, password with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = "x" }, _token))
            .ErrorCode.Should().Be(SaErrors.NotFound);
        SynUser viewer = await fx.UserAsync(ServiceAccountCapabilities.View);
        await fx.GrantAsync(viewer, ScopeKind.Organization, org);
        (await fx.Service.PreviewEventAsync(viewer.Principal, fx.Context, accountId, password, _token)).ErrorCode.Should().Be(SaErrors.Forbidden);
    }

    [ServiceAccountSqlFact]
    public async Task Participant_RecordsOnlyOnItsOwnRequest_NeverProposesOwnershipOpensOrCloses()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        Guid ownerTeam = await fx.TeamAsync("SYN EVT SAHIP " + fx.Suffix, org), helperTeam = await fx.TeamAsync("SYN EVT YARDIM " + fx.Suffix, null);
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "PART");
        account = Ok(await fx.Service.ChangeOwnershipAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new OwnershipChangeRequest(account.Summary.Version, ownerTeam, null, "Confirm", "Sentetik sahiplik kararı"), _token));
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new CreateWorkRequest("PasswordChange", TargetTeamId: helperTeam), _token));
        Guid accountId = account.Summary.Id, mine = account.Requests.Single().Id;
        SynUser member = await fx.UserAsync(_all);
        await fx.GrantAsync(member, ScopeKind.Team, team: helperTeam);
        string version = account.Summary.Version;

        // E4: on its own request a participant may record the reported change.
        EventDraft own = new("PasswordChanged", _on, "DateOnly", "Mail", version, mine);
        EventPreview allowed = Ok(await fx.Service.PreviewEventAsync(member.Principal, fx.Context, accountId, own, _token));
        allowed.CanCommit.Should().BeTrue();
        // ... but not close it, not without a request, and never an ownership proposal or a new request.
        foreach (EventDraft denied in new[]
        {
            own with { CloseRequest = new EventCloseRequest("Completed") },
            own with { RequestId = null },
            new EventDraft("OwnerChanged", _on, "DateOnly", "Mail", version, Ownership: new EventOwnership(helperTeam, null, "Sentetik öneri")),
            new EventDraft("ServiceMoved", _on, "DateOnly", "Mail", version, Communication: new EventCommunication("Incoming", "Information"),
                CreateRequest: new EventCreateRequest())
        })
        {
            EventPreview preview = Ok(await fx.Service.PreviewEventAsync(member.Principal, fx.Context, accountId, denied, _token));
            preview.CanCommit.Should().BeFalse(denied.EventKind);
            preview.Items.Where(i => !i.Allowed).Should().NotBeEmpty().And.OnlyContain(i => i.DeniedReason == "NotParticipantRequest");
            SaResult<EventCommitResult> refused = await fx.Service.CommitEventAsync(member.Principal, fx.Context, accountId,
                denied with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = preview.PreviewDigest }, _token);
            refused.ErrorCode.Should().Be(SaErrors.Forbidden);
            refused.Field.Should().Be("eventItemDenied");
        }

        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.AccountEvents WHERE AccountId = @accountId;", new { accountId })).Should().Be(0);
        Ok(await fx.Service.CommitEventAsync(member.Principal, fx.Context, accountId, own with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = allowed.PreviewDigest }, _token))
            .Detail.Actions.Single().RequestId.Should().Be(mine);
    }

    [ServiceAccountSqlFact]
    public async Task OwnerReplied_MovesLastReplyOnlyForward_OwnerChangedOnlyProposes_ServiceMovedOpensReview()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        Guid team = await fx.TeamAsync("SYN EVT TAKIM " + fx.Suffix, org);
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "REPLY");
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            new CreateWorkRequest("Review", LastReplyOn: new(2026, 9, 20)), _token));
        Guid accountId = account.Summary.Id;
        RequestView review = account.Requests.Single();

        EventDraft earlier = new("OwnerReplied", new(2026, 9, 10), "DateOnly", "Mail", account.Summary.Version, review.Id, Communication: new EventCommunication());
        EventPreview back = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, earlier, _token));
        back.Warnings.Should().Contain(w => w.Code == "ReplyDateEarlier");
        back.Items.Should().NotContain(i => i.Kind == "RequestUpdate", "a reply date never moves back");

        EventDraft later = earlier with { OccurredOn = _on };
        EventPreview forward = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, later, _token));
        Field(forward.Items.Single(i => i.Kind == "RequestUpdate"), "lastReplyOn").Should().Be("2026-10-01");
        account = Ok(await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            later with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = forward.PreviewDigest }, _token)).Detail;
        account.Requests.Single().LastReplyOn.Should().Be(_on);
        CommunicationView reply = account.Communications.Single();
        (reply.Direction, reply.Kind, reply.MeaningfulReply).Should().Be(("Incoming", "Reply", (bool?)true));
        account.Actions.Should().BeEmpty("an owner reply is correspondence, not an action");

        EventDraft owner = new("OwnerChanged", _on, "DateOnly", "Meeting", account.Summary.Version, Ownership: new EventOwnership(team, null, "Toplantıda söylendi (sentetik)"));
        EventPreview proposal = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, owner, _token));
        proposal.NotRecorded.Should().Contain("OwnershipDecision");
        account = Ok(await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            owner with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = proposal.PreviewDigest }, _token)).Detail;
        account.Ownership.Should().ContainSingle().Which.State.Should().Be("Proposed");
        account.Summary.OwnerTeam.Should().BeNull("a proposal is not a decision");

        EventDraft moved = new("ServiceMoved", _on, "DateOnly", "Mail", account.Summary.Version, Communication: new EventCommunication("Incoming", "Information"),
            CreateRequest: new EventCreateRequest(TargetTeamId: team, Notes: "Taşınan servisin kullanımı yeniden taranacak"));
        EventPreview movedPreview = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, moved, _token));
        movedPreview.FollowUps.Should().Contain(f => f.Kind == "UsageTab");
        account = Ok(await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
            moved with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = movedPreview.PreviewDigest }, _token)).Detail;
        account.Requests.Should().ContainSingle(r => r.ActionType == "Review" && r.TargetTeam != null && r.TargetTeam.Id == team && r.Status == "Open");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.AccountEvents WHERE AccountId = @accountId;", new { accountId })).Should().Be(3);
    }

    [ServiceAccountSqlFact]
    public async Task InjectedFailure_AtTheLastStep_LeavesNoRecordHistoryOrAudit()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "FAULT");
        account = Ok(await fx.Service.CreateRequestAsync(coordinator.Principal, fx.Context, account.Summary.Id, new CreateWorkRequest("PasswordChange"), _token));
        Guid accountId = account.Summary.Id;
        EventDraft draft = new("PasswordChanged", _on, "DateOnly", "Mail", account.Summary.Version, account.Requests.Single().Id,
            Communication: new EventCommunication(Summary: "sentetik"), References: [new SaExternalRef("OCO", "OCO-FAULT-" + fx.Suffix)],
            CloseRequest: new EventCloseRequest("Completed"));
        EventPreview preview = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, draft, _token));
        int before = await WrittenAsync(fx, accountId);
        string[] auditBefore = await AuditAsync(fx);

        // E7: the event header is the last insert; failing it after every other record was written must roll all of them back.
        string trigger = "TR_SynEventFault_" + fx.Suffix;
        await using (SqlConnection connection = fx.Connection())
        {
            await connection.ExecuteAsync($"""
                CREATE TRIGGER svcacct.{trigger} ON svcacct.AccountEvents AFTER INSERT AS
                BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE AccountId = '{accountId:D}') THROW 51999, 'Synthetic event fault.', 1; END;
                """);
        }

        try
        {
            SaResult<EventCommitResult> failed = await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId,
                draft with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = preview.PreviewDigest }, _token);
            failed.ErrorCode.Should().Be(SaErrors.Unavailable);
            fx.SqlDiagnostics.Should().Contain(d => d.StartsWith("Number=51999", StringComparison.Ordinal));
        }
        finally
        {
            await using SqlConnection connection = fx.Connection();
            await connection.ExecuteAsync($"DROP TRIGGER svcacct.{trigger};");
        }

        (await WrittenAsync(fx, accountId)).Should().Be(before);
        (await AuditAsync(fx)).Should().Equal(auditBefore);
        AccountDetail after = Ok(await fx.Service.AccountAsync(coordinator.Principal, fx.Context, accountId, _token));
        after.Requests.Single().Status.Should().Be("Open");
        after.Actions.Should().BeEmpty();
        after.Communications.Should().BeEmpty();
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ExternalRecords WHERE Number = @n;", new { n = "OCO-FAULT-" + fx.Suffix })).Should().Be(0);
    }

    [ServiceAccountSqlFact]
    public async Task ConcurrentDoubleClick_RecordsOnce_TheOtherReplays()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync();
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "TWICE");
        Guid accountId = account.Summary.Id;
        EventDraft draft = new("Other", _on, "DateOnly", "Mail", account.Summary.Version, Communication: new EventCommunication("Incoming", "Information"));
        EventPreview preview = Ok(await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, accountId, draft, _token));
        EventDraft commit = draft with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = preview.PreviewDigest };

        SaResult<EventCommitResult>[] results = await Task.WhenAll(
            fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId, commit, _token),
            fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, accountId, commit, _token));
        results.Should().OnlyContain(r => r.IsSuccess, $"safe SQL diagnostics: {string.Join("; ", fx.SqlDiagnostics)}");
        results.Count(r => r.Value!.Replayed).Should().Be(1);
        results.Select(r => r.Value!.EventId).Distinct().Should().ContainSingle();
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.AccountEvents WHERE AccountId = @accountId;", new { accountId })).Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.CommunicationAccounts WHERE AccountId = @accountId;", new { accountId })).Should().Be(1);
    }

    [ServiceAccountSql030Fact]
    public async Task Before036_TheFlowIsClosedAndHonest_AndNothingIsWritten()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org) = await SetupAsync(Environment.GetEnvironmentVariable(ServiceAccountSql030FactAttribute.Variable));
        (await fx.CountAsync("SELECT CASE WHEN OBJECT_ID(N'svcacct.AccountEvents', N'U') IS NULL THEN 0 ELSE 1 END")).Should().Be(0,
            "this test needs a database without migration 036");
        AccountDetail account = await CreateAccountAsync(fx, coordinator, org, "OLD");
        (await fx.Service.MeAsync(coordinator.Principal, fx.Context, _token)).Value!.EventGuidanceAvailable.Should().BeFalse();
        EventDraft draft = new("Other", null, "Unknown", "Mail", account.Summary.Version, Communication: new EventCommunication("Incoming", "Information"));
        (await fx.Service.PreviewEventAsync(coordinator.Principal, fx.Context, account.Summary.Id, draft, _token)).ErrorCode.Should().Be(SaErrors.EventsNotInstalled);
        (await fx.Service.CommitEventAsync(coordinator.Principal, fx.Context, account.Summary.Id,
            draft with { IdempotencyKey = Guid.NewGuid(), PreviewDigest = new string('0', 64) }, _token)).ErrorCode.Should().Be(SaErrors.EventsNotInstalled);
        (await WrittenAsync030(fx, account.Summary.Id)).Should().Be(1, "only the account creation history row exists");
    }

    private static Task<int> WrittenAsync030(ServiceAccountSqlFixture fx, Guid accountId) => fx.CountAsync("""
        SELECT (SELECT COUNT(*) FROM svcacct.ActionEvents WHERE AccountId = @accountId)
             + (SELECT COUNT(*) FROM svcacct.CommunicationAccounts WHERE AccountId = @accountId)
             + (SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @accountId);
        """, new { accountId });

    /// <summary>Every row an event could add for this account: records, links, history, events (audit is checked by correlation).</summary>
    private static Task<int> WrittenAsync(ServiceAccountSqlFixture fx, Guid accountId) => fx.CountAsync("""
        SELECT (SELECT COUNT(*) FROM svcacct.ActionEvents WHERE AccountId = @accountId)
             + (SELECT COUNT(*) FROM svcacct.CommunicationAccounts WHERE AccountId = @accountId)
             + (SELECT COUNT(*) FROM svcacct.ExternalRecordLinks WHERE AccountId = @accountId)
             + (SELECT COUNT(*) FROM svcacct.WorkRequests WHERE AccountId = @accountId AND Status = 'Closed')
             + (SELECT COUNT(*) FROM svcacct.OwnershipAssignments WHERE AccountId = @accountId)
             + (SELECT COUNT(*) FROM svcacct.History WHERE AccountId = @accountId)
             + (SELECT COUNT(*) FROM svcacct.AccountEvents WHERE AccountId = @accountId);
        """, new { accountId });

    private static async Task<string[]> AuditAsync(ServiceAccountSqlFixture fx)
    {
        await using SqlConnection connection = fx.Connection();
        return [.. await connection.QueryAsync<string>("SELECT Action FROM audit.AuditLog WHERE CorrelationId = @c ORDER BY AuditLogId;", new { c = fx.Context.CorrelationId })];
    }

    private static string? Field(EventPreviewItem item, string name) => item.Fields.Single(f => f.Name == name).Value;

    private static async Task<(ServiceAccountSqlFixture Fx, SynUser Coordinator, Guid Org)> SetupAsync(string? connection = null)
    {
        ServiceAccountSqlFixture fx = new(connection);
        Guid org = await fx.OrganizationAsync("SYN EVT ORG " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        return (fx, coordinator, org);
    }

    private static async Task<AccountDetail> CreateAccountAsync(ServiceAccountSqlFixture fx, SynUser user, Guid org, string name) =>
        Ok(await fx.Service.CreateAccountAsync(user.Principal, fx.Context, new CreateAccountRequest($"SYN{fx.Suffix}_{name}", null, org, "Sentetik test hesabı"), _token));

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
