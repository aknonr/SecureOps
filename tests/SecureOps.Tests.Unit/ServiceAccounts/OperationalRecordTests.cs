using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ItsmRecords;
using SecureOps.Infrastructure.ItsmRequests;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// OR-2 (ADR-0030, PROPOSED): pure rules, the shared insert rule, the Fake and Disabled creators, startup validation, the
/// requester mapping gate (anything but Matched with a location stops) and the submission flow over an in-memory store
/// (idempotent replay, single dispatch, OutcomeUnknown on any doubt, no automatic retry). Synthetic values only.
/// </summary>
public sealed class OperationalRecordTests
{
    private static readonly Guid _plan = Guid.Parse("00000000-0000-0000-0000-00000000a001");
    private static readonly Guid _account = Guid.Parse("00000000-0000-0000-0000-00000000b001");

    [Theory]
    [InlineData(OperationalRecordState.Sending, true, true, true)]
    [InlineData(OperationalRecordState.OutcomeUnknown, true, true, true)]
    [InlineData(OperationalRecordState.PartiallyCreated, true, true, false)]
    [InlineData(OperationalRecordState.Created, true, false, false)]
    [InlineData(OperationalRecordState.Linked, true, false, false)]
    [InlineData(OperationalRecordState.NotSent, false, false, false)]
    [InlineData(OperationalRecordState.ConfirmedNotCreated, false, false, false)]
    public void States_BlockReconcileAndConfirm_AsAdr0030Says(OperationalRecordState state, bool blocks, bool reconcile, bool notCreated)
    {
        OperationalRecordRules.BlocksNewSubmission(state).Should().Be(blocks);
        OperationalRecordRules.NeedsReconciliation(state).Should().Be(reconcile);
        OperationalRecordRules.CanLink(state).Should().Be(reconcile);
        OperationalRecordRules.CanConfirmNotCreated(state).Should().Be(notCreated, "a PartiallyCreated record exists, so it can only be linked");
    }

    [Fact]
    public void Sending_IsTreatedAsInFlight_UntilTheDispatchBoundAndAMarginPass()
    {
        DateTimeOffset created = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
        var bound = TimeSpan.FromSeconds(60);
        OperationalRecordRules.MayStillBeInFlight(OperationalRecordState.Sending, created, created.AddSeconds(119), bound).Should().BeTrue();
        OperationalRecordRules.MayStillBeInFlight(OperationalRecordState.Sending, created, created.AddSeconds(121), bound).Should().BeFalse();
        OperationalRecordRules.MayStillBeInFlight(OperationalRecordState.OutcomeUnknown, created, created, bound).Should().BeFalse();
    }

    [Fact]
    public void Fingerprint_IsCanonical_IndependentOfServerOrder_AndChangesWithEveryField()
    {
        OperationalRecordFingerprintInput input = Input();
        string baseline = OperationalRecordRules.Fingerprint(input);
        baseline.Should().MatchRegex("^[0-9a-f]{64}$");
        OperationalRecordRules.Fingerprint(input with { Servers = ["SRV-SYN-02", "SRV-SYN-01"] }).Should().Be(baseline);

        foreach (OperationalRecordFingerprintInput changed in new[]
        {
            input with { PlanId = Guid.NewGuid() }, input with { AccountId = Guid.NewGuid() }, input with { PreviewVersion = 2 },
            input with { PreviewSha256 = new string('c', 64) }, input with { TargetGmsaName = "gmsaSynOther" }, input with { Servers = ["SRV-SYN-01"] },
            input with { Provider = "Other" }, input with { RequesterUserId = "920004" }, input with { RequesterHomeLocationId = "9702" },
            input with { Environment = "Prod" }, input with { ShortDescription = input.ShortDescription + " " }, input with { Description = input.Description + "\n" }
        })
        {
            OperationalRecordRules.Fingerprint(changed).Should().NotBe(baseline);
        }

        // Length prefixes: moving text between neighbouring fields never produces the same bytes.
        OperationalRecordRules.Fingerprint(input with { ShortDescription = "ab", Description = "c" })
            .Should().NotBe(OperationalRecordRules.Fingerprint(input with { ShortDescription = "a", Description = "bc" }));
    }

