using System.Security.Cryptography;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.InUse.Execution;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task InUseWorker_RemoteFailuresAndLostAcknowledgement_DoNotReplayOrClaimClosure()
    {
        foreach (string failure in new[] { "MissingActivity", "AmbiguousActivity", "UploadBefore", "UploadAfter", "Bpm", "Closure" })
        {
            ExecutionFixture f = await ExecutionAsync();
            await f.Store.CreateAsync(f.Intent, f.Bytes, _token);
            var transport = new FaultTransport(new FixtureInUseCompletionTransport(f.Options, f.Policy), failure);
            await new InUseExecutionWorker(f.Store, transport, f.Policy, f.Options).RunAsync(f.Intent.OperationId, _token);
            var restartedStore = new SqlInUseExecutionStore(Configuration());
            InUseExecution result = (await restartedStore.LatestAsync(f.Record.Id, _token))!;
            result.State.Should().Be(failure.StartsWith("Upload", StringComparison.Ordinal) ? "Unknown"
                : failure == "Closure" ? "Unconfirmed" : "Failed");
            int count = transport.Calls.Count;
            await new InUseExecutionWorker(restartedStore, transport, f.Policy, f.Options).RunAsync(f.Intent.OperationId, _token);
            transport.Calls.Should().HaveCount(count);
            if (failure is "MissingActivity" or "AmbiguousActivity")
            { transport.Calls.Should().Equal("Validate"); }
            if (failure.StartsWith("Upload", StringComparison.Ordinal))
            { transport.Calls.Should().NotContain("Bpm"); }
            if (failure is "UploadAfter" or "Bpm" or "Closure")
            {
                using var remote = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Directory.GetFiles(f.Options.Value.FixtureDirectory).Single()));
                remote.RootElement.GetProperty("Content").GetBytesFromBase64().Should().Equal(f.Bytes);
                remote.RootElement.GetProperty("Closed").GetBoolean().Should().Be(failure == "Closure");
            }
            if (failure is "Bpm" or "Closure")
            { result.Evidence.Should().Contain(e => e.Step == "Attachment" && e.Outcome == "Verified"); }
        }
    }

    private sealed class FaultTransport(IInUseCompletionTransport inner, string failure) : IInUseCompletionTransport
    {
        public List<string> Calls { get; } = [];
        public async Task<InUseRemoteResult> ExecuteAsync(string step, InUseExecutionLease execution, CancellationToken token)
        {
            Calls.Add(step);
            if (step == "Validate" && failure is "MissingActivity" or "AmbiguousActivity")
            { return new("Rejected", failure); }
            if (step == "Upload" && failure == "UploadBefore")
            { throw new HttpRequestException("Synthetic connection loss before remote acceptance."); }
            if (step == "Bpm" && failure == "Bpm")
            { return new("Rejected", "SyntheticBpmRejected"); }
            if (step == "Closure" && failure == "Closure")
            { return new("Unconfirmed", "AuthoritativeReadbackUnavailable"); }
            InUseRemoteResult result = await inner.ExecuteAsync(step, execution, token);
            if (step == "Upload" && failure == "UploadAfter")
            { throw new OperationCanceledException("Synthetic response loss after remote acceptance."); }
            return result;
        }
    }

    [LocalResourceSqlFact]
    public async Task InUseHistory_ReloadSearchAppendOnlyAndAuditRollback()
    {
        ExecutionFixture f = await ExecutionAsync();
        var repository = new SqlInUseRepository(Configuration());
        InUseRecord record = f.Record with
        {
            Version = f.Record.Version + 1,
            Draft = f.Record.Draft! with
            {
                Answers = f.Record.Draft.Answers.Select(a => a with
                {
                    Origin = new("Individual", AcceptedBy: f.Intent.InitiatorId,
                AcceptedByLabel: "Synthetic reviewer", AcceptedAt: DateTimeOffset.UtcNow)
                }).ToArray()
            }
        };
        var audit = new AuditEvent
        {
            Actor = f.Intent.InitiatorId.ToString("D"),
            Action = "InUseDraftSaved",
            Operation = new(Guid.NewGuid(), Guid.NewGuid(), "InUse", record.Id.ToString("D"), "InUseDraftSaved", f.Record.Version,
                DateTimeOffset.UtcNow, "Saved", new(f.Intent.InitiatorId, "Human", "Synthetic reviewer", null, null),
                new("SecureOps.Api", "synthetic-test"), null, "synthetic-history")
        };
        (await repository.SaveAsync(record, f.Record.Version, audit, _token)).Should().BeTrue();
        string key = InUseReviewHistory.Identity(record.Source, record.Source.Servers[0])!;
        (IReadOnlyList<InUseServerReview> rows, int total) = await new SqlInUseRepository(Configuration()).HistoryAsync(key, record.Source.Code, 1, 10, _token);
        total.Should().Be(1);
        rows.Single().Answers.Should().OnlyContain(a => a.Origin!.AcceptedBy == f.Intent.InitiatorId);
        (await repository.HistoryAsync(new string('0', 64), null, 1, 10, _token)).Total.Should().Be(0);
        await using SqlConnection sql = new(Configuration().GetConnectionString("SecureOpsDb"));
        Func<Task> alter = () => sql.ExecuteAsync("UPDATE ops.InUseServerReviews SET SearchText='changed' WHERE ReviewId=@id", new { id = rows.Single().Id });
        await alter.Should().ThrowAsync<SqlException>();
        string trigger = "TR_InUseReviewTest_" + Guid.NewGuid().ToString("N");
        await sql.ExecuteAsync($"CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Actor='{f.Intent.InitiatorId:D}') THROW 51220,'Synthetic history audit failure.',1; END;");
        try
        {
            Func<Task> save = () => repository.SaveAsync(record with { Version = record.Version + 1 }, record.Version, audit, _token);
            await save.Should().ThrowAsync<SqlException>();
            (await repository.GetAsync(record.Id, _token))!.Version.Should().Be(record.Version);
            (await repository.HistoryAsync(key, record.Source.Code, 1, 10, _token)).Total.Should().Be(1);
        }
        finally { await sql.ExecuteAsync($"DROP TRIGGER audit.[{trigger}];"); }
    }

    [LocalResourceSqlFact]
    public async Task InUseExecution_DurableOutbox_ConcurrentConfirmations_RestartAndExactArtifact()
    {
        ExecutionFixture f = await ExecutionAsync();
        InUseResult<InUseExecution>[] starts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => f.Store.CreateAsync(
            f.Intent with { OperationId = Guid.NewGuid() }, f.Bytes, _token)));
        starts.Should().OnlyContain(r => r.Error == null);
        Guid id = starts[0].Value!.OperationId;
        starts.Select(r => r.Value!.OperationId).Distinct().Should().ContainSingle();
        // No enqueue occurred. A new process/store recovers the committed intent from SQL.
        (await new SqlInUseExecutionStore(Configuration()).RecoverAsync(_token)).Should().Contain(id);
        var transport = new FixtureInUseCompletionTransport(f.Options, f.Policy);
        await new InUseExecutionWorker(f.Store, transport, f.Policy, f.Options).RunAsync(id, _token);
        InUseExecution result = (await f.Store.LatestAsync(f.Record.Id, _token))!;
        result.State.Should().Be("Completed");
        result.Evidence.Where(e => e.Outcome == "Verified").Select(e => e.Step).Should().Equal("Validate", "Attachment", "Closure");
        result.InitiatorId.Should().Be(f.Intent.InitiatorId);
        (await f.Store.ClaimAsync(id, f.Policy.Fingerprint, "second-worker", _token)).Should().BeNull();
        await using SqlConnection sql = new(Configuration().GetConnectionString("SecureOpsDb"));
        byte[] stored = await sql.QuerySingleAsync<byte[]>("SELECT Artifact FROM ops.InUseExecutions WHERE OperationId=@id", new { id });
        stored.Should().Equal(f.Bytes);
        Convert.ToHexString(SHA256.HashData(stored)).Should().Be(result.ReportSha256);
        string remote = Directory.GetFiles(f.Options.Value.FixtureDirectory).Single();
        using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(remote));
        json.RootElement.GetProperty("Content").GetBytesFromBase64().Should().Equal(f.Bytes);
        Func<Task> overwrite = () => sql.ExecuteAsync("UPDATE ops.InUseExecutions SET Artifact=0x01 WHERE OperationId=@id", new { id });
        await overwrite.Should().ThrowAsync<SqlException>();
    }

    [LocalResourceSqlFact]
    public async Task InUseExecution_ExpiredMutation_IsUnknown_StaleLeaseCannotAdvance_AndNoSecondCommandReplays()
    {
        ExecutionFixture f = await ExecutionAsync();
        (await f.Store.CreateAsync(f.Intent, f.Bytes, _token)).Error.Should().BeNull();
        InUseExecutionLease read = (await f.Store.ClaimAsync(f.Intent.OperationId, f.Policy.Fingerprint, "before-crash", _token))!;
        await f.Store.CompleteAsync(read, new("Verified", "fixture"), "before-crash", _token);
        InUseExecutionLease mutation = (await f.Store.ClaimAsync(f.Intent.OperationId, f.Policy.Fingerprint, "before-crash", _token))!;
        await using SqlConnection sql = new(Configuration().GetConnectionString("SecureOpsDb"));
        await sql.ExecuteAsync("UPDATE ops.InUseExecutions SET LeaseUntil=DATEADD(second,-1,SYSDATETIMEOFFSET()) WHERE OperationId=@id", new { id = f.Intent.OperationId });
        (await f.Store.RecoverAsync(_token)).Should().NotContain(f.Intent.OperationId);
        (await f.Store.CompleteAsync(mutation, new("Acknowledged", "late-success"), "before-crash", _token)).Should().BeFalse();
        (await f.Store.LatestAsync(f.Record.Id, _token))!.State.Should().Be("Unknown");
        InUseExecution replay = (await f.Store.CreateAsync(f.Intent with { OperationId = Guid.NewGuid() }, f.Bytes, _token)).Value!;
        replay.OperationId.Should().Be(f.Intent.OperationId);
        replay.State.Should().Be("Unknown");
    }

    [LocalResourceSqlFact]
    public async Task InUseExecution_RevocationAndSourceDrift_StopBeforeNextMutation_PreservePreviousEvidence()
    {
        foreach (string reason in new[] { "revoked", "source", "notObserved" })
        {
            ExecutionFixture f = await ExecutionAsync();
            await f.Store.CreateAsync(f.Intent, f.Bytes, _token);
            InUseExecutionLease read = (await f.Store.ClaimAsync(f.Intent.OperationId, f.Policy.Fingerprint, "worker", _token))!;
            await f.Store.CompleteAsync(read, new("Verified", "read-only-target-check"), "worker", _token);
            await using SqlConnection sql = new(Configuration().GetConnectionString("SecureOpsDb"));
            if (reason == "revoked")
            { await sql.ExecuteAsync("UPDATE security.RoleAssignments SET RevokedAt=SYSDATETIMEOFFSET() WHERE UserId=@id", new { id = f.Intent.InitiatorId }); }
            else
            {
                var repository = new SqlInUseRepository(Configuration());
                await repository.SaveAsync(f.Record with
                {
                    Version = f.Record.Version + 1,
                    SourceVersion = f.Record.SourceVersion + (reason == "source" ? 1 : 0),
                    SourceObservationMissing = reason == "notObserved"
                },
                    f.Record.Version, new AuditEvent { Actor = "synthetic", Action = "SyntheticSourceDrift" }, _token);
            }
            (await f.Store.ClaimAsync(f.Intent.OperationId, f.Policy.Fingerprint, "restarted-worker", _token)).Should().BeNull();
            InUseExecution result = (await f.Store.LatestAsync(f.Record.Id, _token))!;
            result.State.Should().Be("Blocked");
            result.Evidence.Should().Contain(e => e.Step == "Validate" && e.Outcome == "Verified");
            result.Evidence.Should().NotContain(e => e.Step == "Property4463" && e.Outcome == "Started");
        }
    }

    [LocalResourceSqlFact]
    public async Task InUseExecution_UploadUnknown_AndBpmFailureAndUnconfirmedClosure_AreSeparateDurableOutcomes()
    {
        foreach ((int step, string outcome, string state) in new[] { (3, "Unknown", "Unknown"), (5, "Rejected", "Failed"), (6, "Acknowledged", "Unconfirmed") })
        {
            ExecutionFixture f = await ExecutionAsync();
            await f.Store.CreateAsync(f.Intent, f.Bytes, _token);
            for (int index = 0; index <= step; index++)
            {
                InUseExecutionLease lease = (await f.Store.ClaimAsync(f.Intent.OperationId, f.Policy.Fingerprint, "worker", _token))!;
                string result = index == step ? outcome : index is 0 or 4 or 6 ? "Verified" : "Acknowledged";
                (await f.Store.CompleteAsync(lease, new(result, "synthetic-outcome", "exact-target"), "worker", _token)).Should().BeTrue();
                (await f.Store.CompleteAsync(lease, new(result, "duplicate"), "worker", _token)).Should().BeFalse();
            }
            InUseExecution execution = (await new SqlInUseExecutionStore(Configuration()).LatestAsync(f.Record.Id, _token))!;
            execution.State.Should().Be(state);
            (await f.Store.ClaimAsync(f.Intent.OperationId, f.Policy.Fingerprint, "restart", _token)).Should().BeNull();
            if (step > 3)
            { execution.Evidence.Should().Contain(e => e.Step == "Attachment" && e.Outcome == "Verified"); }
            if (step == 3)
            { execution.Evidence.Should().NotContain(e => e.Step == "Bpm"); }
        }
    }

    private static async Task<ExecutionFixture> ExecutionAsync()
    {
        await using SqlConnection sql = new(Configuration().GetConnectionString("SecureOpsDb"));
        SecureOps.Infrastructure.Resources.ResourceActor actor = await CreateActorAsync(sql);
        await sql.ExecuteAsync("""
            IF NOT EXISTS(SELECT 1 FROM security.Roles WHERE RoleId=32000)
                INSERT INTO security.Roles(RoleId,RoleCode,IsSeeded,CapabilitiesJson)
                VALUES(32000,'SyntheticInUseExecutor',0,'["InUse.View","InUse.Review","InUse.Complete"]');
            INSERT INTO security.RoleAssignments(UserId,RoleId,GrantedByCorporateIdentity) VALUES(@id,32000,'synthetic-isolated-test');
            """, new { id = actor.UserId });
        InUseSource source = (await new LocalInUseSourceClient().DiscoverAsync(_token)).Records[0];
        source = source with
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "OR-EXEC-" + Guid.NewGuid().ToString("N"),
            Servers = source.Servers.Select(s => s with
            {
                Fields = new Dictionary<string, InUseEvidence>(s.Fields)
                {
                    ["SI_ENVIRONMENT"] = new("DEV", "fixture"),
                    ["ITMC_Service_ID"] = new("0002915", "fixture"),
                    ["ITMC_Servis_Unsuru_ID"] = new("00112", "fixture")
                }
            }).ToArray()
        };
        var repository = new SqlInUseRepository(Configuration());
        var audit = new AuditEvent { Actor = actor.UserId.ToString("D"), Action = "SyntheticExecutorFixture" };
        await repository.RefreshAsync((await repository.StateAsync(_token)).Version, new([source], false), null, audit, _token);
        InUseRecord record = (await repository.QueryAsync(new(Search: source.Code), actor.UserId, _token)).Items.Single();
        InUseAnswer[] answers = source.Servers.SelectMany(s => InUseChecks.OperatorCodes.Select(c => new InUseAnswer(s.Id, c, "No", ""))).ToArray();
        InUseRecord reviewed = record with { Version = record.Version + 1, Draft = new(record.SourceVersion, answers, "", actor.UserId, DateTimeOffset.UtcNow) };
        await repository.SaveAsync(reviewed, record.Version, audit, _token);
        InUseReport report = InUseWorkbook.Create(reviewed, actor.UserId, DateTimeOffset.UtcNow);
        byte[] bytes = report.Content;
        IOptions<InUseCompletionOptions> options = Options.Create(new InUseCompletionOptions
        {
            Enabled = true,
            Provider = "Fixture",
            FixtureDirectory = Path.Combine(Path.GetTempPath(), "inuse-executor-" + Guid.NewGuid().ToString("N"))
        });
        var policy = new InUseCompletionPolicy(options, Options.Create(new OperationalRecordsOptions
        { ReadOnlyIntegrationMode = false, ControlledTestWritesEnabled = true, SourceCloseEnabled = true }), "Test");
        InUseExecutionIntent intent = new(Guid.NewGuid(), reviewed.Id, reviewed.Version, reviewed.SourceVersion, reviewed.SourceHash,
            Convert.ToHexString(SHA256.HashData(bytes)), report.FileName, actor.UserId, "Synthetic operator", DateTimeOffset.UtcNow,
            policy.Fingerprint, SqlInUseExecutionStore.ReviewHash(reviewed.Draft), source);
        return new(new(Configuration()), reviewed, intent, bytes, options, policy);
    }

    private sealed record ExecutionFixture(SqlInUseExecutionStore Store, InUseRecord Record, InUseExecutionIntent Intent,
        byte[] Bytes, IOptions<InUseCompletionOptions> Options, InUseCompletionPolicy Policy);
}
