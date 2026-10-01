using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts;
using SecureOps.Infrastructure.ServiceAccounts.Reminders;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Tests.Integration.ServiceAccounts;

public sealed class ServiceAccountReminderSqlTests
{
    private static readonly CancellationToken _token = CancellationToken.None;
    private static readonly string[] _all = [.. ServiceAccountCapabilities.All.Where(c => c != ServiceAccountCapabilities.Administer)];

    [ServiceAccountSqlFact]
    public async Task RepeatedRunsCreateEachReminderOnce_ListingIsScoped_AndDismissUsesVersion()
    {
        ServiceAccountSqlFixture fx = new();
        MovableClock clock = new(DateTimeOffset.UtcNow);
        DateOnly today = ReportCalendar.LocalDate(clock.GetUtcNow());
        Guid mine = await fx.OrganizationAsync("SYN REM1 " + fx.Suffix), theirs = await fx.OrganizationAsync("SYN REM2 " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, mine);
        SynUser other = await fx.UserAsync(_all);
        await fx.GrantAsync(other, ScopeKind.Organization, theirs);
        Guid requestId = await OpenRequestAsync(fx, coordinator, mine, "REM1", new CreateWorkRequest("PasswordChange",
            PlanEnd: today.AddDays(-3), NextFollowupOn: today.AddDays(-1), FirstSentOn: today.AddDays(-10)));

        ServiceAccountReminderJob job = Job(fx, clock, maxAttempts: 5);
        await job.RunAsync(_token);
        await job.RunAsync(_token);
        clock.Advance(TimeSpan.FromHours(1));
        await job.RunAsync(_token);
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ReminderOutbox WHERE RequestId = @requestId", new { requestId }))
            .Should().Be(5, "follow-up and overdue (in-app + draft) plus one no-reply draft, each exactly once");
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ReminderOutbox WHERE RequestId = @requestId AND Status = 'Delivered'", new { requestId })).Should().Be(5);

        IReadOnlyList<ReminderView> listed = Ok(await fx.Service.RemindersAsync(coordinator.Principal, fx.Context, null, false, _token));
        listed.Where(r => r.RequestId == requestId).Should().HaveCount(5).And.OnlyContain(r => r.Message.Length > 0 && r.RuleLabel.Length > 0);
        Ok(await fx.Service.RemindersAsync(other.Principal, fx.Context, null, false, _token)).Should().NotContain(r => r.RequestId == requestId);

        ReminderView first = listed.First(r => r.RequestId == requestId);
        (await fx.Service.DismissReminderAsync(other.Principal, fx.Context, first.Id, new DismissReminderRequest(first.Version), _token))
            .ErrorCode.Should().Be(SaErrors.NotFound, "another scope cannot see or dismiss the reminder");
        Ok(await fx.Service.DismissReminderAsync(coordinator.Principal, fx.Context, first.Id, new DismissReminderRequest(first.Version), _token));
        (await fx.Service.DismissReminderAsync(coordinator.Principal, fx.Context, first.Id, new DismissReminderRequest(first.Version), _token))
            .ErrorCode.Should().Be(SaErrors.Conflict);
        (await fx.CountAsync("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor = @a AND Action = 'ServiceAccount.ReminderDismissed'",
            new { a = coordinator.User.Id.ToString("D") })).Should().Be(1);
    }

