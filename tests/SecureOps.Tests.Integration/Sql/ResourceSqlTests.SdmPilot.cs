using System.Globalization;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.Identity;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task ExactPilot_RealDraftSqlAuditAndRestart_PreserveOneJiraOnlyResult()
    {
        IConfiguration configuration = Configuration();
        var repository = new SqlOperationalRecordRepository(configuration);
        string sourceId = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        var item = new OperationalRecordSourceItem(sourceId, "OR-" + sourceId,
            "Synthetic exact policy", "Synthetic provisioning outcome for tracking only",
            "synthetic.requester", null, "SIMULATION", null, null);
        var context = new OperationalRecordCommandContext(await SqlAccessTestActors.AdminAsync(configuration, "synthetic.publisher." + Guid.NewGuid().ToString("N")), Guid.NewGuid().ToString("N"), null);
        var audit = new InMemoryAuditWriter();
        OperationalRecord record = await repository.UpsertImportedAsync(item, context.CorrelationId, _token);
        record = await repository.EvaluateAsync(record.Id, SdmEvaluationEvidence.FromSource(item, true, true), context, audit, _token);
        var options = new OperationalRecordsOptions { SourceProvider = "TuruncuHat", ReadOnlyIntegrationMode = true };
        var jiraOptions = new JiraIntegrationOptions
        {
            ProjectKey = "TEST",
            IssueTypeId = "3",
            MappingVersion = "synthetic-exact-v1",
            TeamCustomField = "customfield_101",
            TeamValue = "Synthetic",
            RequesterWatcherCustomField = "customfield_102",
            Labels = ["synthetic-server"],
            ReporterMode = "AuthenticatedOperator",
            UnresolvedRequesterPolicy = "Block"
        };
        var sourceOptions = new TuruncuHatOptions { SourceBaseObject = "SMSS_oRFF", RelatedGroupId = 68, ExcludedDccIds = [4241] };
        options.Pilot = new()
        {
            RuleSetVersion = SdmPilotEvaluator.RuleSetVersion,
            SourceRecordId = sourceId,
            SourceFingerprint = SdmPilotPolicy.Fingerprint(record),
            SourceScope = "SMSS_oRFF:68:4241",
            ApprovalReference = "synthetic-local-decision",
            TrackingReason = "Track synthetic outcome",
            MappingVersion = jiraOptions.MappingVersion,
            RequestType = OperationalRecordClassification.ServerRequest,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        IOperationalRecordClient source = Substitute.For<IOperationalRecordClient>();
        source.GetByIdAsync(sourceId, Arg.Any<CancellationToken>()).Returns(item);
        IJiraUserResolver resolver = Substitute.For<IJiraUserResolver>();
        resolver.ResolveExactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => RequesterResolutionResult.Found(call.ArgAt<string>(0)));
        IJiraClient jira = Substitute.For<IJiraClient>();
        string key = "TEST" + sourceId + "-1";
        jira.CreateIssueAsync(Arg.Any<JiraIssueDraft>(), Arg.Any<CancellationToken>()).Returns(new JiraIssueCreationResult(key));
        JiraTransferService Service() => new(new SqlOperationalRecordRepository(configuration),
            new JiraIssueDraftService(resolver, new IdentityAccountNormalizer(Options.Create(new IdentityLookupOptions())),
                Options.Create(jiraOptions), Options.Create(options), Options.Create(sourceOptions)),
            jira, source, new SqlCommandIdempotencyStore(configuration), audit,
            Options.Create(options), Options.Create(new CommandIdempotencyOptions()), NullLogger<JiraTransferService>.Instance);

        // Corporate options select the real policy logic; only substitutes are constructed, never HTTP adapters.
        JiraTransferService service = Service();
        string fingerprint = options.Pilot.SourceFingerprint;
        options.Pilot.SourceFingerprint = new string('0', 64);
        (await service.PreviewAsync(record.Id, context, _token)).IsSuccess.Should().BeFalse();
        options.Pilot.SourceFingerprint = fingerprint;
        OperationalRecordResult<JiraIssueDraft> preview = await service.PreviewAsync(record.Id, context, _token);
        preview.IsSuccess.Should().BeTrue();
        preview.Value!.RequestType.Should().Be(OperationalRecordClassification.ServerRequest);
        preview.Value.ReviewOnly.Should().BeFalse();
        preview.Value.ReporterUsername.Should().Be(context.Actor);
        preview.Value.SourceCloseRequested.Should().BeFalse();
        (await service.CreateAsync(record.Id, context, _token)).Failure!.Code.Should().Be("ExternalWritesDisabled");
        options.ReadOnlyIntegrationMode = false;
        options.ControlledTestWritesEnabled = true;
        (await service.CreateAsync(record.Id, context, _token)).Value!.JiraIssueKey.Should().Be(key);
        options.SourceCloseEnabled = true;
        JiraTransferService restarted = Service();
        (await restarted.CreateAsync(record.Id, context, _token)).Value!.JiraIssueKey.Should().Be(key);
        (await restarted.RetryAsync(record.Id, context, _token)).Value!.JiraIssueKey.Should().Be(key);
        OperationalRecord stored = (await new SqlOperationalRecordRepository(configuration).GetAsync(record.Id, _token))!;
        stored.WorkflowState.Should().Be(OperationalRecordWorkflowState.JiraCreated);
        stored.SourceCloseRequested.Should().BeFalse();
        await jira.Received(1).CreateIssueAsync(Arg.Any<JiraIssueDraft>(), Arg.Any<CancellationToken>());
        await source.DidNotReceiveWithAnyArgs().CloseAsync(default!, default!, default!, default);
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId=@CorrelationId AND Action=@Action",
            new { context.CorrelationId, Action = SdmEvaluationEvidence.AuditAction })).Should().Be(2);
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditLog WHERE CorrelationId=@CorrelationId AND Actor=@Actor AND Action=@Action",
            new { context.CorrelationId, context.Actor, Action = SdmEvaluationEvidence.AuditAction })).Should().Be(1);
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ops.OperationalRecordWorkflowHistory WHERE OperationalRecordId=@Id AND WorkflowState='JiraCreated'",
            new { record.Id })).Should().Be(1);
    }
}
