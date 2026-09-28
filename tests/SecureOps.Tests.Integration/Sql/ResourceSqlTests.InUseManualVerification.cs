using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.InUse;
using SecureOps.Infrastructure.InUse.Execution;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task InUseActivity_VerifiedEvidence_IsTrackedAndReportedWithoutOverallClosure()
    {
        ExecutionFixture f = await ExecutionAsync();
        InUseExecutionIntent intent = f.Intent with { VerificationMode = "WasasActivityManual" };
        await f.Store.CreateAsync(intent, f.Bytes, _token);
        var transport = new ManualClosureTransport(new FixtureInUseCompletionTransport(f.Options, f.Policy), "Verified");
        await new InUseExecutionWorker(f.Store, transport, f.Policy, f.Options).RunAsync(intent.OperationId, _token);
        InUseExecution operation = (await f.Store.LatestAsync(f.Record.Id, _token))!;
        operation.State.Should().Be("Completed");
        InUseClosureVerification.ActivityVerified(operation).Should().BeTrue();
        InUseClosureVerification.SourceVerified(operation).Should().BeFalse();
        InUseClosureVerification.CanConfirm(operation).Should().BeFalse();
        var repository = new SqlInUseRepository(Configuration());
        (await repository.GetAsync(f.Record.Id, _token))!.TrackingOnly.Should().BeTrue();
        (await repository.QueryAsync(new(Search: f.Record.Source.Code, View: "verification"), intent.InitiatorId, _token)).Items.Should().NotContain(r => r.Id == f.Record.Id);
        (await repository.QueryAsync(new(Search: f.Record.Source.Code, View: "tracking"), intent.InitiatorId, _token)).Items.Should().Contain(r => r.Id == f.Record.Id);
        (await repository.QueryAsync(new(Search: f.Record.Source.Code, View: "review"), intent.InitiatorId, _token)).Items.Should().NotContain(r => r.Id == f.Record.Id);
        await using SqlConnection sql = new(Configuration().GetConnectionString("SecureOpsDb"));
        (await sql.ExecuteScalarAsync<bool>("SELECT Active FROM ops.InUseExecutions WHERE OperationId=@id", new { id = intent.OperationId })).Should().BeTrue();
        long version = await sql.ExecuteScalarAsync<long>("SELECT AccessVersion FROM security.Users WHERE UserId=@id", new { id = intent.InitiatorId });
        var actor = new ApplicationUser(intent.InitiatorId, "synthetic-report", "test", AccessStatus.Approved,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, version, [], [Capabilities.ManagementReportingView, Capabilities.InUseView]);
        var reports = new SqlWorkflowReportStore(Configuration());
        Guid snapshot = await reports.CaptureAsync(actor, "activity", new(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, IncludeSynthetic: true),
            true, false, false, "activity-report", _token);
        (await reports.ReadAsync(snapshot, actor, "activity", new(Metric: "InUse.ActivityVerified"), "activity-report", true, _token))!.Items.Should().ContainSingle(r => r.RecordId == f.Record.Id);
        (await reports.ReadAsync(snapshot, actor, "activity", new(Metric: "InUse.Closed"), "activity-report", true, _token))!.Items.Should().NotContain(r => r.RecordId == f.Record.Id);
        transport.Calls.Should().NotContain("Closure");
        (await f.Store.RecoverAsync(_token)).Should().NotContain(intent.OperationId);
    }

    [LocalResourceSqlFact]
    public async Task InUseManualClosure_AcknowledgementTimeoutAndRejection_StopWithoutReadbackOrReplay()
    {
        foreach ((string mode, string outcome) in new[] { "Manual", "WasasActivityManual" }
            .SelectMany(mode => new[] { "Acknowledged", "Timeout", "Rejected" }.Select(outcome => (mode, outcome))))
        {
            ExecutionFixture f = await ExecutionAsync();
            InUseExecutionIntent intent = f.Intent with { VerificationMode = mode };
            await f.Store.CreateAsync(intent, f.Bytes, _token);
            var transport = new ManualClosureTransport(new FixtureInUseCompletionTransport(f.Options, f.Policy), outcome);
            await new InUseExecutionWorker(f.Store, transport, f.Policy, f.Options).RunAsync(intent.OperationId, _token);
            var restarted = new SqlInUseExecutionStore(Configuration());
            InUseExecution operation = (await restarted.LatestAsync(f.Record.Id, _token))!;
            operation.State.Should().Be(outcome == "Timeout" ? "Unknown" : outcome == "Rejected" ? "Failed" : "Unconfirmed");
            operation.SourceCode.Should().Be(f.Record.Source.Code);
            operation.VerificationMode.Should().Be(mode);
            var repository = new SqlInUseRepository(Configuration());
            InUseRecord listed = (await repository.GetAsync(f.Record.Id, _token))!;
            listed.HasActiveExecution.Should().BeTrue();
            listed.ActivityStatus.Should().Be("VerificationPending");
            (listed with { Source = listed.Source with { WasasActivity = new("Pending", "Synthetic previous activity") } }).ActivityStatus.Should().Be("VerificationPending");
            (await repository.QueryAsync(new(Search: listed.Source.Code, View: "verification"), intent.InitiatorId, _token)).Items.Should().Contain(r => r.Id == listed.Id);
            (await repository.QueryAsync(new(Search: listed.Source.Code, View: "pending"), intent.InitiatorId, _token)).Items.Should().NotContain(r => r.Id == listed.Id);
            InUseClosureVerification.SourceVerified(operation).Should().BeFalse();
            InUseClosureVerification.CanConfirm(operation).Should().Be(outcome != "Rejected");
            transport.Calls.Should().Equal("Validate", "Property4463", "Property4464", "Upload", "Attachment", "Bpm");
            (await restarted.RecoverAsync(_token)).Should().NotContain(intent.OperationId);
            await new InUseExecutionWorker(restarted, transport, f.Policy, f.Options).RunAsync(intent.OperationId, _token);
            transport.Calls.Should().HaveCount(6);
            (await restarted.CreateAsync(intent with { OperationId = Guid.NewGuid() }, f.Bytes, _token)).Value!.OperationId.Should().Be(intent.OperationId);
            if (outcome == "Rejected")
            {
                (await restarted.ConfirmClosureAsync(f.Record.Id, intent.InitiatorId, "Operator",
                    new(intent.OperationId, operation.Revision, operation.SourceCode), _token)).Error.Should().Be("InUseConflict");
                continue;
            }
            ExecutionFixture confirmer = await ExecutionAsync();
            var request = new ConfirmInUseClosureRequest(intent.OperationId, operation.Revision, operation.SourceCode);
            (await restarted.ConfirmClosureAsync(f.Record.Id, confirmer.Intent.InitiatorId, "Other authorized operator",
                request with { SourceCode = "OR-WRONG" }, _token)).Error.Should().Be("InUseConflict");
            InUseResult<InUseExecution>[] confirmations = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => restarted.ConfirmClosureAsync(
                f.Record.Id, confirmer.Intent.InitiatorId, "Other authorized operator", request, _token)));
            confirmations.Count(r => r.Error is null).Should().Be(1);
            confirmations.Count(r => r.Error == "InUseConflict").Should().Be(1);
            InUseExecution confirmed = (await restarted.LatestAsync(f.Record.Id, _token))!;
            confirmed.State.Should().Be(operation.State);
            confirmed.InitiatorId.Should().Be(intent.InitiatorId);
            InUseStepEvidence manual = InUseClosureVerification.ManualConfirmation(confirmed)!;
            manual.Code.Should().Be(mode == "WasasActivityManual" ? "OperatorAttestedWasasActivityCompleted" : "OperatorAttestedSourceClosed");
            manual.ConfirmedBy.Should().Be(confirmer.Intent.InitiatorId);
            manual.ConfirmedByLabel.Should().Be("Other authorized operator");
            manual.At.Offset.Should().Be(TimeSpan.Zero);
            InUseClosureVerification.SourceVerified(confirmed).Should().BeFalse();
            InUseClosureVerification.CanConfirm(confirmed).Should().BeFalse();
            (await repository.GetAsync(f.Record.Id, _token))!.ActivityStatus.Should().Be("VerificationPending", "manual confirmation is not a source observation");
            await using SqlConnection sql = new(Configuration().GetConnectionString("SecureOpsDb"));
            (await sql.ExecuteScalarAsync<bool>("SELECT Active FROM ops.InUseExecutions WHERE OperationId=@id", new { id = intent.OperationId })).Should().BeTrue();
            string audit = await sql.QuerySingleAsync<string>("""
                SELECT DetailsJson FROM audit.AuditLog WHERE CorrelationId=@id AND Actor=@actor
                    AND JSON_VALUE(DetailsJson,'$.evidence.Step')='ManualVerification';
                """, new { id = intent.OperationId.ToString("D"), actor = confirmer.Intent.InitiatorId.ToString("D") });
            using var json = JsonDocument.Parse(audit);
            json.RootElement.GetProperty("InitiatorId").GetGuid().Should().Be(intent.InitiatorId);
            (await restarted.ClaimAsync(intent.OperationId, f.Policy.Fingerprint, "restart", _token)).Should().BeNull();
            long version = await sql.ExecuteScalarAsync<long>("SELECT AccessVersion FROM security.Users WHERE UserId=@id", new { id = intent.InitiatorId });
            var actor = new ApplicationUser(intent.InitiatorId, "synthetic-report", "test", AccessStatus.Approved,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, version, [], [Capabilities.ManagementReportingView, Capabilities.InUseView]);
            var reports = new SqlWorkflowReportStore(Configuration());
            Guid snapshot = await reports.CaptureAsync(actor, "manual", new(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, IncludeSynthetic: true),
                true, false, false, "manual-closure-report", _token);
            (await reports.ReadAsync(snapshot, actor, "manual", new(Metric: "InUse.Closed"), "manual-closure-report", true, _token))!.Items
                .Should().NotContain(x => x.RecordId == f.Record.Id);
            (await reports.ReadAsync(snapshot, actor, "manual", new(Metric: "InUse.Partial"), "manual-closure-report", true, _token))!.Items
                .Should().ContainSingle(x => x.RecordId == f.Record.Id);
        }
    }

    [LocalResourceSqlFact]
    public async Task InUseManualConfirmation_AuditFailureRollsBackAndRevocationDenies()
    {
        ExecutionFixture f = await ExecutionAsync();
        await f.Store.CreateAsync(f.Intent with { VerificationMode = "Manual" }, f.Bytes, _token);
        await new InUseExecutionWorker(f.Store, new FixtureInUseCompletionTransport(f.Options, f.Policy), f.Policy, f.Options)
            .RunAsync(f.Intent.OperationId, _token);
        InUseExecution operation = (await f.Store.LatestAsync(f.Record.Id, _token))!;
        var request = new ConfirmInUseClosureRequest(operation.OperationId, operation.Revision, operation.SourceCode);
        await using SqlConnection sql = new(Configuration().GetConnectionString("SecureOpsDb"));
        string trigger = "TR_ManualClosure_" + Guid.NewGuid().ToString("N");
        await sql.ExecuteAsync($"CREATE TRIGGER audit.[{trigger}] ON audit.AuditLog AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE CorrelationId='{operation.OperationId:D}') THROW 51220,'Synthetic audit failure.',1; END;");
        try
        {
            Func<Task> confirm = () => f.Store.ConfirmClosureAsync(f.Record.Id, f.Intent.InitiatorId, "Operator", request, _token);
            await confirm.Should().ThrowAsync<SqlException>();
            (await f.Store.LatestAsync(f.Record.Id, _token))!.Revision.Should().Be(operation.Revision);
        }
        finally { await sql.ExecuteAsync($"DROP TRIGGER audit.[{trigger}];"); }
        await sql.ExecuteAsync("UPDATE security.Users SET AccessStatus='Disabled' WHERE UserId=@id", new { id = f.Intent.InitiatorId });
        (await f.Store.ConfirmClosureAsync(f.Record.Id, f.Intent.InitiatorId, "Operator", request, _token)).Error.Should().Be("AccessDenied");
        InUseClosureVerification.ManualConfirmation((await f.Store.LatestAsync(f.Record.Id, _token))!).Should().BeNull();
    }

    private sealed class ManualClosureTransport(IInUseCompletionTransport inner, string outcome) : IInUseCompletionTransport
    {
        public List<string> Calls { get; } = [];
        public async Task<InUseRemoteResult> ExecuteAsync(string step, InUseExecutionLease execution, CancellationToken token)
        {
            Calls.Add(step);
            if (step == "Closure")
            { throw new InvalidOperationException("Deferred readback must not run."); }
            if (step == "Bpm" && outcome == "Rejected")
            { return new("Rejected", "SourceExplicitRejection"); }
            InUseRemoteResult result = await inner.ExecuteAsync(step, execution, token);
            if (step == "Bpm" && outcome == "Timeout")
            { throw new OperationCanceledException("Synthetic lost closure response."); }
            if (step == "Bpm" && outcome == "Verified" && result.Outcome == "Acknowledged")
            { return new("Verified", "SyntheticAuthoritativeActivityEvidence", result.RemoteId); }
            return result;
        }
    }
}
