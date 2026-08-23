using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SecureOps.Shared.Contracts.Reporting;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the request the reporting client actually sends.
/// </summary>
/// <remarks>
/// The query string is the whole contract surface for these two endpoints, and one of its rules is
/// invisible from the client's own types: a preset must arrive with no bounds at all, because the
/// server rejects a named window that carries <c>from</c> or <c>to</c>. That is exactly the kind of
/// thing a refactor breaks silently, so it is asserted here rather than left to a manual check.
/// </remarks>
public sealed class ManagementReportingApiClientTests
{
    [Fact]
    public async Task Summary_ForAPreset_SendsNoBounds()
    {
        (ManagementReportingApiClient client, RecordingHandler handler) = Create();

        await client.GetSummaryAsync(
            ReportingWindowSelection.ForPreset(ReportingWindowPreset.Last30Days),
            CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be("/api/v1/reporting/management/summary");
        handler.LastUri!.Query.Should().Be("?window=30d");
    }

    [Fact]
    public async Task Summary_ForACustomWindow_SendsRoundTripUtcBounds()
    {
        (ManagementReportingApiClient client, RecordingHandler handler) = Create();
        ReportingWindowRequest window = ReportingWindowSelection.ForCustom(
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 3),
            new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero));

        await client.GetSummaryAsync(window, CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.LastUri!.Query);
        query.Should().StartWith("?window=custom");
        query.Should().Contain("from=2026-03-01T00:00:00.0000000Z");
        query.Should().Contain("to=2026-03-04T00:00:00.0000000Z");
    }

    [Fact]
    public async Task Operators_AppendsPagingToTheWindowQuery()
    {
        (ManagementReportingApiClient client, RecordingHandler handler) = Create(Page());

        await client.GetOperatorsAsync(
            ReportingWindowSelection.ForPreset(ReportingWindowPreset.Today),
            3,
            25,
            CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be("/api/v1/reporting/management/operators");
        handler.LastUri!.Query.Should().Be("?window=today&page=3&pageSize=25");
    }

    [Fact]
    public async Task Forbidden_IsTranslatedIntoAProblemRatherThanEscaping()
    {
        // A management screen must be able to render a refusal. An HttpRequestException reaching a
        // component would tear down the circuit instead.
        (ManagementReportingApiClient client, _) = Create(status: HttpStatusCode.Forbidden);

        Func<Task> act = () => client.GetSummaryAsync(
            ReportingWindowSelection.ForPreset(ReportingWindowPreset.Today),
            CancellationToken.None);

        (await act.Should().ThrowAsync<SecureOpsApiException>())
            .Which.Problem.Kind.Should().Be(UiProblemKind.Forbidden);
    }

    [Fact]
    public async Task TransportFailure_IsTranslatedIntoANetworkProblem()
    {
        (ManagementReportingApiClient client, _) = Create(throws: true);

        Func<Task> act = () => client.GetSummaryAsync(
            ReportingWindowSelection.ForPreset(ReportingWindowPreset.Today),
            CancellationToken.None);

        (await act.Should().ThrowAsync<SecureOpsApiException>())
            .Which.Problem.Kind.Should().Be(UiProblemKind.Network);
    }

    private static (ManagementReportingApiClient Client, RecordingHandler Handler) Create(
        object? body = null,
        HttpStatusCode status = HttpStatusCode.OK,
        bool throws = false)
    {
        RecordingHandler handler = new(body ?? Report(), status, throws);
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        return (new ManagementReportingApiClient(httpClient), handler);
    }

    private static ManagementReportResponse Report() => new(
        new ReportingWindowResponse("7d", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(7)),
        new IdentityLookupMetricsResponse(0, 0, 0, 0, 0, 0, 0, []),
        new OperationalWorkflowMetricsResponse(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new RetryOutcomeMetricsResponse(0, 0, 0, 0), []),
        new PlatformAdoptionMetricsResponse(0, 0, 0, 0, [], []),
        new SecurityQualityMetricsResponse(0, 0, 0, 0, null),
        []);

    private static OperatorActivityPageResponse Page() => new(
        new ReportingWindowResponse("today", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)),
        3,
        25,
        0,
        []);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _body;
        private readonly HttpStatusCode _status;
        private readonly bool _throws;

        public RecordingHandler(object body, HttpStatusCode status, bool throws)
        {
            _body = body;
            _status = status;
            _throws = throws;
        }

        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;

            if (_throws)
            {
                throw new HttpRequestException("connection refused");
            }

            HttpResponseMessage response = new(_status);
            if (_status == HttpStatusCode.OK)
            {
                response.Content = JsonContent.Create(_body, _body.GetType());
            }

            return Task.FromResult(response);
        }
    }
}
