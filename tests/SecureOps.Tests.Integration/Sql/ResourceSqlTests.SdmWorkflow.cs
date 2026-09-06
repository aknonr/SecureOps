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
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Integration.Sql;

public sealed partial class ResourceSqlTests
{
    [LocalResourceSqlFact]
    public async Task SdmCreate_KeyPersistenceFailure_RollsBackAndBlocksRecreationAfterRestart()
    {
        IConfiguration configuration = Configuration();
        var repository = new SqlOperationalRecordRepository(configuration);
        string unique = Guid.NewGuid().ToString("N");
        var source = new OperationalRecordSourceItem(unique, "OR-" + unique[..12], "Synthetic SDM persistence",
            "Synthetic only", "synthetic.requester", null, null, null, null);
        OperationalRecord record = await repository.UpsertImportedAsync(source, "synthetic", _token);
        await repository.SetClassificationAsync(record.Id, new(OperationalRecordClassification.OperationalSupport, true, "Synthetic test policy"), "synthetic", _token);
        var draft = new JiraIssueDraft(record.Id, record.OrCode, "TEST", "Task", source.Title, source.Description,
            "synthetic.requester", "synthetic-v1", new string('a', 32) + unique, [], new("3", "customfield_team", "WASAS", "customfield_requester", []));
        IJiraIssueDraftService drafts = Substitute.For<IJiraIssueDraftService>();
        drafts.BuildAsync(Arg.Any<OperationalRecord>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationalRecordResult<JiraIssueDraft>.Success(draft));
        IJiraClient jira = Substitute.For<IJiraClient>();
        jira.CreateIssueAsync(Arg.Any<JiraIssueDraft>(), Arg.Any<CancellationToken>()).Returns(new JiraIssueCreationResult("TEST" + unique.ToUpperInvariant() + "-1"));
        IOperationalRecordClient client = Substitute.For<IOperationalRecordClient>();
        client.GetByIdAsync(source.SourceRecordId, Arg.Any<CancellationToken>()).Returns(source);
        var context = new OperationalRecordCommandContext("synthetic-publisher", "synthetic-" + unique, null);
        JiraTransferService Service() => new(new SqlOperationalRecordRepository(configuration), drafts, jira, client,
            new SqlCommandIdempotencyStore(configuration), new InMemoryAuditWriter(),
            Options.Create(new OperationalRecordsOptions()), Options.Create(new CommandIdempotencyOptions()), NullLogger<JiraTransferService>.Instance);
        JiraTransferService service = Service();
        (await service.PreviewAsync(record.Id, context, _token)).IsSuccess.Should().BeTrue();
        await using SqlConnection connection = new(configuration.GetConnectionString("SecureOpsDb"));
        string trigger = "TR_SdmTest_" + unique;
        // A test-owned history failure exercises the real key/state/history transaction.
        await connection.ExecuteAsync($"CREATE TRIGGER ops.[{trigger}] ON ops.OperationalRecordWorkflowHistory AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE OperationalRecordId = '{record.Id:D}' AND WorkflowState = 'JiraCreated') THROW 51091, 'Synthetic persistence failure.', 1; END;");
        try
        {
            OperationalRecordResult<OperationalRecord> result = await service.CreateAsync(record.Id, context, _token);
            result.Failure!.Stage.Should().Be("jira-reconciliation");
            result.Failure.Retryable.Should().BeFalse();
            OperationalRecord persisted = (await repository.GetAsync(record.Id, _token))!;
            persisted.WorkflowState.Should().Be(OperationalRecordWorkflowState.CreatingJira);
            persisted.JiraIssueKey.Should().BeNull();
            (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ops.OperationalRecordWorkflowHistory WHERE OperationalRecordId=@Id AND WorkflowState='JiraCreated'", new { record.Id })).Should().Be(0);
            JiraTransferService restarted = Service();
            (await restarted.RetryAsync(record.Id, context, _token)).IsSuccess.Should().BeFalse();
            (await restarted.CreateAsync(record.Id, context with { IdempotencyKey = "new-command-" + unique }, _token)).IsSuccess.Should().BeFalse();
            await jira.Received(1).CreateIssueAsync(Arg.Any<JiraIssueDraft>(), Arg.Any<CancellationToken>());
            await client.DidNotReceive().CloseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            await connection.ExecuteAsync($"DROP TRIGGER ops.[{trigger}];");
        }
    }

    [LocalResourceSqlFact]
    public async Task SdmClaims_ConcurrentRepositories_OneOwnerAndDurableCommandReplay()
    {
        IConfiguration configuration = Configuration();
        var repository = new SqlOperationalRecordRepository(configuration);
        string unique = Guid.NewGuid().ToString("N");
        OperationalRecord record = await repository.UpsertImportedAsync(new(unique, "OR-" + unique[..12], "Synthetic concurrency", "Synthetic", null, null, null, null, null), "synthetic", _token);
        await repository.SetClassificationAsync(record.Id, new(OperationalRecordClassification.OperationalSupport, true, "Synthetic"), "synthetic", _token);
        WorkflowClaimResult[] claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => new SqlOperationalRecordRepository(configuration)
            .TryClaimAsync(record.Id, "synthetic-" + index, TimeSpan.FromMinutes(2), "synthetic", _token)));
        claims.Count(claim => claim.Disposition == WorkflowAcquireDisposition.Acquired).Should().Be(1);
        claims.Count(claim => claim.Disposition == WorkflowAcquireDisposition.AlreadyClaimed).Should().Be(3);
        var store = new SqlCommandIdempotencyStore(configuration);
        CommandBeginResult command = await store.TryBeginAsync("SyntheticSdm", unique, unique, "synthetic", TimeSpan.FromMinutes(2), _token);
        await store.CompleteAsync("SyntheticSdm", unique, unique, command.ExecutionToken!.Value, _token);
        CommandBeginResult replay = await new SqlCommandIdempotencyStore(configuration).TryBeginAsync("SyntheticSdm", unique, unique, "synthetic", TimeSpan.FromMinutes(2), _token);
        replay.Disposition.Should().Be(CommandBeginDisposition.Completed);
    }
}
