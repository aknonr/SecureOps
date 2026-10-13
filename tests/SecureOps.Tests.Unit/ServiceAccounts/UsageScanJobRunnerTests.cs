using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// The web usage scan job end to end with the deterministic fake collector and an in-memory store (no SQL, no server): claim, live
/// per-server results, the tool = Jea evidence through the upload parser, cancel, starter re-check, missing collector, collector failure,
/// malformed documents, contract breaches and a lost lease. "Bulunamadı" never turns into "kullanılmıyor".
/// </summary>
public sealed class UsageScanJobRunnerTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid _starter = Guid.Parse("5a5a5a5a-0000-4000-8000-000000000001");
    private static readonly Guid _accountA = Guid.Parse("5a5a5a5a-0000-4000-8000-0000000000a1");
    private static readonly Guid _accountB = Guid.Parse("5a5a5a5a-0000-4000-8000-0000000000b1");

    private static readonly string[] _servers =
        ["syn-app-01", "syn-app-02", "syn-app-03", "syn-unreach-01", "syn-noep-01", "syn-deny-01", "syn-slow-01", "syn-bad-01", "syn-part-01",
         "syn-web-01", "syn-web-02", "syn-web-03", "syn-web-04", "syn-web-05"];

    static UsageScanJobRunnerTests() => UsageScanJobRunner.HeartbeatInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task Fake_EndToEnd_RecordsEveryServer_AndWritesJeaEvidenceLinkedToEachAccount()
    {
        Harness h = new();
        Guid jobId = await h.CreateAsync();

        await h.Runner().RunAsync(jobId, CancellationToken.None);

        UsageScanJobRecord job = (await h.Store.GetAsync(jobId, CancellationToken.None))!;
        job.State.Should().Be(UsageScanJobState.Completed);
        job.Servers.Should().OnlyContain(s => s.State == UsageScanJobServerState.Done);
        Status(job, "syn-unreach-01").Should().Be(UsageCollectionStatus.Unreachable);
        Status(job, "syn-noep-01").Should().Be(UsageCollectionStatus.NoEndpoint);
        Status(job, "syn-deny-01").Should().Be(UsageCollectionStatus.Forbidden);
        Status(job, "syn-slow-01").Should().Be(UsageCollectionStatus.Timeout);
        Status(job, "syn-bad-01").Should().Be(UsageCollectionStatus.Malformed);
        job.Servers.Select(s => s.FinishSeq).Should().OnlyHaveUniqueItems();
        job.Accounts.Should().OnlyContain(a => !a.IsActive && a.LinkOutcome == "Attached", "the accounts are free again and the evidence is linked");

        h.Store.Evidence.Select(e => e.AccountId).Should().BeEquivalentTo([_accountA, _accountB]);
        h.Store.Evidence.Should().OnlyContain(e => e.Actor.UserId == _starter, "the evidence is linked under the starter, scope re-checked in the write");
        UsageScanUpload upload = h.Store.Evidence[0].Upload;
        upload.Parsed.Tool.Should().Be("Jea");
        upload.RunStatement.Should().Contain("SAHTE").And.Contain("hiçbir sunucuya bağlanılmadı");
        ParsedUsageScan reparsed = UsageScanParser.Parse(upload.Content, int.MaxValue, _now.AddMinutes(1));
        reparsed.Servers.Select(s => s.ServerName).Should().Equal(_servers);
        reparsed.Servers.Single(s => s.ServerName == "syn-unreach-01").Result.Should().Be(ScanServerResult.Unreachable);
        reparsed.Servers.Where(s => s.ServerName is "syn-noep-01" or "syn-deny-01" or "syn-slow-01" or "syn-bad-01")
            .Should().OnlyContain(s => s.Result == ScanServerResult.NoResult);
        job.ScanId.Should().Be(InMemoryUsageScanJobStore.ScanIdFor(upload.Sha256));

        IReadOnlyList<UsageScanJobFoundItem> items = await h.Store.ItemsAfterAsync(jobId, 0, 500, CancellationToken.None);
        items.Select(i => i.Item).Should().BeEquivalentTo(reparsed.Items, "the live list shows exactly what the evidence holds");
    }

    [Fact]
    public async Task View_SaysNotFoundOnlyAfterACompleteRead_AndNeverNotUsed()
    {
        Harness h = new();
        Guid jobId = await h.CreateAsync();
        await h.Runner().RunAsync(jobId, CancellationToken.None);
        UsageScanJobRecord job = (await h.Store.GetAsync(jobId, CancellationToken.None))!;

        UsageScanJobView view = UsageScanJobViews.View(job, await h.Store.ItemsAfterAsync(jobId, 0, 500, CancellationToken.None), 0, false, _now,
            _starter, canCancel: true, canRetry: true);

        view.Servers.Where(s => s.Status is not ("Ok" or null)).Should().OnlyContain(s => s.Tile == "NotCovered" && s.TileText.EndsWith("bilgi yok"));
        view.Servers.Single(s => s.ServerName == "syn-part-01").Tile.Should().BeOneOf("Uncertain", "Found");
        view.Servers.Where(s => s.Tile == "NotFound").Should().NotBeEmpty().And.OnlyContain(s => s.DocumentResult == "Success");
        view.Servers.Where(s => s.Tile == "Found").Should().NotBeEmpty().And.OnlyContain(s => s.FormerMatches > 0);
        view.Counts.NotCovered.Should().Be(5);
        view.Counts.Retryable.Should().Be(6, "five without information plus the partial read");
        view.Summary.Should().Contain("5 sunucuya erişilemedi veya okunamadı (bilgi yok)").And.Contain("yeniden deneyebilirsiniz");
        string[] texts = [.. view.Servers.Select(s => s.TileText), view.Summary!, view.NextStep];
        texts.Where(t => t.Contains("kullanılmıyor", StringComparison.OrdinalIgnoreCase))
            .Should().OnlyContain(t => t.Contains("kullanılmıyor demek değildir", StringComparison.OrdinalIgnoreCase));
        view.Permissions.Should().Be(new UsageScanJobPermissions(false, true), "a finished scan cannot be cancelled; its failed servers can be retried");
        UsageScanJobViews.RetryServers(job).Should().Equal("syn-unreach-01", "syn-noep-01", "syn-deny-01", "syn-slow-01", "syn-bad-01", "syn-part-01");
        view.LastSeq.Should().Be(view.NewItems.Count == 0 ? 0 : view.NewItems.Max(i => i.Seq));
    }

    [Fact]
    public async Task Cancel_KeepsFinishedServersAsEvidence_AndMarksTheRestNotRun()
    {
        Harness h = new();
        GatedCollector gated = new(release: 2);
        Guid jobId = await h.CreateAsync();
        Task run = h.Runner(gated).RunAsync(jobId, CancellationToken.None);
        await gated.Reported.Task.WaitAsync(TimeSpan.FromSeconds(10));

        (await h.Store.RequestCancelAsync(jobId, new SaActor(_starter, "test"), _now, CancellationToken.None)).Should().BeTrue();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        UsageScanJobRecord job = (await h.Store.GetAsync(jobId, CancellationToken.None))!;
        job.State.Should().Be(UsageScanJobState.Cancelled);
        job.FailureCode.Should().BeNull();
        job.Servers.Count(s => s.Status == UsageCollectionStatus.Ok).Should().Be(2);
        job.Servers.Count(s => s.Status == UsageCollectionStatus.NotRun).Should().Be(_servers.Length - 2);
        ParsedUsageScan evidence = h.Store.Evidence[0].Upload.Parsed;
        evidence.AnsweredServers.Should().Be(2);
        evidence.Servers.Where(s => s.ScannedAt is null).Should().OnlyContain(s => s.Result == ScanServerResult.NoResult, "not run is no information");
        h.Store.Evidence[0].Upload.RunStatement.Should().Contain("İptal edildi");
    }

    [Fact]
    public async Task QueuedCancel_ClosesAtOnce_AndTheRunnerDoesNothing()
    {
        Harness h = new();
        Guid jobId = await h.CreateAsync();
        await h.Store.RequestCancelAsync(jobId, new SaActor(_starter, "test"), _now, CancellationToken.None);

        await h.Runner().RunAsync(jobId, CancellationToken.None);

        UsageScanJobRecord job = (await h.Store.GetAsync(jobId, CancellationToken.None))!;
        job.State.Should().Be(UsageScanJobState.Cancelled);
        job.Servers.Should().OnlyContain(s => s.Status == UsageCollectionStatus.NotRun);
        h.Store.Evidence.Should().BeEmpty("nothing was read, so nothing is evidence");
    }

    [Fact]
    public async Task RevokedStarter_FailsBeforeAnyServerIsRead()
    {
        Harness h = new() { StarterAllowed = false };
        GatedCollector never = new(release: 0);
        Guid jobId = await h.CreateAsync();

        await h.Runner(never).RunAsync(jobId, CancellationToken.None);

        UsageScanJobRecord job = (await h.Store.GetAsync(jobId, CancellationToken.None))!;
        (job.State, job.FailureCode).Should().Be((UsageScanJobState.Failed, "accessRevoked"));
        never.Called.Should().BeFalse();
        h.Store.Evidence.Should().BeEmpty();
        job.Accounts.Should().OnlyContain(a => !a.IsActive && a.LinkOutcome == "NotLinked");
    }

    [Theory]
    [InlineData("Jea", "Development")]
    [InlineData("Fake", "Production")]
    public async Task NoAllowedCollector_FailsClosed_WithoutContactingAnything(string collector, string environment)
    {
        Harness h = new() { Collector = collector, Environment = environment };
        Guid jobId = await h.CreateAsync(collector);

        await h.Runner().RunAsync(jobId, CancellationToken.None);

        UsageScanJobRecord job = (await h.Store.GetAsync(jobId, CancellationToken.None))!;
        (job.State, job.FailureCode).Should().Be((UsageScanJobState.Failed, "collectorUnavailable"));
        job.Servers.Should().OnlyContain(s => s.Status == UsageCollectionStatus.NotRun);
    }

    [Fact]
    public async Task CollectorException_FailsTheJob_ButKeepsReportedServersAsEvidence()
    {
        Harness h = new();
        Guid jobId = await h.CreateAsync();

        await h.Runner(new ThrowingCollector(after: 3)).RunAsync(jobId, CancellationToken.None);

        UsageScanJobRecord job = (await h.Store.GetAsync(jobId, CancellationToken.None))!;
        (job.State, job.FailureCode).Should().Be((UsageScanJobState.Failed, "collectorFailed"));
        job.Servers.Count(s => s.Status == UsageCollectionStatus.Ok).Should().Be(3);
        h.Store.Evidence.Should().HaveCount(2);
        UsageScanJobRules.FailureLabel(job.FailureCode).Should().Contain("çalışmadı");
    }

    [Fact]
    public async Task OkWithASecretOrForeignDocument_IsRecordedAsMalformed_AndNothingFromItIsStored()
    {
        Harness h = new();
        Guid jobId = await h.CreateAsync();

        await h.Runner(new ScriptedCollector(server => server switch
        {
            "syn-app-01" => new UsageCollectionServerResult(server, UsageCollectionStatus.Ok, Secret(server), TimeSpan.FromMilliseconds(5)),
            "syn-app-02" => new UsageCollectionServerResult(server, UsageCollectionStatus.Ok, null, TimeSpan.Zero),
            _ => new UsageCollectionServerResult(server, UsageCollectionStatus.Unreachable, null, TimeSpan.Zero)
        })).RunAsync(jobId, CancellationToken.None);

        UsageScanJobRecord job = (await h.Store.GetAsync(jobId, CancellationToken.None))!;
        job.Servers.Single(s => s.ServerName == "syn-app-01").Should().Match<UsageScanJobServer>(s =>
            s.Status == UsageCollectionStatus.Malformed && s.RefusalCode == UsageScanFileCodes.SecretField);
        job.Servers.Single(s => s.ServerName == "syn-app-02").Status.Should().Be(UsageCollectionStatus.Malformed);
        (await h.Store.DocumentsAsync(jobId, CancellationToken.None)).Should().BeEmpty();
        h.Store.Evidence[0].Upload.Parsed.AnsweredServers.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(h.Store.Evidence[0].Upload.Content).Should().NotContain("Password");
    }

    [Fact]
    public async Task UnknownAndRepeatedServers_AreIgnored_TheFirstResultStands()
    {
        Harness h = new();
        Guid jobId = await h.CreateAsync();

        await h.Runner(new BreachingCollector()).RunAsync(jobId, CancellationToken.None);

        UsageScanJobRecord job = (await h.Store.GetAsync(jobId, CancellationToken.None))!;
        job.Servers.Single(s => s.ServerName == "syn-app-01").Status.Should().Be(UsageCollectionStatus.Unreachable, "the first report stands");
        job.Servers.Should().NotContain(s => s.ServerName == "syn-other-99");
        job.Servers.Count(s => s.Status == UsageCollectionStatus.NotRun).Should().Be(_servers.Length - 1, "unreported servers are not run, never dropped");
        job.State.Should().Be(UsageScanJobState.Completed);
    }

    [Fact]
    public async Task LostLease_LeavesTheJobToWhoeverClosedIt()
    {
        Harness h = new();
        GatedCollector gated = new(release: 1);
        Guid jobId = await h.CreateAsync();
        Task run = h.Runner(gated).RunAsync(jobId, CancellationToken.None);
        await gated.Reported.Task.WaitAsync(TimeSpan.FromSeconds(10));

        h.Store.LoseLease = true;
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        (await h.Store.GetAsync(jobId, CancellationToken.None))!.State.Should().Be(UsageScanJobState.Running, "this runner no longer owns it");
        h.Store.Evidence.Should().BeEmpty();
        h.Store.LoseLease = false;
        h.Store.Age(jobId, TimeSpan.FromHours(1));
        (await h.Store.ExpireAsync([jobId], _now, _now.AddMinutes(-15), CancellationToken.None)).Should().Be(1);
        (await h.Store.GetAsync(jobId, CancellationToken.None))!.FailureCode.Should().Be("workerLost");
    }

    [Fact]
    public async Task GmsaCheck_StoresExpectedItems_AsAGmsaCheckScan()
    {
        Harness h = new();
        Guid jobId = await h.CreateAsync(expected: "SYN\\gmsa-syn-a$");

        await h.Runner().RunAsync(jobId, CancellationToken.None);

        ParsedUsageScan evidence = h.Store.Evidence[0].Upload.Parsed;
        evidence.Purpose.Should().Be("GmsaCheck");
        evidence.ExpectedAccount.Should().Be("SYN\\gmsa-syn-a$");
    }

    [Fact]
    public void Availability_IsClosedUntilEverythingIsConfigured()
    {
        Availability("None", "Development", true).Reason.Should().Be("collectorNotConfigured");
        Availability("Fake", "Production", true).Reason.Should().Be("fakeNotAllowed");
        Availability("Fake", "Demo", false).Reason.Should().Be("queueNotConfigured");
        Availability("Jea", "Production", true).Reason.Should().BeNull();
        Availability("Fake", "Test", true).Available.Should().BeTrue();
        Availability("jea", "Production", true).Reason.Should().Be("collectorNotConfigured", "kinds are exact");
        UsageScanJobAvailability.ReasonText("collectorNotConfigured").Should().Contain("script").And.Contain("kapalı");

        static UsageScanJobAvailability Availability(string collector, string environment, bool queue) =>
            new(Options.Create(new ServiceAccountOptions { Provider = "SqlServer", UsageScanJobs = new() { Collector = collector } }), environment,
                queue ? new StubDispatcher() : new UnavailableUsageScanJobDispatcher());
    }

    private static UsageCollectionStatus? Status(UsageScanJobRecord job, string server) => job.Servers.Single(s => s.ServerName == server).Status;

    private static byte[] Secret(string server) => System.Text.Encoding.UTF8.GetBytes($$"""
        {"schema":"service-account-usage-v1","serverName":"{{server}}","generatedAt":"2026-10-13T08:00:00Z","durationMs":1,"accounts":["SYN\\svc_a","svc_b"],
         "scanResult":"Success","sources":{"WindowsServices":"Success","ScheduledTasks":"Success","Iis":"Success"},
         "components":[{"ComponentType":"WindowsService","ComponentName":"SynSvc01","Identity":"SYN\\svc_a","MatchedAccount":"SYN\\svc_a","Password":"x"}],
         "warnings":[]}
        """);

    private sealed class Harness
    {
        public InMemoryUsageScanJobStore Store { get; } = new();

        public bool StarterAllowed { get; init; } = true;

        public string Collector { get; init; } = "Fake";

        public string Environment { get; init; } = "Test";

        public async Task<Guid> CreateAsync(string collector = "Fake", string? expected = null)
        {
            var id = Guid.NewGuid();
            (await Store.CreateAsync(new NewUsageScanJob(id, expected is null ? "Discovery" : "GmsaCheck", expected,
                [new UsageScanJobAccountInput(_accountA, "SYN\\svc_a"), new UsageScanJobAccountInput(_accountB, "svc_b")], _servers, new string('a', 64),
                collector, null, _starter, _now), new SaActor(_starter, "test"), CancellationToken.None)).Should().BeNull();
            return id;
        }

        public UsageScanJobRunner Runner(IServiceAccountUsageCollector? collector = null)
        {
            ServiceAccountOptions options = new() { Provider = "SqlServer", UsageScanJobs = new() { Collector = Collector } };
            UsageScanJobAvailability availability = new(Options.Create(options), Environment, new StubDispatcher());
            IServiceAccountUsageCollector used = collector ?? new FakeServiceAccountUsageCollector(new UsageScanCollectorContractTests.FixedClock(_now));
            return new UsageScanJobRunner(Store, Collector == "Jea" ? [] : [used], availability, new StubStarter(StarterAllowed), Options.Create(options),
                new UsageScanCollectorContractTests.FixedClock(_now), NullLogger<UsageScanJobRunner>.Instance);
        }
    }

    private sealed class StubStarter(bool allowed) : IUsageScanJobStarterCheck
    {
        public Task<bool> IsStillAllowedAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(allowed);
    }

    private sealed class StubDispatcher : IUsageScanJobDispatcher
    {
        public bool IsConfigured => true;

        public void Enqueue(Guid jobId)
        {
        }
    }

    /// <summary>Reports <c>release</c> servers with the fake's answers, then waits until cancelled (like a slow fleet).</summary>
    private sealed class GatedCollector(int release) : IServiceAccountUsageCollector
    {
        public TaskCompletionSource Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Called { get; private set; }

        public string Kind => "Fake";

        public async Task CollectAsync(UsageCollectionRequest request, IUsageCollectionObserver observer, CancellationToken cancellationToken)
        {
            Called = true;
            FakeServiceAccountUsageCollector fake = new(new UsageScanCollectorContractTests.FixedClock(_now));
            foreach (string server in request.Servers.Take(release))
            {
                await observer.ServerStartingAsync(server, cancellationToken);
                await observer.ServerCompletedAsync(fake.Result(request, server, TimeSpan.FromMilliseconds(3)), cancellationToken);
            }

            Reported.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class ThrowingCollector(int after) : IServiceAccountUsageCollector
    {
        public string Kind => "Fake";

        public async Task CollectAsync(UsageCollectionRequest request, IUsageCollectionObserver observer, CancellationToken cancellationToken)
        {
            FakeServiceAccountUsageCollector fake = new(new UsageScanCollectorContractTests.FixedClock(_now));
            foreach (string server in request.Servers.Take(after))
            {
                await observer.ServerCompletedAsync(fake.Result(request, server, TimeSpan.Zero), cancellationToken);
            }

            throw new InvalidOperationException("synthetic transport failure with remote text that must not be logged");
        }
    }

    private sealed class ScriptedCollector(Func<string, UsageCollectionServerResult> answer) : IServiceAccountUsageCollector
    {
        public string Kind => "Fake";

        public async Task CollectAsync(UsageCollectionRequest request, IUsageCollectionObserver observer, CancellationToken cancellationToken)
        {
            foreach (string server in request.Servers)
            {
                await observer.ServerCompletedAsync(answer(server), cancellationToken);
            }
        }
    }

    private sealed class BreachingCollector : IServiceAccountUsageCollector
    {
        public string Kind => "Fake";

        public async Task CollectAsync(UsageCollectionRequest request, IUsageCollectionObserver observer, CancellationToken cancellationToken)
        {
            await observer.ServerCompletedAsync(new UsageCollectionServerResult("SYN-APP-01", UsageCollectionStatus.Unreachable, null, TimeSpan.Zero), cancellationToken);
            await observer.ServerCompletedAsync(new UsageCollectionServerResult("syn-app-01", UsageCollectionStatus.Timeout, null, TimeSpan.Zero), cancellationToken);
            await observer.ServerCompletedAsync(new UsageCollectionServerResult("syn-other-99", UsageCollectionStatus.Ok, null, TimeSpan.Zero), cancellationToken);
        }
    }
}
