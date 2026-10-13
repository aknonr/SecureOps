using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;
using SecureOps.Infrastructure.ServiceAccounts.UsageScans.Jobs;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>
/// Web usage scan jobs (migration 037) on the real svcacct schema with the deterministic fake collector: start under Work and the
/// responsible basis, one active scan per account (409), the runner writing every server and the tool = Jea evidence into the existing
/// 030 tables with one link, history and audit per account, cancel, retry of incompletely read servers only, and a lost Worker lease
/// closed as workerLost. Synthetic names only; nothing contacts a server. Runs alone with the other write-gate tests.
/// </summary>
[Collection(ServiceAccountWriteGateCollection.Name)]
public sealed class ServiceAccountUsageScanJobSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All];
    private static readonly string[] _servers = ["syn-app-01", "syn-app-02", "syn-unreach-01", "syn-noep-01", "syn-part-01", "syn-web-01"];

    [ServiceAccountSqlFact]
    public async Task Start_Run_WritesJeaEvidence_LinksEachAccount_AndFreesTheAccounts()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, ServiceAccountService service, Recorder queue) = await SetupAsync();
        AccountDetail a = await CreateAccountAsync(fx, coordinator, org, "J1");
        AccountDetail b = await CreateAccountAsync(fx, coordinator, org, "J2");

        UsageScanJobView started = Ok(await service.StartUsageScanJobAsync(coordinator.Principal, fx.Context,
            new StartUsageScanJobRequest([a.Summary.Id, b.Summary.Id], _servers, null), _token));
        started.State.Should().Be("Queued");
        queue.Jobs.Should().Equal(started.Id);
        SaResult<UsageScanJobView> again = await service.StartUsageScanJobAsync(coordinator.Principal, fx.Context,
            new StartUsageScanJobRequest([b.Summary.Id], ["syn-app-09"], null), _token);
        again.ErrorCode.Should().Be(SaErrors.UsageScanAlreadyRunning);

        await Runner(fx).RunAsync(started.Id, _token);

        UsageScanJobView done = Ok(await service.UsageScanJobAsync(coordinator.Principal, fx.Context, started.Id, 0, _token));
        done.State.Should().Be("Completed");
        done.Servers.Should().OnlyContain(s => s.State == "Done");
        done.Servers.Single(s => s.ServerName == "syn-unreach-01").Tile.Should().Be("NotCovered");
        done.Accounts.Should().OnlyContain(x => x.LinkOutcome == "Attached");
        done.ScanId.Should().NotBeNull();
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScans WHERE Id = @s AND Tool = 'Jea' AND PlannedServers = 6", new { s = done.ScanId }))
            .Should().Be(1);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScanServers WHERE ScanId = @s AND Result IN ('Unreachable','NoResult')", new { s = done.ScanId }))
            .Should().Be(2, "unreachable and no endpoint stay no information");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScanLinks WHERE ScanId = @s", new { s = done.ScanId })).Should().Be(2);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.History WHERE EntityId = @j AND Action IN ('UsageScanJobRequested','UsageScanJobCompleted')",
            new { j = started.Id })).Should().Be(4);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Action LIKE 'ServiceAccount.UsageScanJob%' AND DetailsJson LIKE @j",
            new { j = "%" + started.Id.ToString("D") + "%" })).Should().Be(2);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Action LIKE 'ServiceAccount.UsageScanJob%' AND DetailsJson LIKE '%syn-app-01%'"))
            .Should().Be(0, "server names stay in the job record, not in the audit text");
        Ok(await service.AccountAsync(coordinator.Principal, fx.Context, a.Summary.Id, _token)).UsageScans!.Should().ContainSingle(s => s.ScanId == done.ScanId);
        Ok(await service.StartUsageScanJobAsync(coordinator.Principal, fx.Context, new StartUsageScanJobRequest([b.Summary.Id], ["syn-app-09"], null), _token))
            .State.Should().Be("Queued", "the finished scan released the account");
    }

    [ServiceAccountSqlFact]
    public async Task Start_RefusesOutOfScopeAsMissing_AndAParticipantAsForbidden()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, ServiceAccountService service, _) = await SetupAsync();
        AccountDetail owned = await CreateAccountAsync(fx, coordinator, org, "J3");
        Guid otherOrg = await fx.OrganizationAsync("SYN JOB BASKA " + fx.Suffix);
        SynUser other = await fx.UserAsync(_all);
        await fx.GrantAsync(other, ScopeKind.Organization, otherOrg);

        SaResult<UsageScanJobView> refused = await service.StartUsageScanJobAsync(other.Principal, fx.Context,
            new StartUsageScanJobRequest([owned.Summary.Id], _servers, null), _token);

        (refused.ErrorCode, refused.Field).Should().Be((SaErrors.NotFound, "accountIds"));
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScanJobAccounts WHERE AccountId = @a", new { a = owned.Summary.Id })).Should().Be(0);
        SaResult<UsageScanJobView> badServer = await service.StartUsageScanJobAsync(coordinator.Principal, fx.Context,
            new StartUsageScanJobRequest([owned.Summary.Id], ["syn-app-01", "bad name"], null), _token);
        (badServer.ErrorCode, badServer.Field).Should().Be((SaErrors.Invalid, "serverName"));
    }

    [ServiceAccountSqlFact]
    public async Task CancelQueued_ClosesAtOnce_ThenRetryRepeatsOnlyServersWithoutACompleteRead()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, ServiceAccountService service, _) = await SetupAsync();
        AccountDetail a = await CreateAccountAsync(fx, coordinator, org, "J4");
        UsageScanJobView first = Ok(await service.StartUsageScanJobAsync(coordinator.Principal, fx.Context,
            new StartUsageScanJobRequest([a.Summary.Id], _servers, null), _token));

        UsageScanJobView cancelled = Ok(await service.CancelUsageScanJobAsync(coordinator.Principal, fx.Context, first.Id, _token));
        cancelled.State.Should().Be("Cancelled");
        cancelled.Servers.Should().OnlyContain(s => s.Status == "NotRun");
        (await service.CancelUsageScanJobAsync(coordinator.Principal, fx.Context, first.Id, _token)).ErrorCode.Should().Be(SaErrors.UsageScanJobState);

        UsageScanJobView retry = Ok(await service.RetryUsageScanJobAsync(coordinator.Principal, fx.Context, first.Id, _token));
        retry.RetryOfJobId.Should().Be(first.Id);
        await Runner(fx).RunAsync(retry.Id, _token);
        UsageScanJobView retried = Ok(await service.UsageScanJobAsync(coordinator.Principal, fx.Context, retry.Id, 0, _token));

        UsageScanJobView second = Ok(await service.RetryUsageScanJobAsync(coordinator.Principal, fx.Context, retry.Id, _token));
        second.Servers.Select(s => s.ServerName).Should().Equal(
            retried.Servers.Where(s => s.Status != "Ok" || s.DocumentResult != "Success").Select(s => s.ServerName),
            "a retry never repeats a complete read");
        second.Servers.Select(s => s.ServerName).Should().Contain(["syn-unreach-01", "syn-noep-01", "syn-part-01"]);
    }

    [ServiceAccountSqlFact]
    public async Task ExpiredLease_IsClosedAsWorkerLost_WhenRead_AndFreesTheAccount()
    {
        (ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, ServiceAccountService service, _) = await SetupAsync();
        AccountDetail a = await CreateAccountAsync(fx, coordinator, org, "J5");
        UsageScanJobView job = Ok(await service.StartUsageScanJobAsync(coordinator.Principal, fx.Context,
            new StartUsageScanJobRequest([a.Summary.Id], _servers, null), _token));
        DateTimeOffset past = DateTimeOffset.UtcNow.AddHours(-1);
        (await fx.Repository.ClaimAsync(job.Id, Guid.NewGuid(), past, past.AddMinutes(5), _token)).Should().BeTrue();

        UsageScanJobView read = Ok(await service.UsageScanJobAsync(coordinator.Principal, fx.Context, job.Id, 0, _token));

        (read.State, read.FailureCode).Should().Be(("Failed", "workerLost"));
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.UsageScanJobAccounts WHERE AccountId = @a AND IsActive = 1", new { a = a.Summary.Id }))
            .Should().Be(0);
    }

    private static UsageScanJobRunner Runner(ServiceAccountSqlFixture fx)
    {
        ServiceAccountOptions options = Options();
        return new UsageScanJobRunner(fx.Repository, [new FakeServiceAccountUsageCollector(TimeProvider.System)],
            new UsageScanJobAvailability(Microsoft.Extensions.Options.Options.Create(options), "Test", new Recorder()), new Starter(fx),
            Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System, NullLogger<UsageScanJobRunner>.Instance);
    }

    private static ServiceAccountOptions Options() => new() { Provider = "SqlServer", UsageScanJobs = new() { Collector = "Fake" } };

    private static async Task<(ServiceAccountSqlFixture, SynUser, Guid, ServiceAccountService, Recorder)> SetupAsync()
    {
        ServiceAccountSqlFixture fx = new();
        if (await fx.CountAsync("SELECT CASE WHEN OBJECT_ID(N'svcacct.UsageScanJobs', N'U') IS NULL THEN 0 ELSE 1 END") == 0)
        {
            throw new InvalidOperationException("Migration 037 is not installed on the isolated test database (run sa-sql-harness.ps1).");
        }

        Guid org = await fx.OrganizationAsync("SYN WEB TARAMA ORG " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        Recorder queue = new();
        ServiceAccountOptions options = Options();
        ServiceAccountService service = fx.WithUsageScanJobs(options,
            new UsageScanJobAvailability(Microsoft.Extensions.Options.Options.Create(options), "Test", queue), queue);
        return (fx, coordinator, org, service, queue);
    }

    private static async Task<AccountDetail> CreateAccountAsync(ServiceAccountSqlFixture fx, SynUser coordinator, Guid org, string label) =>
        Ok(await fx.Service.CreateAccountAsync(coordinator.Principal, fx.Context,
            new CreateAccountRequest($"SYN_WEB_{label}_{fx.Suffix}", "SYN", org, "Sentetik web taraması testi hesabı"), _token));

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }

    /// <summary>Records queued jobs instead of handing them to Hangfire; the test runs the runner itself.</summary>
    private sealed class Recorder : IUsageScanJobDispatcher
    {
        public List<Guid> Jobs { get; } = [];

        public bool IsConfigured => true;

        public void Enqueue(Guid jobId) => Jobs.Add(jobId);
    }

    private sealed class Starter(ServiceAccountSqlFixture fx) : IUsageScanJobStarterCheck
    {
        public Task<bool> IsStillAllowedAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(fx.User(userId) is { } user && user.Capabilities.Contains(ServiceAccountCapabilities.Work));
    }
}
