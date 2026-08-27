using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Reporting;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Tests.Unit.Reporting;

public sealed class ManagementReportingServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly ManagementReportingContext _context = new("CONTOSO\\auditor", "report-correlation", "192.0.2.10");

    [Fact]
    public async Task GetSummaryAsync_EmptyRepository_ReturnsZeroReportAndAuditsPrivilegedRead()
    {
        StubRepository repository = new();
        InMemoryAuditWriter audit = new();
        ManagementReportingService service = Service(repository, audit);

        ManagementReportingResult<ManagementReportResponse> result = await service.GetSummaryAsync("7d", null, null, _context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IdentityLookup.TotalLookups.Should().Be(0);
        audit.Events.Select(item => item.Action).Should().ContainInOrder(
            AuditActions.ManagementReportRequested,
            AuditActions.ManagementReportViewed);
        repository.SummaryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetOperatorsAsync_ValidLargePage_PreservesServerSidePagination()
    {
        StubRepository repository = new()
        {
            OperatorPage = new OperatorActivityDataPage(250, [], _now.AddDays(-40))
        };
        ManagementReportingService service = Service(repository, new InMemoryAuditWriter());

        ManagementReportingResult<OperatorActivityPageResponse> result = await service.GetOperatorsAsync("30d", null, null, 3, 100, _context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(3);
        result.Value.PageSize.Should().Be(100);
        result.Value.TotalItems.Should().Be(250);
        repository.LastPage.Should().Be(3);
        repository.LastPageSize.Should().Be(100);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetOperatorsAsync_InvalidPagination_DoesNotReadRepository(int page, int pageSize)
    {
        StubRepository repository = new();
        ManagementReportingService service = Service(repository, new InMemoryAuditWriter());

        ManagementReportingResult<OperatorActivityPageResponse> result = await service.GetOperatorsAsync("7d", null, null, page, pageSize, _context, CancellationToken.None);

        result.ErrorCode.Should().Be(OperationalErrorCodes.ReportingValidationFailed);
        repository.OperatorCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenInitialAuditFails_DoesNotReadRepository()
    {
        StubRepository repository = new();
        ManagementReportingService service = Service(repository, new ThrowingAuditWriter());

        ManagementReportingResult<ManagementReportResponse> result = await service.GetSummaryAsync("7d", null, null, _context, CancellationToken.None);

        result.ErrorCode.Should().Be(OperationalErrorCodes.AuditStoreUnavailable);
        repository.SummaryCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenRepositoryFails_ReturnsStableUnavailableCode()
    {
        StubRepository repository = new() { Failure = new ReportingUnavailableException("synthetic unavailable") };
        InMemoryAuditWriter audit = new();
        ManagementReportingService service = Service(repository, audit);

        ManagementReportingResult<ManagementReportResponse> result = await service.GetSummaryAsync("7d", null, null, _context, CancellationToken.None);

        result.ErrorCode.Should().Be(OperationalErrorCodes.ReportingUnavailable);
        audit.Events.Select(item => item.Action).Should().Contain(AuditActions.ManagementReportFailed);
    }

    [Fact]
    public async Task GetSummaryAsync_WhenPersistenceIsNotConfigured_ReturnsDistinctStableCode()
    {
        StubRepository repository = new()
        {
            Failure = new ReportingPersistenceNotConfiguredException("synthetic not configured")
        };
        ManagementReportingService service = Service(repository, new InMemoryAuditWriter());

        ManagementReportingResult<ManagementReportResponse> result = await service.GetSummaryAsync(
            "7d", null, null, _context, CancellationToken.None);

        result.ErrorCode.Should().Be(OperationalErrorCodes.ReportingPersistenceNotConfigured);
    }

    private static ManagementReportingService Service(IManagementReportingRepository repository, IAuditWriter auditWriter) =>
        new(
            new ReportingWindowResolver(new FixedTimeProvider(_now)),
            repository,
            new ManagementReportProjector(),
            auditWriter,
            NullLogger<ManagementReportingService>.Instance);

    private sealed class StubRepository : IManagementReportingRepository
    {
        public int SummaryCalls { get; private set; }
        public int OperatorCalls { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }
        public Exception? Failure { get; init; }
        public OperatorActivityDataPage OperatorPage { get; init; } = new(0, [], null);

        public Task<ManagementReportingData> GetSummaryAsync(ReportingWindow window, CancellationToken cancellationToken)
        {
            SummaryCalls++;
            if (Failure is not null)
            {
                return Task.FromException<ManagementReportingData>(Failure);
            }

            return Task.FromResult(new ManagementReportingData(
                [], [], 0, new ReportingActiveUsers(0, 0, 0, 0), 0,
                new ReportingRetryOutcomes(0, 0, 0), [], null));
        }

        public Task<OperatorActivityDataPage> GetOperatorActivityAsync(
            ReportingWindow window,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            OperatorCalls++;
            LastPage = page;
            LastPageSize = pageSize;
            return Task.FromResult(OperatorPage);
        }
    }

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("synthetic audit failure"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
