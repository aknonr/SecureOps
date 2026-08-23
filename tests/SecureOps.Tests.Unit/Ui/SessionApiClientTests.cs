using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Shared.Contracts.Sessions;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the application-session and integration-diagnostics client.
/// </summary>
public sealed class SessionApiClientTests
{
    [Fact]
    public async Task ActiveSessions_SendsBoundedPaging()
    {
        (SessionApiClient client, RecordingHandler handler) = Create(Page());

        await client.GetActiveAsync(2, 25, CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be("/api/v1/sessions/active");
        handler.LastUri!.Query.Should().Be("?page=2&pageSize=25");
    }

    [Fact]
    public async Task Revoke_SendsTheSessionIdAndReason()
    {
        var sessionId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        (SessionApiClient client, RecordingHandler handler) = Create(
            new ApplicationSessionEndedResponse(sessionId, "Revoked", DateTimeOffset.UnixEpoch));

        await client.RevokeAsync(sessionId, "Devredilen görev", CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be("/api/v1/sessions/revoke");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("sessionId").GetGuid().Should().Be(sessionId);
        body.RootElement.GetProperty("reason").GetString().Should().Be("Devredilen görev");
    }

    [Fact]
    public async Task SessionNotFound_IsSurfacedAsAConflictThatNeedsARefresh()
    {
        // The common case is a session that ended between the list rendering and the button being
        // pressed. It is a stale-view conflict, not an administrator error.
        (SessionApiClient client, _) = Create(Page(), HttpStatusCode.NotFound, "SessionNotFound");

        Func<Task> act = () => client.RevokeAsync(Guid.NewGuid(), "reason", CancellationToken.None);

        SecureOpsApiException exception = (await act.Should().ThrowAsync<SecureOpsApiException>()).Which;
        exception.Problem.Kind.Should().Be(UiProblemKind.Conflict);
        exception.Problem.RequiresRefresh.Should().BeTrue();
    }

    [Fact]
    public async Task RevokedCurrentSession_IsReportedAsASignInRequirement()
    {
        (SessionApiClient client, _) = Create(Page(), HttpStatusCode.Unauthorized, "SessionRevoked");

        Func<Task> act = () => client.GetCurrentAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<SecureOpsApiException>())
            .Which.Problem.RequiresSignIn.Should().BeTrue();
    }

    [Fact]
    public async Task IntegrationHealth_ReadsTheAdminDiagnosticsRoute()
    {
        (SessionApiClient client, RecordingHandler handler) = Create(
            new EnterpriseIntegrationHealthResponse(
                new IntegrationProviderHealthResponse("TuruncuHat", "Disabled", "Disabled"),
                new IntegrationProviderHealthResponse("Jira", "Fake", "Configured")));

        EnterpriseIntegrationHealthResponse health = await client.GetIntegrationHealthAsync(CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be("/api/v1/health/enterprise-integrations");
        health.TuruncuHat.Status.Should().Be("Disabled");
        health.Jira.Status.Should().Be("Configured");
    }

    [Fact]
    public async Task Forbidden_IsTranslatedRatherThanEscaping()
    {
        (SessionApiClient client, _) = Create(Page(), HttpStatusCode.Forbidden, "AccessDenied");

        Func<Task> act = () => client.GetIntegrationHealthAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<SecureOpsApiException>())
            .Which.Problem.Kind.Should().Be(UiProblemKind.Forbidden);
    }

    private static (SessionApiClient Client, RecordingHandler Handler) Create(
        object body,
        HttpStatusCode status = HttpStatusCode.OK,
        string? errorCode = null)
    {
        RecordingHandler handler = new(body, status, errorCode);
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        return (new SessionApiClient(httpClient), handler);
    }

    private static ActiveApplicationSessionsResponse Page() => new(2, 25, []);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _body;
        private readonly HttpStatusCode _status;
        private readonly string? _errorCode;

        public RecordingHandler(object body, HttpStatusCode status, string? errorCode)
        {
            _body = body;
            _status = status;
            _errorCode = errorCode;
        }

        public Uri? LastUri { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;

            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            HttpResponseMessage response = new(_status);
            response.Content = _status == HttpStatusCode.OK
                ? JsonContent.Create(_body, _body.GetType())
                : JsonContent.Create(new
                {
                    title = "Refused.",
                    status = (int)_status,
                    code = _errorCode,
                    correlationId = "00-test-01"
                });

            return response;
        }
    }
}