    [Fact]
    public void Template_CarriesTheGmsaNameServersAndNoSecret_AndTheReferenceIsTheLastLine()
    {
        string body = OperationalRecordRules.DefaultDescription("gmsaSyn01", ["SRV-SYN-01", "SRV-SYN-02"]);
        body.Should().Contain("gMSA adı: gmsaSyn01").And.Contain("- SRV-SYN-01").And.Contain("- SRV-SYN-02");
        OperationalRecordRules.DefaultShortDescription("gmsaSyn01").Should().Be("gmsa service account talebi(gmsaSyn01)");
        string reference = OperationalRecordRules.PlanReference(_plan, _account);
        OperationalRecordRules.ComposeDescription(body, reference).Split('\n')[^1].Should().Be(reference);
        OperationalRecordRules.DefaultDescription("gmsaSyn01", []).Should().Contain("sunucu yok");
    }

    [Theory]
    [InlineData("kısa", "gövde", null)]
    [InlineData("", "gövde", "shortDescription")]
    [InlineData("iki\nsatır", "gövde", "shortDescription")]
    [InlineData("sekme\tvar", "gövde", "shortDescription")]
    [InlineData("kısa", "", "description")]
    [InlineData("kısa", "NUL\0var", "description")]
    [InlineData("kısa", "yön‮değişimi", "description")]
    [InlineData("kısa", "satır\nsatır", null)]
    public void TextError_AllowsLineBreaksOnly_AndRequiresBoth(string title, string body, string? expected) =>
        OperationalRecordRules.TextError(title, body, "ref", 200).Should().Be(expected);

    [Fact]
    public void TextError_CountsTheReferenceLine_AndTheConfiguredShortLimit()
    {
        string reference = OperationalRecordRules.PlanReference(_plan, _account);
        string fits = new('a', OperationalRecordRules.MaxDescription - reference.Length - 2);
        OperationalRecordRules.TextError("kısa", fits, reference, 200).Should().BeNull();
        OperationalRecordRules.TextError("kısa", fits + "a", reference, 200).Should().Be("description");
        OperationalRecordRules.TextError(new string('k', 11), "gövde", reference, 10).Should().Be("shortDescription");
    }

    [Fact]
    public void NormalizeText_MakesLineEndingsLf_AndTrimsTheEnds() =>
        OperationalRecordRules.NormalizeText("  a\r\nb\rc  ").Should().Be("a\nb\nc");

    [Fact]
    public void ReasonLabels_CoverEveryStoredCode()
    {
        foreach (ItsmRequestCreateReason reason in Enum.GetValues<ItsmRequestCreateReason>().Where(r => r != ItsmRequestCreateReason.None))
        {
            OperationalRecordRules.ReasonLabel(reason.ToString()).Should().NotBe(reason.ToString(), reason.ToString());
        }

        OperationalRecordRules.StateLabel(OperationalRecordState.OutcomeUnknown).Should().StartWith("Belirsiz");
    }

    [Theory]
    [InlineData(true, "900123", true, "900123", ItsmRequestCreateReason.None)]
    [InlineData(true, "900123", false, "900123", ItsmRequestCreateReason.None)]
    [InlineData(true, null, true, null, ItsmRequestCreateReason.InvalidResponse)]
    [InlineData(true, "", false, null, ItsmRequestCreateReason.InvalidResponse)]
    [InlineData(true, "abc", false, null, ItsmRequestCreateReason.InvalidResponse)]
    [InlineData(false, "900123", true, null, ItsmRequestCreateReason.Rejected)]
    [InlineData(null, "900123", false, null, ItsmRequestCreateReason.InvalidResponse)]
    public void InsertRule_DecidesOnSuccessAndInsertedId_IgnoringErrorText(bool? success, string? insertedId, bool errorText, string? expected,
        ItsmRequestCreateReason unproven)
    {
        ItsmInsertResponse response = new(success, insertedId, errorText);
        ItsmInsertRule.InsertedId(response).Should().Be(expected);
        if (expected is null)
        {
            ItsmInsertRule.UnprovenReason(response).Should().Be(unproven);
        }
    }

