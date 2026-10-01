using Dapper;
using FluentAssertions;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Integration.Sql;

[Collection("Announcement SQL")]
public sealed class AnnouncementSourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task RuntimeGrants_AllowHangfireDispatchWithoutDdlOrAuditMutation()
    {
        var db = new SourceTestDatabase();
        string user = "SourceRuntime_" + Guid.NewGuid().ToString("N");
        await db.ExecuteAsync($"""
            CREATE USER [{user}] WITHOUT LOGIN;
            GRANT SELECT,INSERT,UPDATE ON announcements.SourceJobs TO [{user}];
            GRANT SELECT,INSERT,UPDATE ON announcements.SourceOverrides TO [{user}];
            GRANT SELECT,INSERT ON announcements.DraftRevisions TO [{user}];
            GRANT INSERT ON audit.AuditLog TO [{user}];
            GRANT SELECT,INSERT,UPDATE,DELETE ON SCHEMA::HangFire TO [{user}];
            """);
        var storage = new SqlServerStorage(() =>
        {
            // Test-only impersonation must never return its session to the integrated-auth pool.
            var connection = new SqlConnection(new SqlConnectionStringBuilder(db.Connection) { Pooling = false }.ConnectionString);
            connection.Open();
            connection.Execute($"EXECUTE AS USER=N'{user}';");
            return connection;
        }, new SqlServerStorageOptions { PrepareSchemaIfNecessary = false, DisableGlobalLocks = true });
        string queuedId = new HangfireAnnouncementSourceDispatcher(new BackgroundJobClient(storage), "source-grants")
            .Enqueue(Guid.NewGuid());
        queuedId.Should().NotBeNullOrEmpty();
        (await db.ScalarAsync<int>($"""
            EXECUTE AS USER=N'{user}';
            SELECT HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CREATE TABLE')
                + HAS_PERMS_BY_NAME('audit.AuditLog','OBJECT','UPDATE')
                + HAS_PERMS_BY_NAME('audit.AuditLog','OBJECT','DELETE')
                + HAS_PERMS_BY_NAME('announcements.DraftRevisions','OBJECT','UPDATE')
                + HAS_PERMS_BY_NAME('announcements.SourceJobs','OBJECT','DELETE');
            REVERT;
            """)).Should().Be(0);
    }

    [LocalResourceSqlFact]
    public async Task Apply_WriteFailuresRollBackRevisionOverridesAndBothAudits()
    {
        var db = new SourceTestDatabase();
        AnnouncementDraft draft = await db.DraftAsync();
        AnnouncementSourceJob job = await db.CompletedAsync(draft);
        AnnouncementSourceOverrides next = AnnouncementSourceOverrides.Empty(draft.Id, draft.OwnerId) with
        { AppliedJobId = job.JobId, AppliedCapturedAt = job.Snapshot!.CapturedAt, Profile = "NonProd" };
        int auditCount = await db.AuditCountAsync(draft.OwnerId);
        foreach ((string table, string predicate) in new[]
        {
            ("announcements.DraftRevisions", $"Id='{draft.Id}'"),
            ("audit.AuditLog", $"Actor='{draft.OwnerId}' AND Action='AnnouncementDraftSaved'"),
            ("announcements.SourceOverrides", $"DraftId='{draft.Id}'"),
            ("audit.AuditLog", $"Actor='{draft.OwnerId}' AND Action='AnnouncementSourceApplied'")
        })
        {
            string trigger = await db.FailureAsync(table, predicate);
            try
            {
                await FluentActions.Awaiting(() => db.Sources.ApplyAsync(db.Drafts, draft with { Version = 2 },
                    next, 0, ["Scope"], true, "synthetic-rollback", default)).Should().ThrowAsync<SqlException>();
                (await db.Drafts.GetAsync(draft.Id, draft.OwnerId, default))!.Version.Should().Be(1);
                (await db.Sources.OverridesAsync(draft.Id, draft.OwnerId, default)).Version.Should().Be(0);
                (await db.AuditCountAsync(draft.OwnerId)).Should().Be(auditCount);
            }
            finally { await db.ExecuteAsync("DROP TRIGGER " + trigger); }
        }
        (await db.Sources.ApplyAsync(db.Drafts, draft with { Version = 2 }, next, 0, ["Scope"], true, "synthetic", default)).Should().BeNull();
        (await db.AuditCountAsync(draft.OwnerId)).Should().Be(auditCount + 2);
        (await db.Sources.OverridesAsync(draft.Id, draft.OwnerId, default)).Version.Should().Be(1);
        (await db.Sources.ApplyAsync(db.Drafts, draft with { Version = 2 }, next, 1, [], false, "synthetic", default)).Should().Be("AnnouncementConflict");
        (await db.Sources.ApplyAsync(db.Drafts, draft with { Version = 3 }, next, 0, [], false, "synthetic", default)).Should().Be("AnnouncementSourceOverrideConflict");
        (await db.Sources.ApplyAsync(db.Drafts, draft with { Version = 3 }, next, 1, [], false, "synthetic", default)).Should().Be("AnnouncementSourceStale");
        (await db.Drafts.GetAsync(draft.Id, draft.OwnerId, default))!.Version.Should().Be(2);
        (await db.AuditCountAsync(draft.OwnerId)).Should().Be(auditCount + 2);
    }

    [LocalResourceSqlFact]
    public async Task Submission_IdempotencyOwnerIsolationAndAuditFailureAreDurable()
    {
        var db = new SourceTestDatabase();
        AnnouncementDraft draft = await db.DraftAsync();
        AnnouncementSourceJob job = SourceTestDatabase.Job(draft);
        string key = Guid.NewGuid().ToString("N");
        string trigger = await db.FailureAsync("audit.AuditLog", $"Actor='{draft.OwnerId}' AND Action='AnnouncementSourceJobSubmitted'");
        try
        {
            await FluentActions.Awaiting(() => db.Sources.SubmitAsync(job, key, "synthetic", default)).Should().ThrowAsync<SqlException>();
            (await db.Sources.GetAsync(job.JobId, draft.OwnerId, default)).Should().BeNull();
        }
        finally { await db.ExecuteAsync("DROP TRIGGER " + trigger); }
        SqlAnnouncementSourceStore.SubmitResult[] submissions = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            db.Sources.SubmitAsync(job with { JobId = Guid.NewGuid() }, key, "synthetic", default)));
        submissions.Count(s => !s.Duplicate).Should().Be(1);
        submissions.Select(s => s.Job.JobId).Distinct().Should().ContainSingle();
        foreach (AnnouncementSourceJob conflict in new[] { job with { Profile = "Prod01" }, job with { OcoReference = "OCO-OTHER" } })
        {
            (await FluentActions.Awaiting(() => db.Sources.SubmitAsync(conflict, key, "synthetic", default))
                .Should().ThrowAsync<AnnouncementSourceException>()).Which.ErrorCode.Should().Be("AnnouncementSourceSubmissionConflict");
        }
        Guid id = submissions[0].Job.JobId;
        (await db.Sources.GetAsync(id, Guid.NewGuid(), default)).Should().BeNull();
        (await db.Sources.LatestAsync(draft.Id, Guid.NewGuid(), default)).Should().BeNull();
    }

    [LocalResourceSqlFact]
    public async Task Attempts_ExpiredAndOverlappingWorkersCannotOverwriteNewerResults()
    {
        var db = new SourceTestDatabase();
        AnnouncementDraft draft = await db.DraftAsync();
        AnnouncementSourceJob job = (await db.Sources.SubmitAsync(SourceTestDatabase.Job(draft), Guid.NewGuid().ToString("N"), "synthetic", default)).Job;
        SourceJobAttempt first = (await db.Sources.ClaimAsync(job.JobId, Guid.NewGuid(), 90, default))!;
        (await db.Sources.ClaimAsync(job.JobId, Guid.NewGuid(), 90, default)).Should().BeNull();
        await db.ExecuteAsync("UPDATE announcements.SourceJobs SET LeaseUntil=DATEADD(second,-1,SYSUTCDATETIME()) WHERE JobId=@id", new { id = job.JobId });
        (await db.Sources.CompleteAsync(job.JobId, first.AttemptId, "Succeeded", null, SourceTestDatabase.Snapshot(job), "late", DateTimeOffset.UtcNow, default)).Should().BeFalse();
        SourceJobAttempt second = (await db.Sources.ClaimAsync(job.JobId, Guid.NewGuid(), 90, default))!;
        second.Number.Should().Be(2);
        (await db.Sources.CompleteAsync(job.JobId, first.AttemptId, "Failed", "Obsolete", null, "late", DateTimeOffset.UtcNow, default)).Should().BeFalse();
        string trigger = await db.FailureAsync("audit.AuditLog", $"Actor='{draft.OwnerId}' AND Action='AnnouncementSourceJobCompleted'");
        try
        {
            await FluentActions.Awaiting(() => db.Sources.CompleteAsync(job.JobId, second.AttemptId, "Succeeded", null,
                SourceTestDatabase.Snapshot(job), "synthetic", DateTimeOffset.UtcNow, default)).Should().ThrowAsync<SqlException>();
            (await db.Sources.GetAsync(job.JobId, draft.OwnerId, default))!.State.Should().Be("Running");
        }
        finally { await db.ExecuteAsync("DROP TRIGGER " + trigger); }
        (await db.Sources.CompleteAsync(job.JobId, second.AttemptId, "Succeeded", null, SourceTestDatabase.Snapshot(job), "synthetic", DateTimeOffset.UtcNow, default)).Should().BeTrue();
        (await db.Sources.CompleteAsync(job.JobId, first.AttemptId, "Failed", "Obsolete", null, "late", DateTimeOffset.UtcNow, default)).Should().BeFalse();
        (await db.Sources.ClaimAsync(job.JobId, Guid.NewGuid(), 90, default)).Should().BeNull();
        (await db.Sources.GetAsync(job.JobId, draft.OwnerId, default))!.Snapshot.Should().NotBeNull();
    }

    [LocalResourceSqlFact]
    public async Task Dispatch_FailuresBeforeEnqueueAfterEnqueueAndBeforeAcknowledgementRemainRecoverable()
    {
        var db = new SourceTestDatabase();
        AnnouncementDraft draft = await db.DraftAsync();
        foreach (string stage in new[] { "before", "after", "ack" })
        {
            AnnouncementSourceJob job = (await db.Sources.SubmitAsync(SourceTestDatabase.Job(draft), Guid.NewGuid().ToString("N"), "synthetic", default)).Job;
            var dispatcher = new InterruptedDispatcher(stage);
            var recovery = new AnnouncementSourceRecovery(db.Sources, dispatcher, NullLogger<AnnouncementSourceRecovery>.Instance);
            string? trigger = stage == "ack" ? await db.FailureAsync("announcements.SourceJobs", $"JobId='{job.JobId}' AND HangfireJobId IS NOT NULL") : null;
            try
            { await recovery.DispatchAsync(job.JobId, default); }
            finally { if (trigger is not null) { await db.ExecuteAsync("DROP TRIGGER " + trigger); } }
            (await db.Sources.GetAsync(job.JobId, draft.OwnerId, default))!.State.Should().Be("Queued");
            dispatcher.Stage = "ok";
            await db.ExecuteAsync("UPDATE announcements.SourceJobs SET DispatchAfter=DATEADD(second,-1,SYSUTCDATETIME()) WHERE JobId=@id", new { id = job.JobId });
            await recovery.RunAsync(default);
            await recovery.RunAsync(default);
            dispatcher.Enqueued.Where(id => id == job.JobId).Should().HaveCount(stage == "before" ? 1 : 2);
            (await db.ScalarAsync<int>("SELECT COUNT(*) FROM announcements.SourceJobs WHERE JobId=@id", new { id = job.JobId })).Should().Be(1);
            (await db.ScalarAsync<string>("SELECT HangfireJobId FROM announcements.SourceJobs WHERE JobId=@id", new { id = job.JobId })).Should().NotBeNullOrEmpty();
        }
    }

    [LocalResourceSqlFact]
    public async Task Runner_RevokedOwnersAndExhaustedAttemptsNeverReadSources()
    {
        var db = new SourceTestDatabase();
        AnnouncementDraft draft = await db.DraftAsync();
        ICollectionMembershipClient collection = Substitute.For<ICollectionMembershipClient>();
        IAnnouncementServiceSourceClient service = Substitute.For<IAnnouncementServiceSourceClient>();
        IAnnouncementSourceAuthorizationRecheck recheck = Substitute.For<IAnnouncementSourceAuthorizationRecheck>();
        recheck.IsStillAuthorizedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        IOptions<AnnouncementSourceOptions> settings = Options.Create(new AnnouncementSourceOptions());
        var collector = new AnnouncementSourceCollector(collection, service, settings, TimeProvider.System, NullLogger<AnnouncementSourceCollector>.Instance);
        var runner = new AnnouncementSourceJobRunner(db.Sources, new MaintenanceProfileCatalog(settings), collector, recheck,
            settings, TimeProvider.System, NullLogger<AnnouncementSourceJobRunner>.Instance);
        foreach (string expected in new[] { "AccessDenied", "AnnouncementSourceAttemptsExhausted" })
        {
            AnnouncementSourceJob job = (await db.Sources.SubmitAsync(SourceTestDatabase.Job(draft), Guid.NewGuid().ToString("N"), "synthetic", default)).Job;
            if (expected.EndsWith("Exhausted", StringComparison.Ordinal))
            { await db.ExecuteAsync("UPDATE announcements.SourceJobs SET AttemptCount=3 WHERE JobId=@id", new { id = job.JobId }); }
            await runner.RunAsync(job.JobId, default);
            (await db.Sources.GetAsync(job.JobId, draft.OwnerId, default))!.ErrorCode.Should().Be(expected);
        }
        await collection.DidNotReceive().GetDevicesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private sealed class InterruptedDispatcher(string stage) : IAnnouncementSourceDispatcher
    {
        public bool IsConfigured => true;
        public string Stage { get; set; } = stage;
        public List<Guid> Enqueued { get; } = [];
        public string Enqueue(Guid jobId)
        {
            if (Stage == "before")
            { throw new IOException("Synthetic pre-enqueue failure."); }
            Enqueued.Add(jobId);
            if (Stage == "after")
            { throw new IOException("Synthetic post-enqueue failure."); }
            return Guid.NewGuid().ToString("N");
        }
    }
}

