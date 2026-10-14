using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;
using SecureOps.Tests.Unit.ServiceAccounts;
using SecureOps.Worker.Jea;

namespace SecureOps.Tests.Unit.Worker;

/// <summary>
/// The <c>Jea</c> collector against USAGE-SCAN-JOB-CONTRACT.md §3 (TARAMA-S report, five points): every server exactly once, failure =
/// result, a document only with Ok and with the requested <c>serverName</c>, the size limit, the status mapping, MaxParallel and timeouts
/// passed through, cancellation; then end to end through the module's job runner. Fake runners only; nothing connects anywhere.
/// </summary>
public sealed class JeaServiceAccountUsageCollectorTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] _servers =
        ["syn-hit-01.syn.example", "syn-hit-02", "syn-app-03", "syn-unreach-01", "syn-noep-01", "syn-deny-01", "syn-slow-01", "syn-bad-01"];

    [Theory]
    [InlineData(null)]
    [InlineData("SYN\\gmsa_a$")]
    public async Task EveryServerOnce_StatusesMapped_AndOkDocumentsPassTheModuleParserWithTheRequestedName(string? expected)
    {
        UsageCollectionRequest request = Request(_servers, expected);
        Recorder recorder = new();

        await Collector(new FakeJeaRunner()).CollectAsync(request, recorder, CancellationToken.None);

        recorder.Completed.Select(r => r.ServerName).Should().BeEquivalentTo(_servers).And.OnlyHaveUniqueItems();
        recorder.Started.Should().BeEquivalentTo(_servers);
        Status("syn-unreach-01").Should().Be(UsageCollectionStatus.Unreachable);
        Status("syn-noep-01").Should().Be(UsageCollectionStatus.NoEndpoint);
        Status("syn-deny-01").Should().Be(UsageCollectionStatus.Forbidden);
        Status("syn-slow-01").Should().Be(UsageCollectionStatus.Timeout);
        Status("syn-bad-01").Should().Be(UsageCollectionStatus.Malformed);
        recorder.Completed.Where(r => r.Status != UsageCollectionStatus.Ok).Should().OnlyContain(r => r.Document == null);
        foreach (UsageCollectionServerResult ok in recorder.Completed.Where(r => r.Status == UsageCollectionStatus.Ok))
        {
            JsonNode.Parse(ok.Document!)!["serverName"]!.GetValue<string>().Should().Be(ok.ServerName, "the endpoint's COMPUTERNAME is replaced by the requested name");
            (ParsedUsageScan? parsed, string? refused) = UsageScanJobBundle.Validate(request.Accounts, expected, ok.ServerName, ok.Document!,
                request.Limits.MaxDocumentBytes, _now);
            refused.Should().BeNull();
            parsed!.Servers.Single().ServerName.Should().Be(ok.ServerName);
        }

        UsageCollectionStatus Status(string server) => recorder.Completed.Single(r => r.ServerName == server).Status;
    }

    [Fact]
    public async Task TheRunnerGetsTheAllowListedCommand_AndTheJobLimits()
    {
        RecordingRunner runner = new();
        UsageCollectionRequest request = Request(["syn-app-01"], "SYN\\gmsa_a$") with
        {
            Limits = new UsageCollectionLimits(5, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(70), 4096)
        };

        await Collector(runner).CollectAsync(request, new Recorder(), CancellationToken.None);

        (string server, JeaCommand command, JeaInvocationLimits limits) = runner.Calls.Single();
        server.Should().Be("syn-app-01");
        command.Endpoint.Should().BeSameAs(JeaEndpointCatalog.ServiceAccountUsage);
        command.Parameters.Select(p => p.Key).Should().Equal("Account", "ExpectedAccount", "AsJson");
        ((string[])command.Parameters[0].Value).Should().Equal(request.Accounts);
        command.Parameters[1].Value.Should().Be("SYN\\gmsa_a$");
        limits.Should().Be(new JeaInvocationLimits(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(70), 4096));
    }

    [Fact]
    public async Task AnInvalidAccountRefusesTheJob_BeforeAnyServerIsContacted()
    {
        RecordingRunner runner = new();
        UsageCollectionRequest request = Request(["syn-app-01"], null) with { Accounts = ["svc_a'; Stop-Service x"] };

        Func<Task> run = () => Collector(runner).CollectAsync(request, new Recorder(), CancellationToken.None);

        await run.Should().ThrowAsync<JeaCommandRejectedException>();
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AtMostMaxParallelServersRunAtOnce()
    {
        CountingRunner runner = new(TimeSpan.FromMilliseconds(40));
        string[] servers = [.. Enumerable.Range(1, 24).Select(i => $"syn-app-{i:00}")];
        UsageCollectionRequest request = Request(servers, null) with { Limits = UsageCollectionLimits.Default with { MaxParallel = 3 } };
        Recorder recorder = new();

        await Collector(runner).CollectAsync(request, recorder, CancellationToken.None);

        runner.MaxInFlight.Should().BeInRange(2, 3, "bounded by MaxParallel, and actually parallel");
        recorder.Completed.Should().HaveCount(24);
    }

    [Fact]
    public async Task Cancellation_StopsStartingServers_AndNothingIsReportedTwice()
    {
        CountingRunner runner = new(TimeSpan.FromMilliseconds(50));
        string[] servers = [.. Enumerable.Range(1, 40).Select(i => $"syn-app-{i:00}")];
        UsageCollectionRequest request = Request(servers, null) with { Limits = UsageCollectionLimits.Default with { MaxParallel = 2 } };
        Recorder recorder = new();
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(180));

        Func<Task> run = () => Collector(runner).CollectAsync(request, recorder, cancel.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        int startedAtCancel = recorder.Started.Count;
        await Task.Delay(200);
        recorder.Started.Should().HaveCount(startedAtCancel, "no server starts after cancellation");
        recorder.Completed.Count.Should().BeLessThan(servers.Length);
        recorder.Completed.Select(r => r.ServerName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ARunnerThatThrowsForOneServer_StillGivesThatServerOneResult()
    {
        ThrowingRunner runner = new("syn-app-02");
        Recorder recorder = new();

        await Collector(runner).CollectAsync(Request(["syn-app-01", "syn-app-02", "syn-app-03"], null), recorder, CancellationToken.None);

        recorder.Completed.Should().HaveCount(3);
        recorder.Completed.Single(r => r.ServerName == "syn-app-02").Status.Should().Be(UsageCollectionStatus.Malformed);
        recorder.Completed.Where(r => r.ServerName != "syn-app-02").Should().OnlyContain(r => r.Status == UsageCollectionStatus.Ok);
    }

    [Theory]
    [InlineData(JeaInvocationStatus.Ok, UsageCollectionStatus.Ok)]
    [InlineData(JeaInvocationStatus.Unreachable, UsageCollectionStatus.Unreachable)]
    [InlineData(JeaInvocationStatus.NoEndpoint, UsageCollectionStatus.NoEndpoint)]
    [InlineData(JeaInvocationStatus.Forbidden, UsageCollectionStatus.Forbidden)]
    [InlineData(JeaInvocationStatus.Timeout, UsageCollectionStatus.Timeout)]
    [InlineData(JeaInvocationStatus.Malformed, UsageCollectionStatus.Malformed)]
    public void StatusMapping_FollowsTheContractTable(JeaInvocationStatus runner, UsageCollectionStatus module) =>
        JeaServiceAccountUsageCollector.Map(runner).Should().Be(module);

    [Fact]
    public void EveryRunnerStatusHasAMapping_AndNoneBecomesNotRun() =>
        Enum.GetValues<JeaInvocationStatus>().Select(JeaServiceAccountUsageCollector.Map).Should().NotContain(UsageCollectionStatus.NotRun);

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{\"schema\":\"service-account-usage-v2\"}")]
    [InlineData("{\"schema\":1}")]
    [InlineData("{\"serverName\":\"x\"}")]
    [InlineData("{\"schema\":\"service-account-usage-v1\",\"schema\":\"service-account-usage-v1\"}")]
    [InlineData("{\"schema\":\"service-account-usage-v1\"")]
    [InlineData("")]
    public void Document_IsNullUnlessOneObjectOfTheSchema(string output) =>
        JeaServiceAccountUsageCollector.Document("syn-app-01", output, 4096).Should().BeNull();

    [Fact]
    public void Document_SetsTheRequestedName_KeepsEverythingElse_AndHonoursTheSizeLimit()
    {
        string output = "{\"schema\":\"service-account-usage-v1\",\"serverName\":\"SYN-APP-01\",\"durationMs\":3,\"accounts\":[\"svc_a\"]}";

        JsonNode document = JsonNode.Parse(JeaServiceAccountUsageCollector.Document("syn-app-01.syn.example", output, 4096)!)!;

        document["serverName"]!.GetValue<string>().Should().Be("syn-app-01.syn.example");
        document["durationMs"]!.GetValue<int>().Should().Be(3);
        document["accounts"]![0]!.GetValue<string>().Should().Be("svc_a");
        JeaServiceAccountUsageCollector.Document("syn-app-01", output, Encoding.UTF8.GetByteCount(output) - 1).Should().BeNull();
    }

    [Fact]
    public async Task EndToEnd_TheModuleJobRunnerWithTheJeaCollector_WritesJeaEvidence()
    {
        var starter = Guid.Parse("5a5a5a5a-0000-4000-8000-000000000001");
        var account = Guid.Parse("5a5a5a5a-0000-4000-8000-0000000000a1");
        InMemoryUsageScanJobStore store = new();
        var jobId = Guid.NewGuid();
        (await store.CreateAsync(new NewUsageScanJob(jobId, "Discovery", null, [new UsageScanJobAccountInput(account, "SYN\\svc_a")], _servers,
            new string('a', 64), UsageCollectorKinds.Jea, null, starter, _now), new SaActor(starter, "test"), CancellationToken.None)).Should().BeNull();
        var options = new ServiceAccountOptions { Provider = "SqlServer", UsageScanJobs = new() { Collector = UsageCollectorKinds.Jea } };
        var availability = new UsageScanJobAvailability(Options.Create(options), "Production", new ConfiguredDispatcher());
        UsageScanJobRunner runner = new(store, [Collector(new FakeJeaRunner())], availability, new AllowedStarter(), Options.Create(options),
            new UsageScanCollectorContractTests.FixedClock(_now), NullLogger<UsageScanJobRunner>.Instance);

        await runner.RunAsync(jobId, CancellationToken.None);

        UsageScanJobRecord job = (await store.GetAsync(jobId, CancellationToken.None))!;
        job.State.Should().Be(UsageScanJobState.Completed);
        job.Servers.Should().OnlyContain(s => s.State == UsageScanJobServerState.Done);
        job.Servers.Single(s => s.ServerName == "syn-noep-01").Status.Should().Be(UsageCollectionStatus.NoEndpoint);
        job.Servers.Single(s => s.ServerName == "syn-hit-01.syn.example").Status.Should().Be(UsageCollectionStatus.Ok);
        UsageScanUpload upload = store.Evidence.Single().Upload;
        upload.Parsed.Tool.Should().Be("Jea");
        upload.Parsed.Items.Should().Contain(i => i.ServerName == "syn-hit-01.syn.example" && i.ComponentName == "SynSvc1");
        upload.Parsed.Servers.Single(s => s.ServerName == "syn-unreach-01").Result.Should().Be(ScanServerResult.Unreachable);
        upload.Parsed.Servers.Single(s => s.ServerName == "syn-deny-01").Result.Should().Be(ScanServerResult.NoResult);
    }

    [Fact]
    public void Registration_IsClosedByDefault_AndTheFakeNeverRunsInProduction()
    {
        Composition(new()).Collector.Should().BeNull("Collector = None by default: no collector, no runner use");

        Dictionary<string, string?> fakeInProduction = new()
        {
            ["environment"] = "Production",
            ["JeaRunner:Transport"] = "Fake",
            ["ServiceAccounts:Provider"] = "SqlServer",
            ["ServiceAccounts:UsageScanJobs:Collector"] = "Jea"
        };
        (string? transport, IServiceAccountUsageCollector? collector, IJeaRunner? jea) = Composition(fakeInProduction);
        transport.Should().BeNull();
        collector.Should().BeNull();
        jea.Should().BeNull();

        Composition(new() { ["JeaRunner:Transport"] = "Something" }).Transport.Should().BeNull("an unknown transport fails closed");
    }

    [Fact]
    public void Registration_WithJeaConfigured_RegistersTheJeaCollector()
    {
        Dictionary<string, string?> settings = new()
        {
            ["environment"] = "Test",
            ["JeaRunner:Transport"] = "Fake",
            ["ServiceAccounts:Provider"] = "SqlServer",
            ["ServiceAccounts:UsageScanJobs:Collector"] = "Jea"
        };

        (string? transport, IServiceAccountUsageCollector? collector, IJeaRunner? jea) = Composition(settings);

        transport.Should().Be(JeaTransportKinds.Fake);
        collector.Should().BeOfType<JeaServiceAccountUsageCollector>().Which.Kind.Should().Be("Jea");
        jea.Should().BeOfType<FakeJeaRunner>();

        settings["JeaRunner:Transport"] = "WsMan";
        (string? real, _, IJeaRunner? wsman) = Composition(settings);
        if (OperatingSystem.IsWindows())
        {
            real.Should().Be(JeaTransportKinds.WsMan);
            wsman.Should().BeOfType<JeaRunner>();
        }
        else
        {
            real.Should().BeNull("the real WinRM client exists only on Windows");
        }

        settings["ServiceAccounts:UsageScanJobs:Collector"] = "None";
        Composition(settings).Collector.Should().BeNull();
    }

    private static (string? Transport, IServiceAccountUsageCollector? Collector, IJeaRunner? Runner) Composition(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        services.AddLogging();
        string? transport = services.AddWorkerJea(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();
        return (transport, provider.GetService<IServiceAccountUsageCollector>(), provider.GetService<IJeaRunner>());
    }

    private static JeaServiceAccountUsageCollector Collector(IJeaRunner runner) => new(runner, NullLogger<JeaServiceAccountUsageCollector>.Instance);

    private static UsageCollectionRequest Request(IReadOnlyList<string> servers, string? expected) =>
        new(Guid.NewGuid(), ["SYN\\svc_a", "svc_b"], expected, servers, UsageCollectionLimits.Default);

    private sealed class Recorder : IUsageCollectionObserver
    {
        public ConcurrentBag<string> Started { get; } = [];

        public ConcurrentBag<UsageCollectionServerResult> Completed { get; } = [];

        public ValueTask ServerStartingAsync(string serverName, CancellationToken cancellationToken)
        {
            Started.Add(serverName);
            return ValueTask.CompletedTask;
        }

        public ValueTask ServerCompletedAsync(UsageCollectionServerResult result, CancellationToken cancellationToken)
        {
            Completed.Add(result);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRunner : IJeaRunner
    {
        public ConcurrentQueue<(string Server, JeaCommand Command, JeaInvocationLimits Limits)> Calls { get; } = [];

        public Task<JeaInvocationResult> InvokeAsync(string computerName, JeaCommand command, JeaInvocationLimits limits, CancellationToken cancellationToken)
        {
            Calls.Enqueue((computerName, command, limits));
            return Task.FromResult(new JeaInvocationResult(JeaInvocationStatus.Unreachable, null, TimeSpan.Zero, "test"));
        }
    }

    private sealed class CountingRunner(TimeSpan delay) : IJeaRunner
    {
        private int _inFlight;
        private int _max;

        public int MaxInFlight => Volatile.Read(ref _max);

        public async Task<JeaInvocationResult> InvokeAsync(string computerName, JeaCommand command, JeaInvocationLimits limits, CancellationToken cancellationToken)
        {
            int now = Interlocked.Increment(ref _inFlight);
            int seen;
            while (now > (seen = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, now, seen) != seen)
            {
            }

            try
            {
                await Task.Delay(delay, cancellationToken);
                return new(JeaInvocationStatus.Ok, FakeJeaRunner.UsageDocument(computerName, command), delay, null);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private sealed class ThrowingRunner(string failing) : IJeaRunner
    {
        public Task<JeaInvocationResult> InvokeAsync(string computerName, JeaCommand command, JeaInvocationLimits limits, CancellationToken cancellationToken) =>
            computerName == failing ? throw new InvalidOperationException("boom")
                : Task.FromResult(new JeaInvocationResult(JeaInvocationStatus.Ok, FakeJeaRunner.UsageDocument(computerName, command), TimeSpan.Zero, null));
    }

    private sealed class AllowedStarter : IUsageScanJobStarterCheck
    {
        public Task<bool> IsStillAllowedAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class ConfiguredDispatcher : IUsageScanJobDispatcher
    {
        public bool IsConfigured => true;

        public void Enqueue(Guid jobId)
        {
        }
    }
}