    [Theory]
    [InlineData("", ItsmRequestCreateStatus.Created, ItsmRequestCreateReason.None)]
    [InlineData("[fake:errortext]", ItsmRequestCreateStatus.Created, ItsmRequestCreateReason.None)]
    [InlineData("[fake:partial]", ItsmRequestCreateStatus.PartiallyCreated, ItsmRequestCreateReason.NumberNotReadBack)]
    [InlineData("[fake:unknown]", ItsmRequestCreateStatus.OutcomeUnknown, ItsmRequestCreateReason.Rejected)]
    [InlineData("[fake:noid]", ItsmRequestCreateStatus.OutcomeUnknown, ItsmRequestCreateReason.InvalidResponse)]
    [InlineData("[fake:notsent]", ItsmRequestCreateStatus.NotSent, ItsmRequestCreateReason.AuthenticationFailed)]
    public async Task FakeCreator_IsDeterministic_PerMarker(string marker, ItsmRequestCreateStatus status, ItsmRequestCreateReason reason)
    {
        FakeItsmRequestCreator creator = new();
        ItsmRequestDraft draft = Draft("gövde " + marker);
        ItsmRequestCreateOutcome first = await creator.CreateAsync(draft, CancellationToken.None);
        ItsmRequestCreateOutcome second = await creator.CreateAsync(draft, CancellationToken.None);

        first.Should().Be(second, "same draft, same synthetic answer");
        (first.Status, first.Reason, first.Provider).Should().Be((status, reason, "Fake"));
        first.SourceId.Should().Be(status is ItsmRequestCreateStatus.Created or ItsmRequestCreateStatus.PartiallyCreated ? FakeItsmRequestCreator.SourceId : null);
        if (status == ItsmRequestCreateStatus.Created)
        {
            first.RecordNumber.Should().MatchRegex("^OR-[1-8][0-9]{5}$");
            ItsmRecordNumber.Normalize(ItsmRecordKind.Or, first.RecordNumber).Should().Be(first.RecordNumber, "the lookup accepts the synthetic number");
        }
        else
        {
            first.RecordNumber.Should().BeNull();
        }
    }

    [Fact]
    public async Task FakeCreator_TimesOut_OnMarker_AndRefusesWithoutLocation_AndDisabledNeverCreates()
    {
        FakeItsmRequestCreator fake = new();
        await fake.Invoking(f => f.CreateAsync(Draft("[fake:timeout]"), CancellationToken.None)).Should().ThrowAsync<TimeoutException>();
        (await fake.CreateAsync(Draft("gövde") with { RequesterHomeLocationId = null }, CancellationToken.None))
            .Should().Be(ItsmRequestCreateOutcome.NotSent(ItsmRequestCreateReason.RequesterLocationMissing, "Fake"));
        (await fake.CreateAsync(Draft("gövde"), CancellationToken.None)).RecordNumber
            .Should().NotBe((await fake.CreateAsync(Draft("başka gövde"), CancellationToken.None)).RecordNumber);

        DisabledItsmRequestCreator disabled = new();
        (disabled.IsEnabled, disabled.IsSynthetic, fake.IsEnabled, fake.IsSynthetic).Should().Be((false, false, true, true));
        (await disabled.CreateAsync(Draft("gövde"), CancellationToken.None))
            .Should().Be(ItsmRequestCreateOutcome.NotSent(ItsmRequestCreateReason.NotConfigured, "Disabled"));
    }

    [Theory]
    [InlineData(null, "Production", "Disabled")]
    [InlineData("disabled", "Production", "Disabled")]
    [InlineData("Fake", "Test", "Fake")]
    [InlineData("fake", "Development", "Fake")]
    [InlineData("Fake", "Demo", "Fake")]
    public void Registration_DefaultsToDisabled_AndAllowsFakeOnlyInSyntheticEnvironments(string? provider, string environment, string expected)
    {
        IConfiguration configuration = Configuration(provider);
        ItsmRequestCreationRegistration.Validate(configuration, environment).Should().Be(expected);
        ServiceCollection services = new();
        services.AddItsmRequestCreation(configuration, environment);
        services.BuildServiceProvider().GetRequiredService<IItsmRequestCreator>().Provider.Should().Be(expected);
    }

