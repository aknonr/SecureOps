using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.InUse;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Unit.InUse;

public sealed partial class InUseTests
{
    [Fact]
    public async Task Overview_CountsOrsNotServers_UnknownDatesAndStatesAreNotOpen()
    {
        var f = new Fixture();
        InUseRecord r = await f.ImportAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        InUseSource source = r.Source with { Creation = new(now.AddDays(-5).ToString("O"), "Synthetic parent OR creation"), Lifecycle = new("Open", "Synthetic parent lifecycle") };
        InUseRecord known = r with { Source = source };
        InUseOverview overview = InUseProgress.Summarize([known, r with { Id = Guid.NewGuid() }], InUseRefreshState.Empty, now);
        overview.Open.Should().Be(1);
        overview.Unknown.Should().Be(1);
        overview.AwaitingAnswers.Should().Be(2);
        overview.Oldest.Should().ContainSingle().Which.Id.Should().Be(r.Id);
        InUseProgress.Created(source with { Creation = new("2026-01-02", "No timezone") }, now).Should().BeNull();
        InUseProgress.Created(source with { Creation = new(now.AddDays(1).ToString("O"), "Future") }, now).Should().BeNull();
        r.FirstSeenAt.Should().NotBeNull();
        System.Text.Json.JsonSerializer.Serialize(r.Source).Should().NotContain("\"Creation\"").And.NotContain("\"Lifecycle\"");
        await f.Service.RefreshAsync(_principal, _context, new(Guid.NewGuid()), _token);
        InUseRecord refreshed = (await f.Repository.GetAsync(r.Id, _token))!;
        refreshed.FirstSeenAt.Should().Be(r.FirstSeenAt);
        refreshed.SourceVersion.Should().Be(r.SourceVersion);
        f.Source.ClearReceivedCalls();
        (await f.Service.OverviewAsync(_principal, _context, _token)).Value!.Total.Should().Be(2);
        await f.Source.DidNotReceiveWithAnyArgs().DiscoverAsync(TestContext.Current.CancellationToken);
        var denied = new Fixture("InUseReviewer");
        (await denied.Service.OverviewAsync(_principal, _context, _token)).Error.Should().Be("AccessDenied");
    }

