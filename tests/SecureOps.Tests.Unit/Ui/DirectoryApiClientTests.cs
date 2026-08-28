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
    public async Task GroupLookup_WithoutAPurpose_SendsNull()
    {
        // The delta this proves: a read-only lookup is a valid request with no reason attached.
        (DirectoryApiClient client, RecordingHandler handler) = Create(GroupDetail());

        await client.GetGroupAsync("GRP-SECOPS", purpose: null, refresh: false, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("purpose").ValueKind.Should().Be(JsonValueKind.Null);
        body.RootElement.GetProperty("group").GetString().Should().Be("GRP-SECOPS");
    }

    [Fact]
    public async Task EnrichmentCall_WithoutAPurpose_SendsNull()
    {
        (DirectoryApiClient client, RecordingHandler handler) = Create(Health());

        await client.GetAccountHealthAsync("svc.app", purpose: null, refresh: false, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("purpose").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task PrivilegedMemberships_WithoutAPurpose_StillTargetsItsOwnRoute()
    {
        // Privileged analysis is gated by Identity.PrivilegedGroups.View, not by a supplied reason.
        // Omitting the purpose must not change which endpoint is called or how.
        (DirectoryApiClient client, RecordingHandler handler) = Create(Privileged());

        await client.GetPrivilegedMembershipsAsync("svc.app", null, false, CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be("/api/v1/directory/principals/privileged-memberships");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("purpose").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task SuppliedPurpose_IsSentExactlyAsGiven()
    {
        // The client transmits; trimming is the field's job, and the page normalizes before calling.
        (DirectoryApiClient client, RecordingHandler handler) = Create(Health());

        await client.GetAccountHealthAsync("svc.app", "Yetki incelemesi", false, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("purpose").GetString().Should().Be("Yetki incelemesi");
    }

    [Fact]
    public async Task GroupAnalysis_PostsTheGroupInTheBody()
    {
        (DirectoryApiClient client, RecordingHandler handler) = Create(AnalysisResponse());

        await client.AnalyzeGroupAsync("GRP-ORNEK", purpose: null, refresh: true, CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be("/api/v1/directory/groups/analysis");
        handler.LastUri!.Query.Should().BeEmpty();
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("group").GetString().Should().Be("GRP-ORNEK");
        body.RootElement.GetProperty("purpose").ValueKind.Should().Be(JsonValueKind.Null);
        body.RootElement.GetProperty("refresh").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GroupAnalysis_CarriesPartialCompletenessThrough()
    {
        // A bounded walk is HTTP 200 with isComplete=false. It must arrive as a result the UI can
        // mark partial, never as an exception that hides the evidence the server did return.
        (DirectoryApiClient client, _) = Create(AnalysisResponse(isComplete: false));

        DirectoryGroupAnalysisResponse analysis =
            await client.AnalyzeGroupAsync("GRP-ORNEK", null, false, CancellationToken.None);

        analysis.IsComplete.Should().BeFalse();
        analysis.DirectMembersIncludePrimaryGroupMembers.Should().BeFalse();
    }

    [Theory]
    [InlineData("DirectMembers")]
    [InlineData("EffectiveMembers")]
    public async Task Export_SendsTheRequestedModeAndPinsCsv(string mode)
    {
        // Format is pinned because the contract offers nothing else, and the two modes must never be
        // relabelled as one another — an operator exporting direct members must not receive the
        // expanded set.
        (DirectoryApiClient client, RecordingHandler handler) = Create(
            AnalysisResponse(), fileContent: "ad;soyad\r\n"u8.ToArray(), fileName: "uyeler.csv");

        DirectoryExportFile file = await client.ExportGroupAsync("GRP-ORNEK", mode, null, CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be("/api/v1/directory/groups/export");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("mode").GetString().Should().Be(mode);
        body.RootElement.GetProperty("format").GetString().Should().Be("Csv");

        file.FileName.Should().Be("uyeler.csv");
        file.Content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Export_RejectedPartialResult_IsTranslatedIntoAProblem()
    {
        // The server refuses to export partial effective membership. That refusal has to reach the
        // operator as an explanation, not as a silently empty file.
        (DirectoryApiClient client, _) = Create(
            AnalysisResponse(), HttpStatusCode.UnprocessableEntity, "DirectoryTraversalPartial");

        Func<Task> act = () => client.ExportGroupAsync(
            "GRP-ORNEK", "EffectiveMembers", null, CancellationToken.None);

        SecureOpsApiException exception = (await act.Should().ThrowAsync<SecureOpsApiException>()).Which;
        exception.Problem.Code.Should().Be("DirectoryTraversalPartial");
    }

    [Fact]
    public async Task ProviderTimeout_IsDistinctFromProviderUnavailable()
    {
        // A slow group is not an outage. Reporting one as the other sends an operator to the wrong
        // team and hides that a narrower query would have worked.
        (DirectoryApiClient client, _) = Create(
            AnalysisResponse(), HttpStatusCode.ServiceUnavailable, "DirectoryProviderTimeout");

        Func<Task> act = () => client.AnalyzeGroupAsync("GRP-ORNEK", null, false, CancellationToken.None);

        SecureOpsApiException exception = (await act.Should().ThrowAsync<SecureOpsApiException>()).Which;
        exception.Problem.Code.Should().Be("DirectoryProviderTimeout");
        exception.Problem.Title.Should().NotContain("ulaşılamıyor");
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
        bool throws = false,
        byte[]? fileContent = null,
        string? fileName = null)
    {
        RecordingHandler handler = new(body, status, errorCode, throws, fileContent, fileName);
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        return (new DirectoryApiClient(httpClient, new FakeApiSessionContext()), handler);
    }

    private static DirectoryAccountHealthResponse Health() =>
        new(true, false, null, null, null, null, null, null, true);

    private static DirectoryGroupDetailResponse GroupDetail() =>
        new(new DirectoryGroupDetailDto(
            null, "SecureOps Operators", "GRP-SECOPS", null, null, "Security", "Global", null, 0));

    private static DirectoryPrivilegedMembershipResponse Privileged() =>
        new([], Traversal());

    private static DirectoryServiceEvidenceResponse ServiceEvidence() =>
        new([], 0, false, null, null, null, null, "User", 0, 0, Traversal(), MembershipEvidenceAvailable: true);

    private static DirectoryTraversalMetadataDto Traversal() =>
        new(0, 0, 0, false, false, false, false, false, false);

    private static DirectoryGroupAnalysisResponse AnalysisResponse(bool isComplete = true) =>
        new(
            new DirectoryGroupDetailDto(null, "Ornek Operasyon", "GRP-ORNEK", null, null, "Security", "Global", null, 0),
            [], [], [], [], [],
            new DirectoryGroupParentMembershipsDto([], [], Traversal()),
            Traversal(),
            isComplete);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _body;
        private readonly HttpStatusCode _status;
        private readonly string? _errorCode;
        private readonly bool _throws;
        private readonly byte[]? _fileContent;
        private readonly string? _fileName;

        public RecordingHandler(
            object body, HttpStatusCode status, string? errorCode, bool throws,
            byte[]? fileContent = null, string? fileName = null)
        {
            _body = body;
            _status = status;
            _errorCode = errorCode;
            _throws = throws;
            _fileContent = fileContent;
            _fileName = fileName;
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

            // Export returns a file, not JSON, so the handler mirrors that shape when one is set.
            if (_status == HttpStatusCode.OK && _fileContent is not null && request.RequestUri!.AbsolutePath.EndsWith("/export", StringComparison.Ordinal))
            {
                ByteArrayContent file = new(_fileContent);
                file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
                file.Headers.ContentDisposition =
                    new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = _fileName };
                response.Content = file;
                return response;
            }

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