    [Theory]
    [InlineData("Fake", "Production")]
    [InlineData("Fake", "Pilot")]
    [InlineData("Fake", null)]
    [InlineData("TuruncuHat", "Test")]
    [InlineData("TuruncuHat", "Production")]
    [InlineData("Jira", "Test")]
    public void Registration_FailsClosed_OnFakeOutsideSyntheticEnvironments_OrAnUnimplementedProvider(string provider, string? environment) =>
        FluentActions.Invoking(() => ItsmRequestCreationRegistration.Validate(Configuration(provider), environment))
            .Should().Throw<InvalidOperationException>().WithMessage("*ItsmRequestCreation:Provider*");

    [Theory]
    [InlineData("4")]
    [InlineData("301")]
    public void Registration_RefusesAnUnboundedDispatch(string seconds) =>
        FluentActions.Invoking(() => ItsmRequestCreationRegistration.Validate(Configuration(null, seconds), "Test"))
            .Should().Throw<InvalidOperationException>().WithMessage("*DispatchTimeoutSeconds*");

    [Theory]
    [InlineData(null, "synthetic.user", ItsmUserMatchStatus.Matched, null)]
    [InlineData("990001", "synthetic.user", ItsmUserMatchStatus.Matched, null)]
    [InlineData("990004", "synthetic.user", ItsmUserMatchStatus.Ambiguous, "requesterNotMatched")]
    [InlineData(null, "synthetic.dup", ItsmUserMatchStatus.Ambiguous, "requesterNotMatched")]
    [InlineData(null, "synthetic.down", ItsmUserMatchStatus.Unknown, "requesterNotMatched")]
    [InlineData(null, "someone.else", ItsmUserMatchStatus.NotMatched, "requesterNotMatched")]
    [InlineData("990003", "synthetic.user", ItsmUserMatchStatus.Unknown, "requesterNotMatched")]
    [InlineData(null, "DOMAIN\\synthetic.user", ItsmUserMatchStatus.Unknown, "requesterNotMatched")]
    [InlineData(null, null, ItsmUserMatchStatus.Unknown, "requesterNotMatched")]
    public async Task Requester_OnlyMatchedMaySend_EverythingElseStops(string? registration, string? userName, ItsmUserMatchStatus status, string? blocked)
    {
        OperationalRecordRequester requester = await OperationalRecordRequester.ResolveAsync(new FakeItsmUserMapping(), registration, userName, CancellationToken.None);

        (requester.Status, requester.BlockedReason, requester.CanSend).Should().Be((status, blocked, blocked is null));
        if (blocked is not null)
        {
            requester.UserId.Should().BeNull("no ITSM user is carried forward unless the mapping is Matched");
            requester.Label.Should().Contain("OR açılamaz");
        }
    }

    [Fact]
    public async Task Requester_DisabledMapping_AndAMatchWithoutLocation_Stop()
    {
        OperationalRecordRequester disabled = await OperationalRecordRequester.ResolveAsync(new DisabledItsmUserMapping(), "990001", "synthetic.user", CancellationToken.None);
        (disabled.CanSend, disabled.BlockedReason).Should().Be((false, "requesterNotMatched"));
        disabled.Label.Should().Contain("yapılandırılmamış");

        OperationalRecordRequester noLocation = new(ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None, "920001", null);
        (noLocation.CanSend, noLocation.BlockedReason).Should().Be((false, "requesterLocationMissing"));
    }