    [Fact]
    public async Task Completion_RequiresReadyExactArchive_PersistsBlockedActorAndReplaysWithoutWrites()
    {
        var archive = new InUseReportArchive(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["InUseReports:Directory"] = Path.Combine(Path.GetTempPath(), "inuse-intent-" + Guid.NewGuid()) }).Build());
        var f = new Fixture(archive: archive);
        InUseRecord r = await f.ImportAsync();
        (await f.Service.ConfirmAsync(_principal, _context, r.Id, new(r.Version, Guid.NewGuid(), new string('A', 64)), _token)).Error.Should().Be("InUseIncomplete");
        InUseAnswer[] answers = r.Source.Servers.SelectMany(s => InUseChecks.OperatorCodes.Select(c => new InUseAnswer(s.Id, c, "No", ""))).ToArray();
        r = (await f.Service.SaveDraftAsync(_principal, _context, r.Id, new(r.Version, r.SourceVersion, answers, ""), _token)).Value!;
        InUseReport report = (await f.Service.ExportAsync(_principal, _context, r.Id, new(r.Version, true), _token)).Value!;
        var request = new ConfirmInUseRequest(r.Version, Guid.NewGuid(), report.Sha256);
        f.Audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(_ => throw new IOException("Synthetic audit failure"));
        (await f.Service.ConfirmAsync(_principal, _context, r.Id, request with { CommandId = Guid.NewGuid() }, _token)).Error.Should().Be("PersistenceUnavailable");
        (await f.Repository.GetAsync(r.Id, _token))!.Completion.Should().BeNull();
        f.Audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        f.Audit.ClearReceivedCalls();
        (await f.Service.ConfirmAsync(_principal, _context, r.Id, request with { ReportSha256 = new string('B', 64) }, _token)).Error.Should().Be("InUseConflict");
        f.Source.ClearReceivedCalls();
        InUseResult<InUseRecord>[] results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => f.Service.ConfirmAsync(_principal, _context, r.Id, request, _token)));
        InUseRecord saved = (await f.Repository.GetAsync(r.Id, _token))!;
        saved.Version.Should().Be(r.Version + 1);
        saved.AssigneeId.Should().BeNull();
        saved.Completion!.Stage.Should().Be("Blocked");
        saved.Completion.ActorId.Should().Be(f.User.Id);
        saved.Completion.ReportSha256.Should().Be(report.Sha256);
        (await f.Service.ConfirmAsync(_principal, _context, r.Id, request, _token)).Value!.Version.Should().Be(saved.Version);
        await f.Source.DidNotReceiveWithAnyArgs().DiscoverAsync(TestContext.Current.CancellationToken);
        await f.Audit.Received(1).WriteAsync(Arg.Is<AuditEvent>(a => a.Action == "InUseCompletionIntentBlocked"), Arg.Any<CancellationToken>());
        f.Audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(_ => throw new IOException("Synthetic audit failure"));
        (await f.Service.OverviewAsync(_principal, _context, _token)).Error.Should().Be("PersistenceUnavailable");
    }

    [Theory]
    [InlineData("UploadPending", "UploadFailed")]
    [InlineData("CompletionPending", "CompletionFailed")]
    public void Completion_FailureAndUncertaintyNeverAutoRetry(string stage, string failed)
    {
        var state = new InUseCompletion(Guid.NewGuid(), Guid.NewGuid(), 2, 1, "synthetic", DateTimeOffset.UtcNow, stage);
        InUseCompletionTransitions.Apply(state, "Failed").Stage.Should().Be(failed);
        InUseCompletion unknown = InUseCompletionTransitions.Apply(state, "Unknown");
        unknown.Stage.Should().Be("ReconciliationRequired");
        Action retry = () => InUseCompletionTransitions.Apply(unknown, "Succeeded", "synthetic");
        retry.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Completion_ResultJournal_PreservesUncertaintyAndRollsBackWithAudit()
    {
        var f = new Fixture();
        InUseRecord r = await f.ImportAsync();
        var intent = new InUseCompletion(Guid.NewGuid(), f.User.Id, r.Version, r.SourceVersion, new string('A', 64), DateTimeOffset.UtcNow, "UploadPending");
        // Only the fixture can start a pending write. No production path leaves Blocked.
        await f.Repository.SaveAsync(r with { Version = r.Version + 1, Completion = intent }, r.Version, new AuditEvent { Actor = "synthetic", Action = "Fixture" }, _token);
        r = (await f.Repository.GetAsync(r.Id, _token))!;
        f.Audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(_ => throw new IOException("Synthetic audit failure"));
        (await f.Service.RecordCompletionOutcomeAsync(_principal, _context, r.Id, r.Version, intent.CommandId, "Unknown", null, _token)).Error.Should().Be("PersistenceUnavailable");
        (await f.Repository.GetAsync(r.Id, _token))!.Completion!.Stage.Should().Be("UploadPending");
        f.Audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        InUseRecord saved = (await f.Service.RecordCompletionOutcomeAsync(_principal, _context, r.Id, r.Version, intent.CommandId, "Unknown", null, _token)).Value!;
        saved.Completion!.Stage.Should().Be("ReconciliationRequired");
        (await f.Service.RecordCompletionOutcomeAsync(_principal, _context, r.Id, r.Version, intent.CommandId, "Succeeded", "synthetic", _token)).Error.Should().Be("InUseConflict");
        (await f.Service.RecordCompletionOutcomeAsync(_principal, _context, r.Id, saved.Version, intent.CommandId, "Succeeded", "synthetic", _token)).Error.Should().Be("InUseConflict");
        (await f.Repository.GetAsync(r.Id, _token))!.Completion.Should().Be(saved.Completion);
    }

    [Fact]
    public void Completion_UniqueTaskAndAuthoritativeOrReadAreSeparate()
    {
        var state = new InUseCompletion(Guid.NewGuid(), Guid.NewGuid(), 2, 1, "synthetic", DateTimeOffset.UtcNow, "UploadPending");
        state = InUseCompletionTransitions.Apply(state, "Succeeded", "attachment-1");
        InUseCompletionTransitions.Apply(state, "MissingOrAmbiguous").Stage.Should().Be("TaskLookupBlocked");
        state = InUseCompletionTransitions.Apply(state, "UniqueAuthorized", "task-1");
        state = InUseCompletionTransitions.Apply(state, "Succeeded");
        state.Stage.Should().Be("VerificationPending");
        state.FinalOrState.Should().BeNull();
        InUseCompletionTransitions.Apply(state, "StillOpen", "InProgress").Stage.Should().Be("TaskCompletedOrOpen");
        InUseCompletionTransitions.Apply(state, "Closed").Stage.Should().Be("ClosedVerified");
        Action blocked = () => InUseCompletionTransitions.Apply(state with { Stage = "Blocked" }, "Succeeded");
        blocked.Should().Throw<InvalidOperationException>();
    }
}