    [ServiceAccountSqlFact]
    public async Task FailedDelivery_RetriesWithBackoff_ThenDeadLettersVisibly()
    {
        ServiceAccountSqlFixture fx = new();
        MovableClock clock = new(DateTimeOffset.UtcNow);
        DateOnly today = ReportCalendar.LocalDate(clock.GetUtcNow());
        Guid org = await fx.OrganizationAsync("SYN REM3 " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        Guid requestId = await OpenRequestAsync(fx, coordinator, org, "REM3", new CreateWorkRequest("Review", NextFollowupOn: today));
        Guid accountId = await AccountOfRequestAsync(fx, requestId);

        ServiceAccountReminderJob job = new(fx.Repository, Options(2), clock, NullLogger<ServiceAccountReminderJob>.Instance)
        {
            DeliverOverride = claim => claim.AccountId == accountId ? throw new InvalidOperationException("synthetic failure") : Task.CompletedTask
        };
        await job.RunAsync(_token);
        (await StatusesAsync(fx, requestId)).Should().OnlyContain(s => s == "Failed");
        await job.RunAsync(_token);
        (await StatusesAsync(fx, requestId)).Should().OnlyContain(s => s == "Failed", "the backoff window has not elapsed");
        clock.Advance(TimeSpan.FromMinutes(6));
        await job.RunAsync(_token);
        (await StatusesAsync(fx, requestId)).Should().OnlyContain(s => s == "DeadLetter");
        clock.Advance(TimeSpan.FromDays(1));
        await job.RunAsync(_token);
        (await fx.CountAsync("SELECT MAX(AttemptCount) FROM svcacct.ReminderOutbox WHERE RequestId = @requestId", new { requestId })).Should().Be(2);
        Ok(await fx.Service.RemindersAsync(coordinator.Principal, fx.Context, "DeadLetter", false, _token))
            .Where(r => r.RequestId == requestId).Should().HaveCount(2).And.OnlyContain(r => r.LastError!.Contains("InvalidOperationException"));
    }

    [ServiceAccountSqlFact]
    public async Task ConcurrentClaims_NeverHandTheSameReminderToTwoOwners()
    {
        ServiceAccountSqlFixture fx = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateOnly today = ReportCalendar.LocalDate(now);
        Guid org = await fx.OrganizationAsync("SYN REM4 " + fx.Suffix);
        SynUser coordinator = await fx.UserAsync(_all);
        await fx.GrantAsync(coordinator, ScopeKind.Organization, org);
        List<(ReminderInput, DueReminder)> due = [];
        for (int i = 0; i < 6; i++)
        {
            Guid requestId = await OpenRequestAsync(fx, coordinator, org, "REM4" + i, new CreateWorkRequest("Evaluate", NextFollowupOn: today));
            ReminderInput input = new(requestId, await AccountOfRequestAsync(fx, requestId), "SYN", ServiceAccountActionType.Evaluate, null, null, today, null, null);
            due.AddRange(ReminderRules.Evaluate(input, today, new ReminderSettings(null, null)).Select(d => (input, d)));
        }

        (await fx.Repository.EnqueueRemindersAsync(due, now, _token)).Should().Be(12);
        (await fx.Repository.EnqueueRemindersAsync(due, now, _token)).Should().Be(0);
        IReadOnlyList<ReminderClaim>[] claims = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(i => fx.Repository.ClaimRemindersAsync("owner-" + i + fx.Suffix, 500, TimeSpan.FromMinutes(2), now.AddSeconds(1), _token)));
        List<Guid> claimed = [.. claims.SelectMany(c => c).Select(c => c.Id)];
        claimed.Should().OnlyHaveUniqueItems();
        HashSet<Guid> requests = [.. due.Select(d => d.Item1.RequestId)];
        (await fx.CountAsync("SELECT COUNT(*) FROM svcacct.ReminderOutbox WHERE LeaseOwner IS NOT NULL AND RequestId IN @requests", new { requests }))
            .Should().Be(12);
    }

    private static ServiceAccountReminderJob Job(ServiceAccountSqlFixture fx, TimeProvider clock, int maxAttempts) =>
        new(fx.Repository, Options(maxAttempts), clock, NullLogger<ServiceAccountReminderJob>.Instance);

    private static IOptions<ServiceAccountOptions> Options(int maxAttempts) => Microsoft.Extensions.Options.Options.Create(new ServiceAccountOptions
    {
        Provider = "SqlServer",
        Reminders = new ServiceAccountReminderOptions { Enabled = true, MaxAttempts = maxAttempts, PlanEndLeadDays = 2, NoReplyAfterDays = 7 }
    });

    private static async Task<Guid> OpenRequestAsync(ServiceAccountSqlFixture fx, SynUser user, Guid org, string name, CreateWorkRequest request)
    {
        AccountDetail account = Ok(await fx.Service.CreateAccountAsync(user.Principal, fx.Context, new CreateAccountRequest($"SYN{fx.Suffix}_{name}", null, org, "s"), _token));
        AccountDetail withRequest = Ok(await fx.Service.CreateRequestAsync(user.Principal, fx.Context, account.Summary.Id, request, _token));
        return withRequest.Requests.Single().Id;
    }

    private static async Task<Guid> AccountOfRequestAsync(ServiceAccountSqlFixture fx, Guid requestId)
    {
        await using SqlConnection connection = fx.Connection();
        return await connection.QuerySingleAsync<Guid>("SELECT AccountId FROM svcacct.WorkRequests WHERE Id = @requestId", new { requestId });
    }

    private static async Task<IReadOnlyList<string>> StatusesAsync(ServiceAccountSqlFixture fx, Guid requestId)
    {
        await using SqlConnection connection = fx.Connection();
        return [.. await connection.QueryAsync<string>("SELECT Status FROM svcacct.ReminderOutbox WHERE RequestId = @requestId", new { requestId })];
    }

    private static T Ok<T>(SaResult<T> result)
    {
        result.ErrorCode.Should().BeNull($"field {result.Field}");
        return result.Value!;
    }

    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
