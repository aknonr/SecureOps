using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class JiraTransferServiceTests
{
    private static readonly OperationalRecordCommandContext _context = new("test:publisher", "correlation-transfer", null);

    [Fact]
    public async Task CreateAsync_WhenRepeated_DoesNotCreateDuplicateJira()
    {
        TestFixture fixture = await TestFixture.CreateAsync();

        OperationalRecordResult<OperationalRecord> first = await fixture.Service.CreateAsync(fixture.RecordId, _context, CancellationToken.None);
        OperationalRecordResult<OperationalRecord> second = await fixture.Service.CreateAsync(fixture.RecordId, _context, CancellationToken.None);

        OperationalRecord firstRecord = first.Value!;
        firstRecord.WorkflowState.Should().Be(OperationalRecordWorkflowState.Completed);
        second.IsSuccess.Should().BeTrue();
        second.Value!.JiraIssueKey.Should().Be(firstRecord.JiraIssueKey);
        fixture.Jira.Calls.Should().Be(1);
        fixture.Audit.Events.Select(item => item.Action).Should().Contain([
            AuditActions.JiraCreateRequested,
            AuditActions.JiraCreated,
            AuditActions.OperationalRecordCloseRequested,
            AuditActions.WorkflowCompleted,
            AuditActions.JiraDuplicateCreatePrevented]);
    }

    [Fact]
    public async Task CreateAsync_RepeatedExplicitKey_ReplaysCompletedResult()
    {
        TestFixture fixture = await TestFixture.CreateAsync();
        OperationalRecordCommandContext context = _context with { IdempotencyKey = "browser-command-key-000001" };

        OperationalRecordResult<OperationalRecord> first = await fixture.Service.CreateAsync(fixture.RecordId, context, CancellationToken.None);
        OperationalRecordResult<OperationalRecord> replay = await fixture.Service.CreateAsync(fixture.RecordId, context, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        replay.IsSuccess.Should().BeTrue();
        replay.Value!.JiraIssueKey.Should().Be(first.Value!.JiraIssueKey);
        fixture.Jira.Calls.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenConcurrent_AllowsOnlyOneRemoteCreate()
    {
        BlockingJiraClient jira = new();
        TestFixture fixture = await TestFixture.CreateAsync(jira);

        Task<OperationalRecordResult<OperationalRecord>> firstTask = fixture.Service.CreateAsync(fixture.RecordId, _context, CancellationToken.None);
        await jira.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        OperationalRecordResult<OperationalRecord> second = await fixture.Service.CreateAsync(fixture.RecordId, _context, CancellationToken.None);
        jira.Release.TrySetResult();
        OperationalRecordResult<OperationalRecord> first = await firstTask;

        first.IsSuccess.Should().BeTrue();
        second.Failure!.Code.Should().Be(OperationalErrorCodes.WorkflowAlreadyInProgress);
        jira.Calls.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenSourceCloseFails_RetryResumesCloseWithoutCreatingJiraAgain()
    {
        CountingSourceClient source = new(failCloseCount: 1);
        TestFixture fixture = await TestFixture.CreateAsync(source: source);

        OperationalRecordResult<OperationalRecord> first = await fixture.Service.CreateAsync(fixture.RecordId, _context, CancellationToken.None);
        OperationalRecord afterFailure = (await fixture.Repository.GetAsync(fixture.RecordId, CancellationToken.None))!;
        OperationalRecordResult<OperationalRecord> retried = await fixture.Service.RetryAsync(fixture.RecordId, _context, CancellationToken.None);

        first.Failure!.Code.Should().Be(OperationalErrorCodes.OperationalRecordCloseFailed);
        afterFailure.WorkflowState.Should().Be(OperationalRecordWorkflowState.OperationalRecordCloseFailed);
        afterFailure.JiraIssueKey.Should().Be("TEST-100");
        OperationalRecord retriedRecord = retried.Value!;
        retriedRecord.WorkflowState.Should().Be(OperationalRecordWorkflowState.Completed);
        fixture.Jira.Calls.Should().Be(1);
        source.CloseCalls.Should().Be(2);
    }

    [Fact]
    public async Task RetryAsync_AfterPersistedJiraCreation_ClosesSourceWithoutCreatingJira()
    {
        TestFixture fixture = await TestFixture.CreateAsync();
        OperationalRecord record = (await fixture.Repository.GetAsync(fixture.RecordId, CancellationToken.None))!;
        string idempotencyKey = OperationalRecordIdempotency.Create(record.SourceRecordId, "mapping-v1");
        _ = await fixture.Repository.TryClaimAsync(record.Id, _context.Actor, TimeSpan.FromMinutes(2), _context.CorrelationId, CancellationToken.None);
        _ = await fixture.Repository.TryAcquireCreateAsync(record.Id, "mapping-v1", idempotencyKey, _context.Actor, _context.CorrelationId, CancellationToken.None);
        _ = await fixture.Repository.RecordJiraCreatedAsync(record.Id, "TEST-200", _context.Actor, _context.CorrelationId, CancellationToken.None);

        OperationalRecordResult<OperationalRecord> result = await fixture.Service.RetryAsync(fixture.RecordId, _context, CancellationToken.None);

        OperationalRecord completed = result.Value!;
        completed.WorkflowState.Should().Be(OperationalRecordWorkflowState.Completed);
        completed.JiraIssueKey.Should().Be("TEST-200");
        fixture.Jira.Calls.Should().Be(0);
        fixture.Source.CloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task RetryAsync_AfterUnknownJiraOutcome_BlocksDuplicateCreation()
    {
        CountingJiraClient jira = new(new ExternalIntegrationException(OperationalErrorCodes.JiraUnavailable, true, outcomeUnknown: true));
        TestFixture fixture = await TestFixture.CreateAsync(jira);

        OperationalRecordResult<OperationalRecord> first = await fixture.Service.CreateAsync(fixture.RecordId, _context, CancellationToken.None);
        OperationalRecordResult<OperationalRecord> retry = await fixture.Service.RetryAsync(fixture.RecordId, _context, CancellationToken.None);

        first.Failure!.Code.Should().Be(OperationalErrorCodes.JiraUnavailable);
        first.Failure.Retryable.Should().BeFalse();
        retry.Failure!.Code.Should().Be(OperationalErrorCodes.WorkflowAlreadyInProgress);
        jira.Calls.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenSourceChangedExternally_AbortsBeforeJira()
    {
        CountingSourceClient source = new();
        TestFixture fixture = await TestFixture.CreateAsync(source: source);
        source.ChangeExternally();

        OperationalRecordResult<OperationalRecord> result = await fixture.Service.CreateAsync(fixture.RecordId, _context, CancellationToken.None);

        result.Failure!.Code.Should().Be(OperationalErrorCodes.OperationalRecordChanged);
        fixture.Jira.Calls.Should().Be(0);
        fixture.Audit.Events.Select(item => item.Action).Should().Contain(AuditActions.OperationalRecordSourceChanged);
    }

    [Fact]
    public async Task CreateAsync_WhenSourceClosedExternally_AbortsBeforeJira()
    {
        CountingSourceClient source = new();
        TestFixture fixture = await TestFixture.CreateAsync(source: source);
        source.CloseExternally();

        OperationalRecordResult<OperationalRecord> result = await fixture.Service.CreateAsync(fixture.RecordId, _context, CancellationToken.None);

        result.Failure!.Code.Should().Be(OperationalErrorCodes.OperationalRecordNoLongerOpen);
        fixture.Jira.Calls.Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_SameExplicitKeyFromDifferentActor_IsRejected()
    {
        const string key = "browser-command-key-000001";
        TestFixture fixture = await TestFixture.CreateAsync();
        OperationalRecordCommandContext firstContext = _context with { IdempotencyKey = key };
        OperationalRecordCommandContext secondContext = new("test:other", "correlation-other", null, key);

        _ = await fixture.Service.CreateAsync(fixture.RecordId, firstContext, CancellationToken.None);
        OperationalRecordResult<OperationalRecord> second = await fixture.Service.CreateAsync(fixture.RecordId, secondContext, CancellationToken.None);

        second.Failure!.Code.Should().Be(OperationalErrorCodes.WorkflowAlreadyInProgress);
        fixture.Jira.Calls.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenCancelled_PropagatesCancellation()
    {
        TestFixture fixture = await TestFixture.CreateAsync();
        using CancellationTokenSource source = new();
        source.Cancel();

        Func<Task> act = () => fixture.Service.CreateAsync(fixture.RecordId, _context, source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Jira.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PreviewAsync_WhenMappingChangesForExistingTransfer_ReturnsConflict()
    {
        InMemoryOperationalRecordRepository repository = new();
        OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
        string firstKey = OperationalRecordIdempotency.Create(record.SourceRecordId, "mapping-v1");
        string secondKey = OperationalRecordIdempotency.Create(record.SourceRecordId, "mapping-v2");

        WorkflowAcquireResult first = await repository.MarkPreviewedAsync(record.Id, "mapping-v1", firstKey, _context.Actor, _context.CorrelationId, CancellationToken.None);
        WorkflowAcquireResult second = await repository.MarkPreviewedAsync(record.Id, "mapping-v2", secondKey, _context.Actor, _context.CorrelationId, CancellationToken.None);

        first.Disposition.Should().Be(WorkflowAcquireDisposition.Acquired);
        second.Disposition.Should().Be(WorkflowAcquireDisposition.Conflict);
        second.Record!.MappingVersion.Should().Be("mapping-v1");
    }

    [Fact]
    public async Task ReadOnlyIntegrationMode_CreateAndRetryFailBeforeWorkflowOrProviderWrites()
    {
        TestFixture fixture = await TestFixture.CreateAsync(readOnlyIntegrationMode: true);

        OperationalRecordResult<OperationalRecord> create = await fixture.Service.CreateAsync(
            fixture.RecordId,
            _context,
            CancellationToken.None);
        OperationalRecordResult<OperationalRecord> retry = await fixture.Service.RetryAsync(
            fixture.RecordId,
            _context,
            CancellationToken.None);
        OperationalRecord record = (await fixture.Repository.GetAsync(fixture.RecordId, CancellationToken.None))!;

        create.Failure!.Code.Should().Be(OperationalErrorCodes.ExternalWritesDisabled);
        create.Failure.Stage.Should().Be("external-write-fence");
        retry.Failure!.Code.Should().Be(OperationalErrorCodes.ExternalWritesDisabled);
        fixture.Jira.Calls.Should().Be(0);
        fixture.Source.CloseCalls.Should().Be(0);
        record.WorkflowState.Should().Be(OperationalRecordWorkflowState.Previewed);
    }

    private sealed class TestFixture
    {
        private TestFixture(
            InMemoryOperationalRecordRepository repository,
            CountingJiraClient jira,
            CountingSourceClient source,
            InMemoryAuditWriter audit,
            JiraTransferService service,
            Guid recordId)
        {
            Repository = repository;
            Jira = jira;
            Source = source;
            Audit = audit;
            Service = service;
            RecordId = recordId;
        }

        public InMemoryOperationalRecordRepository Repository { get; }
        public CountingJiraClient Jira { get; }
        public CountingSourceClient Source { get; }
        public InMemoryAuditWriter Audit { get; }
        public JiraTransferService Service { get; }
        public Guid RecordId { get; }

        public static async Task<TestFixture> CreateAsync(
            CountingJiraClient? jira = null,
            CountingSourceClient? source = null,
            bool readOnlyIntegrationMode = false)
        {
            InMemoryOperationalRecordRepository repository = new();
            OperationalRecord record = await TestRecord.SeedEligibleAsync(repository);
            jira ??= new CountingJiraClient();
            source ??= new CountingSourceClient();
            InMemoryAuditWriter audit = new();
            JiraIssueDraftService draftService = new(
                new ExactRequesterResolver(),
                Options.Create(new JiraIntegrationOptions
                {
                    ProjectKey = "TEST",
                    IssueType = "Task",
                    MappingVersion = "mapping-v1",
                    UnresolvedRequesterPolicy = "Block"
                }));
            JiraTransferService service = new(
                repository,
                draftService,
                jira,
                source,
                new InMemoryCommandIdempotencyStore(TimeProvider.System),
                audit,
                Options.Create(new OperationalRecordsOptions { ReadOnlyIntegrationMode = readOnlyIntegrationMode }),
                Options.Create(new CommandIdempotencyOptions()),
                NullLogger<JiraTransferService>.Instance);
            OperationalRecordResult<JiraIssueDraft> preview = await service.PreviewAsync(record.Id, _context, CancellationToken.None);
            preview.IsSuccess.Should().BeTrue();
            return new TestFixture(repository, jira, source, audit, service, record.Id);
        }
    }

    private sealed class ExactRequesterResolver : IRequesterResolver
    {
        public Task<RequesterResolutionResult> ResolveExactAsync(string requester, CancellationToken cancellationToken) =>
            Task.FromResult(RequesterResolutionResult.Found("jira-account-100"));
    }

    private class CountingJiraClient(Exception? failure = null) : IJiraClient
    {
        public int Calls { get; protected set; }

        public virtual Task<JiraIssueCreationResult> CreateIssueAsync(JiraIssueDraft draft, CancellationToken cancellationToken)
        {
            Calls++;
            return failure is null
                ? Task.FromResult(new JiraIssueCreationResult("TEST-100"))
                : Task.FromException<JiraIssueCreationResult>(failure);
        }
    }

    private sealed class BlockingJiraClient : CountingJiraClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<JiraIssueCreationResult> CreateIssueAsync(JiraIssueDraft draft, CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new JiraIssueCreationResult("TEST-100");
        }
    }

    private sealed class CountingSourceClient(int failCloseCount = 0) : IOperationalRecordClient
    {
        private int _remainingFailures = failCloseCount;
        private OperationalRecordSourceItem _current = TestRecord.SourceItem();
        public int CloseCalls { get; private set; }

        public Task<IReadOnlyList<OperationalRecordSourceItem>> GetActiveAsync(int maximumCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OperationalRecordSourceItem>>(Array.Empty<OperationalRecordSourceItem>());

        public Task<OperationalRecordSourceItem?> GetByIdAsync(string sourceRecordId, CancellationToken cancellationToken) =>
            Task.FromResult<OperationalRecordSourceItem?>(string.Equals(_current.SourceRecordId, sourceRecordId, StringComparison.OrdinalIgnoreCase) ? _current : null);

        public void ChangeExternally() => _current = _current with { Description = "Externally changed description." };

        public void CloseExternally() => _current = _current with { IsOpen = false };

        public Task CloseAsync(string sourceRecordId, string orCode, string jiraIssueKey, CancellationToken cancellationToken)
        {
            CloseCalls++;
            if (Interlocked.Decrement(ref _remainingFailures) >= 0)
            {
                throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordCloseFailed, true);
            }

            return Task.CompletedTask;
        }
    }
}
