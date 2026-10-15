using System.Collections.Concurrent;
using FluentAssertions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;

namespace SecureOps.Tests.Unit.ServiceAccounts;

/// <summary>
/// The collector contract (USAGE-SCAN-JOB-CONTRACT.md) through the deterministic fake: every server once, the documented statuses,
/// and documents the ADR-0027 §2 parser accepts as a <c>tool = Jea</c> bundle. Synthetic names only; nothing connects anywhere.
/// </summary>
public sealed class UsageScanCollectorContractTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] _servers =
        ["syn-app-01", "syn-app-02", "syn-app-03", "syn-unreach-01", "syn-noep-01", "syn-deny-01", "syn-slow-01", "syn-bad-01", "syn-part-01",
         "syn-web-01", "syn-web-02", "syn-web-03"];

    [Fact]
    public async Task Fake_ReportsEveryServerOnce_WithTheDocumentedStatuses()
    {
        Recorder recorder = await CollectAsync(Request(null));

        recorder.Completed.Select(r => r.ServerName).Should().BeEquivalentTo(_servers);
        recorder.Started.Should().BeEquivalentTo(_servers);
        Status("syn-unreach-01").Should().Be(UsageCollectionStatus.Unreachable);
        Status("syn-noep-01").Should().Be(UsageCollectionStatus.NoEndpoint);
        Status("syn-deny-01").Should().Be(UsageCollectionStatus.Forbidden);
        Status("syn-slow-01").Should().Be(UsageCollectionStatus.Timeout);
        Status("syn-bad-01").Should().Be(UsageCollectionStatus.Malformed);
        recorder.Completed.Where(r => r.Status != UsageCollectionStatus.Ok).Should().OnlyContain(r => r.Document == null,
            "only Ok carries a document");
        recorder.Completed.Where(r => r.Status == UsageCollectionStatus.Ok).Should().OnlyContain(r => r.Document != null);

        UsageCollectionStatus Status(string server) => recorder.Completed.Single(r => r.ServerName == server).Status;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SYN\\gmsa-syn-a$")]
    public async Task Fake_Documents_PassTheUploadParser_OneByOneAndAsAJeaBundle(string? expected)
    {
        UsageCollectionRequest request = Request(expected);
        Recorder recorder = await CollectAsync(request);

        Dictionary<string, byte[]> documents = new(StringComparer.OrdinalIgnoreCase);
        foreach (UsageCollectionServerResult ok in recorder.Completed.Where(r => r.Status == UsageCollectionStatus.Ok))
        {
            (ParsedUsageScan? parsed, string? refused) = UsageScanJobBundle.Validate(request.Accounts, expected, ok.ServerName, ok.Document!,
                request.Limits.MaxDocumentBytes, _now);
            refused.Should().BeNull();
            parsed!.Servers.Should().ContainSingle().Which.ServerName.Should().Be(ok.ServerName);
            documents[ok.ServerName] = ok.Document!;
        }

        byte[] bundle = UsageScanJobBundle.Build(request.Accounts, expected, request.Servers, documents,
            recorder.Completed.ToDictionary(r => r.ServerName, r => r.Status, StringComparer.OrdinalIgnoreCase), _now);
        ParsedUsageScan scan = UsageScanParser.Parse(bundle, int.MaxValue, _now);

        scan.Tool.Should().Be("Jea");
        scan.Purpose.Should().Be(expected is null ? "Discovery" : "GmsaCheck");
        scan.Servers.Select(s => s.ServerName).Should().Equal(_servers, "every planned server stays in the record, in order");
        scan.Servers.Single(s => s.ServerName == "syn-unreach-01").Result.Should().Be(ScanServerResult.Unreachable);
        scan.Servers.Where(s => s.ServerName is "syn-noep-01" or "syn-deny-01" or "syn-slow-01" or "syn-bad-01")
            .Should().OnlyContain(s => s.Result == ScanServerResult.NoResult, "no detailed reason pretends to be a scan result");
        scan.Servers.Single(s => s.ServerName == "syn-part-01").Result.Should().Be(ScanServerResult.Partial);
        scan.Items.Should().NotBeEmpty("the fake finds something on some servers");
        scan.Items.Should().OnlyContain(i => i.ComponentName.StartsWith("Syn", StringComparison.Ordinal)
            || i.ComponentName.StartsWith("\\Syn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fake_IsDeterministic()
    {
        Recorder first = await CollectAsync(Request(null));
        Recorder second = await CollectAsync(Request(null));

        first.Completed.OrderBy(r => r.ServerName).Select(r => (r.ServerName, r.Status, Text(r.Document)))
            .Should().Equal(second.Completed.OrderBy(r => r.ServerName).Select(r => (r.ServerName, r.Status, Text(r.Document))));

        static string? Text(byte[]? bytes) => bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    [Fact]
    public async Task Fake_StopsOnCancellation_AndLeavesTheRestUnreported()
    {
        FakeServiceAccountUsageCollector fake = new(TimeProvider.System, TimeSpan.FromSeconds(10));
        Recorder recorder = new();
        using CancellationTokenSource cancel = new();
        Task run = fake.CollectAsync(Request(null) with { Limits = UsageCollectionLimits.Default with { MaxParallel = 2 } }, recorder, cancel.Token);

        await cancel.CancelAsync();
        Func<Task> wait = () => run;
        await wait.Should().ThrowAsync<OperationCanceledException>();
        recorder.Completed.Should().BeEmpty("no server finished before the cancel");
    }

    [Fact]
    public void Validate_RefusesSecretsTooLargeAndForeignServers()
    {
        string[] accounts = ["SYN\\svc_a"];
        byte[] secret = System.Text.Encoding.UTF8.GetBytes("""
            {"schema":"service-account-usage-v1","serverName":"syn-app-01","generatedAt":"2026-10-13T08:00:00Z","durationMs":1,"accounts":["SYN\\svc_a"],
             "scanResult":"Success","sources":{"WindowsServices":"Success","ScheduledTasks":"Success","Iis":"Success"},
             "components":[{"ComponentType":"WindowsService","ComponentName":"SynSvc01","Identity":"SYN\\svc_a","MatchedAccount":"SYN\\svc_a","Password":"x"}],
             "warnings":[]}
            """);

        UsageScanJobBundle.Validate(accounts, null, "syn-app-01", secret, 512 * 1024, _now).RefusalCode.Should().Be(UsageScanFileCodes.SecretField);
        UsageScanJobBundle.Validate(accounts, null, "syn-app-01", secret, 10, _now).RefusalCode.Should().Be(UsageScanFileCodes.TooLarge);
        UsageScanJobBundle.Validate(accounts, null, "syn-other-01", secret, 512 * 1024, _now).RefusalCode.Should().NotBeNull();
        UsageScanJobBundle.Validate(accounts, null, "syn-app-01", "not json"u8.ToArray(), 512 * 1024, _now).RefusalCode.Should().Be(UsageScanFileCodes.Json);
    }

    private static UsageCollectionRequest Request(string? expected) =>
        new(Guid.Parse("4b4f7e0e-0000-4000-8000-000000000001"), ["SYN\\svc_a", "SYN\\svc_b", "svc_c"], expected, _servers, UsageCollectionLimits.Default);

    private static async Task<Recorder> CollectAsync(UsageCollectionRequest request)
    {
        Recorder recorder = new();
        await new FakeServiceAccountUsageCollector(new FixedClock(_now)).CollectAsync(request, recorder, CancellationToken.None);
        return recorder;
    }

    internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

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
}
