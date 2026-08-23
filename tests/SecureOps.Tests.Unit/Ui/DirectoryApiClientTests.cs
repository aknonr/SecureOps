using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SecureOps.Shared.Contracts.Directory;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins what the Directory Explorer client actually sends.
/// </summary>
/// <remarks>
/// The account and group being investigated must travel in the request body, never in the URL.
/// A query string ends up in browser history, proxy logs, and referrer headers, which is exactly the
/// disclosure an audited exact-lookup design exists to avoid — and it is invisible from the client's
/// own types, so it is asserted here.
/// </remarks>
public sealed class DirectoryApiClientTests
{
    [Fact]
    public async Task PrincipalGroups_PostsTheAccountInTheBodyAndNotTheUrl()
    {
        (DirectoryApiClient client, RecordingHandler handler) = Create(new DirectoryGroupPageResponse([], 50, null));

        await client.GetPrincipalGroupsAsync("DOM\\svc.app", "Yetki incelemesi", 25, "tok", CancellationToken.None);

        handler.LastUri!.PathAndQuery.Should().Be("/api/v1/directory/principals/groups");
        handler.LastUri!.Query.Should().BeEmpty();
        handler.Body.Should().Contain("svc.app");
        handler.Body.Should().Contain("Yetki incelemesi");
        handler.Body.Should().Contain("tok");
    }

    [Fact]
    public async Task MembershipPaths_SendsBothTheAccountAndTheTargetGroup()
    {
        (DirectoryApiClient client, RecordingHandler handler) = Create(
            new DirectoryMembershipPathResponse(false, false, [], false, Traversal()));

        await client.GetMembershipPathsAsync(
            "DOM\\user", "GRP-TARGET", "Olay incelemesi", refresh: true, CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be("/api/v1/directory/principals/membership-paths");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("account").GetString().Should().Be("DOM\\user");
        body.RootElement.GetProperty("targetGroup").GetString().Should().Be("GRP-TARGET");
        body.RootElement.GetProperty("refresh").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("account-health", "/api/v1/directory/principals/account-health")]
    [InlineData("service-evidence", "/api/v1/directory/principals/service-evidence")]
    public async Task EnrichmentCalls_TargetTheirOwnRoute(string _, string expectedPath)
    {
        (DirectoryApiClient client, RecordingHandler handler) = Create(Health());

        if (expectedPath.EndsWith("account-health", StringComparison.Ordinal))
        {
            await client.GetAccountHealthAsync("a", "p", false, CancellationToken.None);
        }
        else
        {
            (client, handler) = Create(ServiceEvidence());
            await client.GetServiceEvidenceAsync("a", "p", false, CancellationToken.None);
        }

        handler.LastUri!.AbsolutePath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task GroupMembers_PostsTheContinuationTokenWithoutInterpretingIt()
    {
        // The token is opaque and server-protected. It is echoed back exactly, never parsed or built.
        const string token = "eyJvIjoxMDB9.signature-part";
        (DirectoryApiClient client, RecordingHandler handler) = Create(new DirectoryMemberPageResponse([], 50, null));

        await client.GetGroupMembersAsync("GRP", "Denetim", null, token, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("continuationToken").GetString().Should().Be(token);
    }

    [Fact]
    public async Task NotFound_IsTranslatedIntoAProblemRatherThanEscaping()
    {
        (DirectoryApiClient client, _) = Create(
            Health(), HttpStatusCode.NotFound, "DirectoryPrincipalNotFound");

        Func<Task> act = () => client.GetAccountHealthAsync("a", "p", false, CancellationToken.None);

        (await act.Should().ThrowAsync<SecureOpsApiException>())
            .Which.Problem.Kind.Should().Be(UiProblemKind.NotFound);
    }

    [Fact]
    public async Task QueryLimitExceeded_IsSurfacedAsAValidationProblem()
    {
        (DirectoryApiClient client, _) = Create(
            Health(), HttpStatusCode.BadRequest, "DirectoryQueryLimitExceeded");

        Func<Task> act = () => client.GetMembershipsAsync("a", "p", false, CancellationToken.None);

        SecureOpsApiException exception = (await act.Should().ThrowAsync<SecureOpsApiException>()).Which;
        exception.Problem.Code.Should().Be("DirectoryQueryLimitExceeded");
        exception.Problem.Kind.Should().Be(UiProblemKind.Validation);
    }

    [Fact]
    public async Task TransportFailure_IsTranslatedIntoANetworkProblem()
    {
        (DirectoryApiClient client, _) = Create(Health(), throws: true);

        Func<Task> act = () => client.GetGroupAsync("GRP", "p", false, CancellationToken.None);

        (await act.Should().ThrowAsync<SecureOpsApiException>())
            .Which.Problem.Kind.Should().Be(UiProblemKind.Network);
    }

    private static (DirectoryApiClient Client, RecordingHandler Handler) Create(
        object body,
        HttpStatusCode status = HttpStatusCode.OK,
        string? errorCode = null,
        bool throws = false)
    {
        RecordingHandler handler = new(body, status, errorCode, throws);
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        return (new DirectoryApiClient(httpClient), handler);
    }

    private static DirectoryAccountHealthResponse Health() =>
        new(true, false, null, null, null, null, null, null, true);

    private static DirectoryServiceEvidenceResponse ServiceEvidence() =>
        new([], 0, false, null, null, null, null, "User", 0, 0, Traversal());

    private static DirectoryTraversalMetadataDto Traversal() =>
        new(0, 0, 0, false, false, false, false, false, false);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _body;
        private readonly HttpStatusCode _status;
        private readonly string? _errorCode;
        private readonly bool _throws;

        public RecordingHandler(object body, HttpStatusCode status, string? errorCode, bool throws)
        {
            _body = body;
            _status = status;
            _errorCode = errorCode;
            _throws = throws;
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

            if (_throws)
            {
                throw new HttpRequestException("connection refused");
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
