using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ItsmRecords;
using SecureOps.Infrastructure.ItsmRequests;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// OR-2 (ADR-0030, PROPOSED; migration 035) on the real svcacct schema with the Fake creator and the Fake ITSM lookup, tracking
/// and user match: preview, single send, idempotent replay, OutcomeUnknown and its reconciliation, the requester gate and the
/// refusals, each with what it writes (submission rows, events, history, audit). Synthetic data and placeholder numbers only;
/// nothing contacts Turuncu Hat or a managed server. Written in the Linux session and NOT run there (no SQL Server): the Windows
/// coordinator runs it against a harness database through 036 (sa-sql-harness.ps1).
/// </summary>
[Collection(ServiceAccountWriteGateCollection.Name)]
public sealed class ServiceAccountOperationalRecordSqlTests
{
    private const string _statement = "Sentetik: kendi yönetici hesabımla SYN-JUMP01 üzerinden çalıştırdım";
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All];

    [ServiceAccountSqlFact]
    public async Task PreviewThenSubmit_CreatesOnce_AndRecordsExactlyWhatWasPreviewed()
    {
        Setup s = await SetupAsync();
        (ChangePlanView plan, AccountDetail account) = await ApprovedAsync(s);

        OperationalRecordPreview template = Ok(await s.Service.PreviewOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordPreviewRequest(), _token));
        (template.CanSubmit, template.BlockedReason, template.Fingerprint).Should().Be((false, "environmentRequired", (string?)null));
        template.Servers.Should().Equal("SYN-APP01");
        template.Requester.Should().Be(new OperationalRecordRequesterView("Matched", template.Requester.Label, "920001", true));
        OperationalRecordPreview preview = Ok(await s.Service.PreviewOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordPreviewRequest(Environment: "test"), _token));
        (preview.CanSubmit, preview.Environment, preview.Synthetic).Should().Be((true, "Test", true));
        preview.SentDescription.Should().EndWith(OperationalRecordRules.PlanReference(plan.Id, account.Summary.Id));
        (await CountAsync(s, plan.Id)).Should().Be(new Counts(0, 0, 0, 0), "a preview writes nothing");

        OperationalRecordSubmitResult sent = Ok(await SubmitAsync(s, plan.Id, account.Summary.Id, preview, Guid.NewGuid()));

        sent.Replayed.Should().BeFalse();
        sent.Submission.Should().Match<OperationalRecordSubmissionView>(v => v.State == "Created" && v.ExternalId == FakeItsmRequestCreator.SourceId
            && v.RecordNumber!.StartsWith("OR-") && v.Synthetic && v.Environment == "Test" && v.ShortDescription == preview.ShortDescription
            && v.Description == preview.SentDescription && !v.NeedsReconciliation);
        sent.Submission.Events.Select(e => e.Event).Should().Equal("Started", "Created");
        (await CountAsync(s, plan.Id)).Should().Be(new Counts(1, 2, 2, 2));
        (await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.ChangePlanRecordSubmissions WHERE PlanId = @id AND Fingerprint = @fp",
            new { id = plan.Id, fp = preview.Fingerprint })).Should().Be(1, "the stored fingerprint is the previewed one");

        PlanOperationalRecordsView panel = Ok(await s.Service.OperationalRecordsAsync(s.Verifier.Principal, s.Fx.Context, plan.Id, _token));
        OperationalRecordAccountView row = panel.Accounts.Single();
        (row.Current!.State, row.CanOpen, row.OpenBlockedReason, row.CanLink).Should().Be(("Created", false, "submissionExists", false));
        OperationalRecordTrackingView tracking = Ok(await s.Service.OperationalRecordTrackingAsync(s.Verifier.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            _token));
        (tracking.Status, tracking.ActivitiesKnown, tracking.ActivityCount, tracking.CommentsKnown, tracking.CommentCount, tracking.Synthetic)
            .Should().Be(("Found", true, (int?)2, true, (int?)2, true));
        System.Text.Json.JsonSerializer.Serialize(new object[] { sent, panel }).Should().NotContainEquivalentOf("password").And.NotContainEquivalentOf("parola:");
    }

    [ServiceAccountSqlFact]
    public async Task Replay_SameKeySameContent_ReturnsTheFirstResult_OtherContentIs409_AndOnlyOneRowExists()
    {
        Setup s = await SetupAsync();
        (ChangePlanView plan, AccountDetail account) = await ApprovedAsync(s);
        OperationalRecordPreview preview = await SendablePreviewAsync(s, plan, account);
        var key = Guid.NewGuid();
        OperationalRecordSubmitResult first = Ok(await SubmitAsync(s, plan.Id, account.Summary.Id, preview, key));

        OperationalRecordSubmitResult again = Ok(await SubmitAsync(s, plan.Id, account.Summary.Id, preview, key));
        SaResult<OperationalRecordSubmitResult> other = await s.Service.SubmitOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordSubmitRequest(preview.ShortDescription, preview.Description, preview.Environment, new string('e', 64), key), _token);
        SaResult<OperationalRecordSubmitResult> second = await SubmitAsync(s, plan.Id, account.Summary.Id, preview, Guid.NewGuid());

        (again.Replayed, again.Submission.Id, again.Submission.RecordNumber).Should().Be((true, first.Submission.Id, first.Submission.RecordNumber));
        (other.ErrorCode, other.Field).Should().Be((SaErrors.IdempotencyKeyReused, "idempotencyKey"));
        (second.ErrorCode, second.Field).Should().Be((SaErrors.ChangePlanState, "submissionExists"), "one live OR per plan account");
        (await CountAsync(s, plan.Id)).Should().Be(new Counts(1, 2, 2, 2));
    }

    [ServiceAccountSqlFact]
    public async Task Requester_NotMatched_CannotSubmit_AndNothingIsWritten()
    {
        foreach (string? login in new[] { "someone.else", "synthetic.dup", "synthetic.down", null })
        {
            Setup s = await SetupAsync(login);
            (ChangePlanView plan, AccountDetail account) = await ApprovedAsync(s);

            OperationalRecordPreview preview = Ok(await s.Service.PreviewOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
                new OperationalRecordPreviewRequest(Environment: "Test"), _token));
            SaResult<OperationalRecordSubmitResult> sent = await s.Service.SubmitOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
                new OperationalRecordSubmitRequest(preview.ShortDescription, preview.Description, "Test", new string('a', 64), Guid.NewGuid()), _token);

            (preview.CanSubmit, preview.BlockedReason, preview.Fingerprint, preview.Requester.UserId).Should().Be((false, "requesterNotMatched", (string?)null,
                (string?)null), login ?? "no login name");
            (sent.ErrorCode, sent.Field).Should().Be((SaErrors.ChangePlanState, "requesterNotMatched"), login ?? "no login name");
            (await CountAsync(s, plan.Id)).Should().Be(new Counts(0, 0, 0, 0));
        }
    }

    [ServiceAccountSqlFact]
    public async Task Submit_TextEditedAfterThePreview_IsOrPreviewStale_AndWritesNothing()
    {
        Setup s = await SetupAsync();
        (ChangePlanView plan, AccountDetail account) = await ApprovedAsync(s);
        OperationalRecordPreview preview = await SendablePreviewAsync(s, plan, account);

        SaResult<OperationalRecordSubmitResult> edited = await s.Service.SubmitOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordSubmitRequest(preview.ShortDescription, preview.Description + " ek", preview.Environment, preview.Fingerprint, Guid.NewGuid()), _token);
        SaResult<OperationalRecordSubmitResult> otherEnvironment = await s.Service.SubmitOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id,
            account.Summary.Id, new OperationalRecordSubmitRequest(preview.ShortDescription, preview.Description, "Prod", preview.Fingerprint, Guid.NewGuid()), _token);

        (edited.ErrorCode, edited.Field).Should().Be((SaErrors.ChangePlanState, "orPreviewStale"));
        (otherEnvironment.ErrorCode, otherEnvironment.Field).Should().Be((SaErrors.ChangePlanState, "orPreviewStale"));
        (await CountAsync(s, plan.Id)).Should().Be(new Counts(0, 0, 0, 0));
    }

    [ServiceAccountSqlFact]
    public async Task OutcomeUnknown_IsNeverResent_ThenALinkFoundByTheLookupIsVerified()
    {
        Setup s = await SetupAsync();
        (ChangePlanView plan, AccountDetail account) = await ApprovedAsync(s);
        OperationalRecordPreview preview = await SendablePreviewAsync(s, plan, account, "[fake:unknown]");
        OperationalRecordSubmitResult unknown = Ok(await SubmitAsync(s, plan.Id, account.Summary.Id, preview, Guid.NewGuid()));
        (unknown.Submission.State, unknown.Submission.ReasonCode, unknown.Submission.RecordNumber, unknown.Submission.NeedsReconciliation)
            .Should().Be(("OutcomeUnknown", "Rejected", (string?)null, true));
        (await SubmitAsync(s, plan.Id, account.Summary.Id, preview, Guid.NewGuid())).Field.Should().Be("submissionExists", "an unclear OR blocks a second one");

        OperationalRecordCandidate missing = Ok(await s.Service.OperationalRecordCandidateAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordCandidateRequest("OR-000009"), _token));
        OperationalRecordCandidate found = Ok(await s.Service.OperationalRecordCandidateAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordCandidateRequest("or-000001"), _token));
        (missing.LookupStatus, missing.Tracking, missing.CanLink).Should().Be(("NotFound", (OperationalRecordTrackingView?)null, true));
        (found.Number, found.LookupStatus, found.TitleMatchesSubmission, found.Tracking!.Status).Should().Be(("OR-000001", "Found", (bool?)false, "Found"));
        (await CountAsync(s, plan.Id)).Should().Be(new Counts(1, 2, 2, 2), "a candidate lookup writes nothing");

        PlanOperationalRecordsView linked = Ok(await s.Service.LinkOperationalRecordAsync(s.Second.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordLinkRequest(unknown.Submission.Id, "OR-000001", "Sentetik: Turuncu Hat'ta plan referansıyla bulundu"), _token));

        linked.Accounts.Single().Current.Should().Match<OperationalRecordSubmissionView>(v => v.State == "Linked" && v.RecordNumber == "OR-000001"
            && v.LinkVerification == "Found" && v.ExternalId == "900001" && v.Events.Count == 3 && v.Events.Last().Reason!.Contains("plan referansıyla"));
        (await CountAsync(s, plan.Id)).Should().Be(new Counts(1, 3, 3, 3));
        (await s.Service.ConfirmOperationalRecordNotCreatedAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordNotCreatedRequest(unknown.Submission.Id, "Sentetik"), _token)).Field.Should().Be("submissionResolved");
    }

    [ServiceAccountSqlFact]
    public async Task OutcomeUnknown_LinkNotFoundByTheLookup_IsKeptAsNotVerified_NeverAsAbsent()
    {
        Setup s = await SetupAsync();
        (ChangePlanView plan, AccountDetail account) = await ApprovedAsync(s);
        OperationalRecordSubmitResult unknown = Ok(await SubmitAsync(s, plan.Id, account.Summary.Id, await SendablePreviewAsync(s, plan, account, "[fake:noid]"),
            Guid.NewGuid()));

        PlanOperationalRecordsView linked = Ok(await s.Service.LinkOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordLinkRequest(unknown.Submission.Id, "OR-000003", "Sentetik: numara elle bulundu, arama cevap vermedi"), _token));

        linked.Accounts.Single().Current.Should().Match<OperationalRecordSubmissionView>(v => v.State == "Linked" && v.LinkVerification == "NotVerified"
            && v.ExternalId == null);
        OperationalRecordTrackingView tracking = Ok(await s.Service.OperationalRecordTrackingAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            _token));
        (tracking.Status, tracking.LastModified, tracking.ActivityCount, tracking.CommentCount).Should().Be(("Unknown", (string?)null, (int?)null, (int?)null),
            "an unanswered lookup is unknown, never zero");
    }

    [ServiceAccountSqlFact]
    public async Task OutcomeUnknown_ConfirmedNotCreated_AllowsANewExplicitAttempt()
    {
        Setup s = await SetupAsync();
        (ChangePlanView plan, AccountDetail account) = await ApprovedAsync(s);
        OperationalRecordSubmitResult unknown = Ok(await SubmitAsync(s, plan.Id, account.Summary.Id, await SendablePreviewAsync(s, plan, account, "[fake:timeout]"),
            Guid.NewGuid()));
        (unknown.Submission.State, unknown.Submission.ReasonCode).Should().Be(("OutcomeUnknown", "Timeout"));

        (await s.Service.ConfirmOperationalRecordNotCreatedAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordNotCreatedRequest(unknown.Submission.Id, "   "), _token)).Field.Should().Be("reason");
        PlanOperationalRecordsView confirmed = Ok(await s.Service.ConfirmOperationalRecordNotCreatedAsync(s.Planner.Principal, s.Fx.Context, plan.Id,
            account.Summary.Id, new OperationalRecordNotCreatedRequest(unknown.Submission.Id, "Sentetik: Turuncu Hat'ta plan referansıyla arandı, kayıt yok"), _token));
        OperationalRecordAccountView row = confirmed.Accounts.Single();
        (row.Current!.State, row.CanOpen).Should().Be(("ConfirmedNotCreated", true));

        OperationalRecordSubmitResult retried = Ok(await SubmitAsync(s, plan.Id, account.Summary.Id, await SendablePreviewAsync(s, plan, account), Guid.NewGuid()));
        retried.Submission.State.Should().Be("Created");
        Ok(await s.Service.OperationalRecordsAsync(s.Planner.Principal, s.Fx.Context, plan.Id, _token)).Accounts.Single().Earlier.Should().ContainSingle()
            .Which.State.Should().Be("ConfirmedNotCreated");
    }

    [ServiceAccountSqlFact]
    public async Task Reconcile_ASendingRowStillInFlight_PartiallyCreatedNotCreated_AndANumberHeldElsewhere_AreRefused()
    {
        Setup s = await SetupAsync();
        (ChangePlanView plan, AccountDetail account) = await ApprovedAsync(s, accounts: 2);
        Guid other = plan.Accounts.Single(a => a.AccountId != account.Summary.Id).AccountId;
        Guid sending = await InsertSendingAsync(s, plan.Id, account.Summary.Id);
        (await s.Service.ConfirmOperationalRecordNotCreatedAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordNotCreatedRequest(sending, "Sentetik"), _token)).Field.Should().Be("submissionInFlight");

        OperationalRecordPreview preview = Ok(await s.Service.PreviewOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, other,
            new OperationalRecordPreviewRequest(Description: "gövde [fake:partial]", Environment: "Test"), _token));
        OperationalRecordSubmitResult partial = Ok(await s.Service.SubmitOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, other,
            new OperationalRecordSubmitRequest(preview.ShortDescription, preview.Description, preview.Environment, preview.Fingerprint, Guid.NewGuid()), _token));
        (partial.Submission.State, partial.Submission.ExternalId).Should().Be(("PartiallyCreated", "900001"));
        (await s.Service.ConfirmOperationalRecordNotCreatedAsync(s.Planner.Principal, s.Fx.Context, plan.Id, other,
            new OperationalRecordNotCreatedRequest(partial.Submission.Id, "Sentetik"), _token)).Field.Should().Be("submissionResolved", "the record exists");

        await s.Fx.CountAsync("UPDATE svcacct.ChangePlanRecordSubmissions SET State = 'Linked', ExternalNumber = N'OR-000001', LinkVerification = 'NotVerified' WHERE Id = @sending; SELECT 0;",
            new { sending });
        (await s.Service.LinkOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, other,
            new OperationalRecordLinkRequest(partial.Submission.Id, "OR-000001", "Sentetik"), _token)).Field.Should().Be("recordNumberInUse");
    }

    [ServiceAccountSqlFact]
    public async Task Refusals_PlanNotApproved_CreationDisabled_OutsideScope_AndNoBasis_WriteNothing()
    {
        Setup s = await SetupAsync();
        AccountDetail account = await AccountAsync(s, "R1");
        await ScanAsync(s, account);
        ChangePlanView draft = Ok(await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context,
            new CreateChangePlanRequest("Sentetik OR planı " + s.Fx.Suffix, [new ChangePlanAccountInput(account.Summary.Id, "gmsaSynOr0")]), _token));
        (await s.Service.PreviewOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, draft.Id, account.Summary.Id, new OperationalRecordPreviewRequest(), _token))
            .Field.Should().Be("planNotApproved");
        (await s.Service.OperationalRecordsAsync(s.Planner.Principal, s.Fx.Context, draft.Id, _token)).Value!.UnavailableReason.Should().Be("planNotApproved");

        (ChangePlanView plan, AccountDetail approved) = await ApprovedAsync(s);
        ServiceAccountService disabled = s.Fx.WithOperationalRecords(Ports(new DisabledItsmRequestCreator()));
        (await disabled.PreviewOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, approved.Summary.Id, new OperationalRecordPreviewRequest(), _token))
            .ErrorCode.Should().Be(SaErrors.OperationalRecordCreationDisabled);
        PlanOperationalRecordsView off = Ok(await disabled.OperationalRecordsAsync(s.Planner.Principal, s.Fx.Context, plan.Id, _token));
        (off.CreationAvailable, off.Provider, off.UnavailableReason, off.Accounts.Single().CanOpen).Should().Be((false, "Disabled", "creationDisabled", false));

        SynUser outsider = await s.Fx.UserWithItsmIdentityAsync("synthetic.user", null, _all);
        (await s.Service.PreviewOperationalRecordAsync(outsider.Principal, s.Fx.Context, plan.Id, approved.Summary.Id, new OperationalRecordPreviewRequest(), _token))
            .ErrorCode.Should().Be(SaErrors.NotFound, "outside scope looks like missing");
        SynUser reader = await s.Fx.UserWithItsmIdentityAsync("synthetic.user", null, ServiceAccountCapabilities.View);
        await s.Fx.GrantAsync(reader, ScopeKind.Organization, s.Org);
        (await s.Service.PreviewOperationalRecordAsync(reader.Principal, s.Fx.Context, plan.Id, approved.Summary.Id, new OperationalRecordPreviewRequest(), _token))
            .ErrorCode.Should().Be(SaErrors.Forbidden);
        Ok(await s.Service.OperationalRecordsAsync(reader.Principal, s.Fx.Context, plan.Id, _token)).Accounts.Single().OpenBlockedReason.Should().Be("noPermission");
        (await CountAsync(s, plan.Id)).Should().Be(new Counts(0, 0, 0, 0));
    }

    private sealed record Setup(ServiceAccountSqlFixture Fx, ServiceAccountService Service, SynUser Planner, SynUser Verifier, SynUser Second, Guid Org);

    private sealed record Counts(int Submissions, int Events, int History, int Audit);

    /// <summary>The planner's login name feeds the Fake user match: <c>synthetic.user</c> is Matched with a home location.</summary>
    private static async Task<Setup> SetupAsync(string? plannerLogin = "synthetic.user")
    {
        ServiceAccountSqlFixture fx = new();
        Guid org = await fx.OrganizationAsync("SYN OR ORG " + fx.Suffix);
        SynUser planner = await fx.UserWithItsmIdentityAsync(plannerLogin, null, _all);
        SynUser verifier = await fx.UserWithItsmIdentityAsync("synthetic.user", null, _all);
        SynUser second = await fx.UserWithItsmIdentityAsync("synthetic.user", "990001", _all);
        foreach (SynUser user in new[] { planner, verifier, second })
        {
            await fx.GrantAsync(user, ScopeKind.Organization, org);
        }

        return new Setup(fx, fx.WithOperationalRecords(Ports(new FakeItsmRequestCreator())), planner, verifier, second, org);
    }

    private static ServiceAccountOperationalRecordPorts Ports(IItsmRequestCreator creator) =>
        new(creator, new FakeItsmUserMapping(), new FakeItsmRecordLookup(), new FakeItsmRecordTracking(), TimeSpan.FromSeconds(30));

    private static async Task<AccountDetail> AccountAsync(Setup s, string label) =>
        Ok(await s.Fx.Service.CreateAccountAsync(s.Planner.Principal, s.Fx.Context,
            new CreateAccountRequest($"SYN_OR_{label}_{s.Fx.Suffix}", "SYN", s.Org, "Sentetik OR hesabı"), _token));

    private static async Task ScanAsync(Setup s, AccountDetail account)
    {
        string searched = $"SYN\\{account.Summary.AccountName}";
        byte[] file = ServiceAccountUsageScanBatchSqlTests.Discovery([searched], ("SYN-APP01", "WindowsService", "SynSvc", searched));
        Ok(await s.Fx.Service.AttachUsageScanToAccountsAsync(s.Planner.Principal, s.Fx.Context, [account.Summary.Id], "or-scan.json", file, _statement, _token))
            .Results.Single().Outcome.Should().Be("Attached");
    }

    /// <summary>Planned and previewed by the planner, approved by the verifier; returns the plan and its first account.</summary>
    private static async Task<(ChangePlanView Plan, AccountDetail Account)> ApprovedAsync(Setup s, int accounts = 1)
    {
        AccountDetail[] created = new AccountDetail[accounts];
        for (int i = 0; i < accounts; i++)
        {
            created[i] = await AccountAsync(s, "A" + i);
            await ScanAsync(s, created[i]);
        }

        ChangePlanView draft = Ok(await s.Fx.Service.CreateChangePlanAsync(s.Planner.Principal, s.Fx.Context, new CreateChangePlanRequest("Sentetik OR planı " + s.Fx.Suffix,
            [.. created.Select((a, i) => new ChangePlanAccountInput(a.Summary.Id, "gmsaSynOr" + i))]), _token));
        ChangePlanView previewed = Ok(await s.Fx.Service.PreviewChangePlanAsync(s.Planner.Principal, s.Fx.Context, draft.Id, new PreviewChangePlanRequest(draft.Version),
            _token));
        DateTimeOffset start = DateTimeOffset.UtcNow.AddHours(-1);
        ChangePlanView approved = Ok(await s.Fx.Service.ApproveChangePlanAsync(s.Verifier.Principal, s.Fx.Context, previewed.Id,
            new ApproveChangePlanRequest(previewed.Preview!.Version, previewed.Preview.Sha256, "oco-syn-2001", start, start.AddHours(4), "Sentetik onay"), _token));
        return (approved, created[0]);
    }

    private static async Task<OperationalRecordPreview> SendablePreviewAsync(Setup s, ChangePlanView plan, AccountDetail account, string? marker = null)
    {
        OperationalRecordPreview preview = Ok(await s.Service.PreviewOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, plan.Id, account.Summary.Id,
            new OperationalRecordPreviewRequest(Description: marker is null ? null : "Sentetik gövde " + marker, Environment: "Test"), _token));
        preview.CanSubmit.Should().BeTrue(preview.BlockedReason ?? string.Empty);
        return preview;
    }

    private static Task<SaResult<OperationalRecordSubmitResult>> SubmitAsync(Setup s, Guid planId, Guid accountId, OperationalRecordPreview preview, Guid key) =>
        s.Service.SubmitOperationalRecordAsync(s.Planner.Principal, s.Fx.Context, planId, accountId,
            new OperationalRecordSubmitRequest(preview.ShortDescription, preview.Description, preview.Environment, preview.Fingerprint, key), _token);

    /// <summary>A Sending row as a request in flight leaves it (written directly; only in the disposable test database).</summary>
    private static async Task<Guid> InsertSendingAsync(Setup s, Guid planId, Guid accountId)
    {
        var id = Guid.NewGuid();
        await using SqlConnection connection = s.Fx.Connection();
        await connection.ExecuteAsync("""
            INSERT INTO svcacct.ChangePlanRecordSubmissions(Id, PlanId, AccountId, PreviewId, IdempotencyKey, Fingerprint, Provider, RecordKind, Environment,
                RequesterExternalId, ShortDescription, Description, State, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
            SELECT @id, p.Id, @accountId, v.Id, NEWID(), REPLICATE('f', 64), 'Fake', 'OR', N'Test', N'920001', N'Sentetik', N'Sentetik', 'Sending', p.CreatedBy,
                SYSUTCDATETIME(), p.CreatedBy, SYSUTCDATETIME()
            FROM svcacct.ChangePlans p JOIN svcacct.ChangePlanPreviews v ON v.PlanId = p.Id AND v.Version = p.CurrentPreviewVersion WHERE p.Id = @planId;
            """, new { id, planId, accountId });
        return id;
    }

    private static async Task<Counts> CountAsync(Setup s, Guid planId) => new(
        await s.Fx.CountAsync("SELECT COUNT(*) FROM svcacct.ChangePlanRecordSubmissions WHERE PlanId = @planId", new { planId }),
        await s.Fx.CountAsync("""
            SELECT COUNT(*) FROM svcacct.ChangePlanRecordSubmissionEvents e JOIN svcacct.ChangePlanRecordSubmissions r ON r.Id = e.SubmissionId WHERE r.PlanId = @planId
            """, new { planId }),
        await s.Fx.CountAsync("""
            SELECT COUNT(*) FROM svcacct.History h JOIN svcacct.ChangePlanRecordSubmissions r ON r.Id = h.EntityId
            WHERE h.EntityType = 'ChangePlanRecord' AND r.PlanId = @planId
            """, new { planId }),
        await s.Fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Action LIKE 'ServiceAccount.OperationalRecord%' AND DetailsJson LIKE @id",
            new { id = "%" + planId.ToString("D") + "%" }));

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }
}