    [Fact]
    public async Task Flow_CreatesOnce_AndStoresTheProvenNumber()
    {
        MemoryStore store = new();
        SpyCreator creator = new(new FakeItsmRequestCreator());
        SaResult<(OperationalRecordSubmissionRecord Submission, bool Replayed)> result = await Flow(store, creator).SubmitAsync(Reservation(), Draft("gövde"), Caller(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (result.Value.Submission.State, result.Value.Replayed, result.Value.Submission.ExternalId).Should().Be((OperationalRecordState.Created, false, "900001"));
        result.Value.Submission.ExternalNumber.Should().MatchRegex("^OR-[1-8][0-9]{5}$");
        (store.Reservations, creator.Calls).Should().Be((1, 1));
    }

    [Fact]
    public async Task Flow_SameKeySameContent_IsAReplay_WithoutASecondCall()
    {
        MemoryStore store = new();
        SpyCreator creator = new(new FakeItsmRequestCreator());
        OperationalRecordFlow flow = Flow(store, creator);
        await flow.SubmitAsync(Reservation(), Draft("gövde"), Caller(), CancellationToken.None);

        SaResult<(OperationalRecordSubmissionRecord Submission, bool Replayed)> again = await flow.SubmitAsync(Reservation(), Draft("gövde"), Caller(), CancellationToken.None);

        again.Value.Replayed.Should().BeTrue();
        (store.Reservations, creator.Calls).Should().Be((1, 1), "a replay never reaches the ITSM again");
    }

    [Fact]
    public async Task Flow_SameKeyOtherContent_Is409_AndSendsNothing()
    {
        MemoryStore store = new();
        SpyCreator creator = new(new FakeItsmRequestCreator());
        OperationalRecordFlow flow = Flow(store, creator);
        await flow.SubmitAsync(Reservation(), Draft("gövde"), Caller(), CancellationToken.None);

        SaResult<(OperationalRecordSubmissionRecord, bool)> reused = await flow.SubmitAsync(Reservation() with { Fingerprint = new string('d', 64) }, Draft("başka"),
            Caller(), CancellationToken.None);

        (reused.ErrorCode, reused.Field).Should().Be((SaErrors.IdempotencyKeyReused, "idempotencyKey"));
        (store.Reservations, creator.Calls).Should().Be((1, 1));
    }

    [Theory]
    [InlineData("[fake:timeout]", OperationalRecordState.OutcomeUnknown, "Timeout")]
    [InlineData("[fake:unknown]", OperationalRecordState.OutcomeUnknown, "Rejected")]
    [InlineData("[fake:noid]", OperationalRecordState.OutcomeUnknown, "InvalidResponse")]
    [InlineData("[fake:partial]", OperationalRecordState.PartiallyCreated, "NumberNotReadBack")]
    [InlineData("[fake:notsent]", OperationalRecordState.NotSent, "AuthenticationFailed")]
    [InlineData("[fake:errortext]", OperationalRecordState.Created, null)]
    public async Task Flow_RecordsEveryOutcomeHonestly_AndNeverRetries(string marker, OperationalRecordState state, string? reason)
    {
        MemoryStore store = new();
        SpyCreator creator = new(new FakeItsmRequestCreator());

        SaResult<(OperationalRecordSubmissionRecord Submission, bool Replayed)> result = await Flow(store, creator).SubmitAsync(Reservation(), Draft("gövde " + marker),
            Caller(), CancellationToken.None);

        (result.Value.Submission.State, result.Value.Submission.ReasonCode).Should().Be((state, reason));
        creator.Calls.Should().Be(1, "no automatic retry after an unclear or refused answer");
        if (state == OperationalRecordState.OutcomeUnknown)
        {
            result.Value.Submission.ExternalNumber.Should().BeNull("an unknown outcome never invents a number");
        }
    }

    [Fact]
    public async Task Flow_AnyThrownDispatchFailure_IsOutcomeUnknown_NotALostSendingRow()
    {
        MemoryStore store = new();
        SpyCreator creator = new(new ThrowingCreator(new InvalidOperationException("synthetic")));

        SaResult<(OperationalRecordSubmissionRecord Submission, bool Replayed)> result = await Flow(store, creator).SubmitAsync(Reservation(), Draft("gövde"), Caller(),
            CancellationToken.None);

        (result.Value.Submission.State, result.Value.Submission.ReasonCode).Should().Be((OperationalRecordState.OutcomeUnknown, "DispatchInterrupted"));
        store.Submission!.Events.Select(e => e.Event).Should().Equal("Started", "OutcomeUnknown");
    }

    [Fact]
    public async Task Flow_WhenTheOutcomeCannotBeStored_TheRowStaysSending_ForReconciliation()
    {
        MemoryStore store = new() { FailOutcome = true };

        SaResult<(OperationalRecordSubmissionRecord Submission, bool Replayed)> result = await Flow(store, new SpyCreator(new FakeItsmRequestCreator()))
            .SubmitAsync(Reservation(), Draft("gövde"), Caller(), CancellationToken.None);

        result.Value.Submission.State.Should().Be(OperationalRecordState.Sending);
        OperationalRecordRules.NeedsReconciliation(result.Value.Submission.State).Should().BeTrue();
    }

    [Fact]
    public async Task Flow_ARefusedReservation_SendsNothing()
    {
        MemoryStore store = new() { Refusal = SaResult<Guid>.Fail(SaErrors.ChangePlanState, "submissionExists") };
        SpyCreator creator = new(new FakeItsmRequestCreator());

        SaResult<(OperationalRecordSubmissionRecord, bool)> result = await Flow(store, creator).SubmitAsync(Reservation(), Draft("gövde"), Caller(), CancellationToken.None);

        (result.ErrorCode, result.Field, creator.Calls).Should().Be((SaErrors.ChangePlanState, "submissionExists", 0));
    }

    [Fact]
    public async Task Flow_ARaceOnTheSameKey_IsAnsweredAsAReplay()
    {
        MemoryStore store = new();
        SpyCreator creator = new(new FakeItsmRequestCreator());
        await Flow(store, creator).SubmitAsync(Reservation(), Draft("gövde"), Caller(), CancellationToken.None);
        store.HideKeyOnce = true;
        store.Refusal = SaResult<Guid>.Fail(SaErrors.ChangePlanState, SqlServiceAccountRepository.SubmissionKeyExists);

        SaResult<(OperationalRecordSubmissionRecord Submission, bool Replayed)> raced = await Flow(store, creator).SubmitAsync(Reservation(), Draft("gövde"), Caller(),
            CancellationToken.None);

        (raced.IsSuccess, raced.Value.Replayed, creator.Calls).Should().Be((true, true, 1));
    }

    [Fact]
    public async Task Flow_TheBrowserLeavingAfterTheReservation_DoesNotCancelTheDispatch()
    {
        MemoryStore store = new();
        SpyCreator creator = new(new FakeItsmRequestCreator());
        using CancellationTokenSource browser = new();
        store.OnReserve = browser.Cancel;

        SaResult<(OperationalRecordSubmissionRecord Submission, bool Replayed)> result = await Flow(store, creator).SubmitAsync(Reservation(), Draft("gövde"), Caller(),
            browser.Token);

        result.Value.Submission.State.Should().Be(OperationalRecordState.Created, "the dispatch uses its own bound, not the request's token");
        creator.TokenWasCancelled.Should().BeFalse();
    }

    [Fact]
    public void Write_ACreatedAnswerWithoutItsNumber_IsOnlyPartiallyCreated() =>
        OperationalRecordFlow.Write(new ItsmRequestCreateOutcome(ItsmRequestCreateStatus.Created, ItsmRequestCreateReason.None, "900001", null, "Fake"))
            .Should().Be(new OperationalRecordOutcomeWrite(OperationalRecordState.PartiallyCreated, "NumberNotReadBack", "900001", null));

    private static OperationalRecordFlow Flow(MemoryStore store, IItsmRequestCreator creator) =>
        new(store, creator, TimeSpan.FromSeconds(5), NullLogger.Instance);

    private static ChangePlanCaller Caller() => new(new SaActor(Guid.Parse("00000000-0000-0000-0000-00000000c001"), "synthetic-correlation"), true, false);

    private static OperationalRecordReservation Reservation() => new(_plan, _account, Guid.Parse("00000000-0000-0000-0000-00000000d001"), 1,
        Guid.Parse("00000000-0000-0000-0000-00000000e001"), new string('a', 64), "Fake", "Test", "920001", "gmsa service account talebi(gmsaSyn01)", "gövde");

    private static ItsmRequestDraft Draft(string description) =>
        new("920001", "9701", "gmsa service account talebi(gmsaSyn01)", description, "Test", OperationalRecordRules.PlanReference(_plan, _account));

    private static OperationalRecordFingerprintInput Input() => new(_plan, _account, 1, new string('b', 64), "gmsaSyn01", ["SRV-SYN-01", "SRV-SYN-02"], "Fake",
        "920001", "9701", "Test", "gmsa service account talebi(gmsaSyn01)", "gövde\n\nref");

    private static IConfiguration Configuration(string? provider, string? seconds = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ItsmRequestCreation:Provider"] = provider,
            ["ItsmRequestCreation:DispatchTimeoutSeconds"] = seconds
        }.Where(p => p.Value is not null)).Build();

    private sealed class SpyCreator(IItsmRequestCreator inner) : IItsmRequestCreator
    {
        public int Calls { get; private set; }

        public bool TokenWasCancelled { get; private set; }

        public string Provider => inner.Provider;

        public bool IsEnabled => inner.IsEnabled;

        public bool IsSynthetic => inner.IsSynthetic;

        public Task<ItsmRequestCreateOutcome> CreateAsync(ItsmRequestDraft draft, CancellationToken cancellationToken)
        {
            Calls++;
            TokenWasCancelled |= cancellationToken.IsCancellationRequested;
            return inner.CreateAsync(draft, cancellationToken);
        }
    }

    private sealed class ThrowingCreator(Exception exception) : IItsmRequestCreator
    {
        public string Provider => "Fake";

        public bool IsEnabled => true;

        public bool IsSynthetic => true;

        public Task<ItsmRequestCreateOutcome> CreateAsync(ItsmRequestDraft draft, CancellationToken cancellationToken) => throw exception;
    }

    /// <summary>One plan account's submissions in memory, with the same rules the SQL store enforces for the flow.</summary>
    private sealed class MemoryStore : IOperationalRecordStore
    {
        public OperationalRecordSubmissionRecord? Submission { get; private set; }

        public int Reservations { get; private set; }

        public bool FailOutcome { get; init; }

        public bool HideKeyOnce { get; set; }

        public SaResult<Guid>? Refusal { get; set; }

        public Action? OnReserve { get; set; }

        private Guid _key;

        public Task<OperationalRecordSubmissionRecord?> OperationalRecordByKeyAsync(Guid accountId, Guid key, CancellationToken cancellationToken)
        {
            if (HideKeyOnce)
            {
                HideKeyOnce = false;
                return Task.FromResult<OperationalRecordSubmissionRecord?>(null);
            }

            return Task.FromResult(Submission is not null && Submission.AccountId == accountId && _key == key ? Submission : null);
        }

        public Task<SaResult<Guid>> ReserveOperationalRecordAsync(OperationalRecordReservation reservation, ChangePlanCaller caller, CancellationToken cancellationToken)
        {
            if (Refusal is { } refusal)
            {
                return Task.FromResult(refusal);
            }

            Reservations++;
            _key = reservation.IdempotencyKey;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Submission = new OperationalRecordSubmissionRecord(Guid.NewGuid(), reservation.PlanId, reservation.AccountId, OperationalRecordState.Sending, null,
                reservation.Provider, null, null, null, reservation.Environment, reservation.ShortDescription, reservation.Description, reservation.Fingerprint,
                "Sentetik Kullanıcı", now, now, [Event("Started", null, OperationalRecordState.Sending, null)]);
            OnReserve?.Invoke();
            return Task.FromResult<SaResult<Guid>>(Submission.Id);
        }

        public Task<bool> RecordOperationalRecordOutcomeAsync(Guid submissionId, OperationalRecordOutcomeWrite outcome, SaActor actor, CancellationToken cancellationToken)
        {
            if (FailOutcome || Submission is not { State: OperationalRecordState.Sending } current || current.Id != submissionId)
            {
                return Task.FromResult(false);
            }

            Submission = current with
            {
                State = outcome.State,
                ReasonCode = outcome.ReasonCode,
                ExternalId = outcome.ExternalId,
                ExternalNumber = outcome.ExternalNumber,
                Events = [.. current.Events, Event(outcome.State.ToString(), OperationalRecordState.Sending, outcome.State, outcome.ReasonCode)]
            };
            return Task.FromResult(true);
        }

        private static OperationalRecordEventView Event(string name, OperationalRecordState? from, OperationalRecordState to, string? reason) =>
            new(name, from?.ToString(), to.ToString(), reason, null, null, "Sentetik Kullanıcı", DateTimeOffset.UtcNow);
    }
}