internal sealed class SourceTestDatabase
{
    public string Connection { get; }
    public IConfiguration Configuration { get; }
    public SqlAnnouncementStore Drafts { get; }
    public SqlAnnouncementSourceStore Sources { get; }

    public SourceTestDatabase()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SECUREOPS_SQL_TEST_CONNECTION"));
        if (connection.DataSource != "(localdb)\\SecureOpsResourcesV1" || !connection.InitialCatalog.StartsWith("SecureOps_ResourcesV1_OcoSource", StringComparison.Ordinal)
            || !connection.IntegratedSecurity || connection.UserID.Length > 0 || connection.Password.Length > 0)
        { throw new InvalidOperationException("An explicit fresh OcoSource LocalDB test database is required."); }
        Connection = connection.ConnectionString;
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SecureOpsDb"] = Connection }).Build();
        Drafts = new(Configuration);
        Sources = new(Configuration);
    }

    public async Task<AnnouncementDraft> DraftAsync()
    {
        var owner = Guid.NewGuid();
        await ExecuteAsync("INSERT INTO security.Users(UserId,CorporateIdentity,AuthenticationSource,AccessStatus) VALUES(@owner,@identity,'test','Approved')",
            new { owner, identity = "synthetic-source:" + owner });
        var draft = new AnnouncementDraft(Guid.NewGuid(), owner, 1, DateTimeOffset.UtcNow, Content(), "sender@example.invalid", "synthetic");
        (await Drafts.SaveAsync(draft, "synthetic", default)).Should().BeNull();
        return draft;
    }

    public static AnnouncementContent Content() => new("OCO-TEST", "Manual scope", "Synthetic source acceptance", "2026-09-14",
        "2026-09-15T01:00+03:00", "2026-09-15T02:00+03:00", "Manual description", "Manual impact", "Manual checks", "",
        ["manual@example.invalid"], [], "synthetic-v1");
    public static AnnouncementSourceJob Job(AnnouncementDraft draft) => new(Guid.NewGuid(), draft.OwnerId, draft.Id,
        "NonProd", "OCO-TEST", "Queued", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);
    public static AnnouncementSourceSnapshot Snapshot(AnnouncementSourceJob job) => new(job.JobId, job.DraftId, job.OwnerId,
        job.Profile, job.OcoReference, DateTimeOffset.UtcNow, "SYNTHETIC", [new("device-1", "SYNTHETIC", DateTimeOffset.UtcNow)],
        [new("service-1", ["device-1"], "Resolved", DateTimeOffset.UtcNow)], null, new(true, 1, 1, 1, 1, 0, 0, 0, false, []));
    public async Task<AnnouncementSourceJob> CompletedAsync(AnnouncementDraft draft)
    {
        AnnouncementSourceJob job = (await Sources.SubmitAsync(Job(draft), Guid.NewGuid().ToString("N"), "synthetic", default)).Job;
        SourceJobAttempt attempt = (await Sources.ClaimAsync(job.JobId, Guid.NewGuid(), 90, default))!;
        await Sources.CompleteAsync(job.JobId, attempt.AttemptId, "Succeeded", null, Snapshot(job), "synthetic", DateTimeOffset.UtcNow, default);
        return (await Sources.GetAsync(job.JobId, draft.OwnerId, default))!;
    }
    public async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(Connection);
        await connection.ExecuteAsync(sql, parameters, commandTimeout: 15);
    }
    public async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(Connection);
        return (await connection.ExecuteScalarAsync<T>(sql, parameters, commandTimeout: 15))!;
    }
    public Task<int> AuditCountAsync(Guid owner) => ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE Actor=@actor", new { actor = owner.ToString("D") });
    public async Task<string> FailureAsync(string table, string predicate)
    {
        string name = table.Split('.')[0] + ".[TR_SourceTest_" + Guid.NewGuid().ToString("N") + "]";
        await ExecuteAsync($"CREATE TRIGGER {name} ON {table} AFTER INSERT,UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE {predicate}) THROW 51199, 'Synthetic source persistence failure.', 1; END;");
        return name;
    }
}
