using System.Net;
using System.Text.Json;
using FluentAssertions;
using SecureOps.Infrastructure.Resources;
using SecureOps.Shared.Contracts.Resources;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Pins the resource client's wire contract.
/// </summary>
/// <remarks>
/// The catalogue routes are filter-heavy and the personal routes are version-guarded, so the two
/// things worth holding are that filters reach the server in the documented shape and that the
/// aggregate version travels on every personal mutation. A dropped version silently overwrites
/// another tab's edit, which is the failure the whole optimistic scheme exists to prevent.
/// </remarks>
public sealed class ResourceApiClientTests
{
    [Fact]
    public async Task Query_SendsOnlySuppliedFilters()
    {
        // Empty filters are ignored by the contract, so sending them blank adds noise to the URL and
        // to any audit of what was actually asked for.
        (ResourceApiClient client, RecordingHandler handler) = Create(Page());

        await client.QueryLinksAsync(new ResourceQuery(Page: 2, PageSize: 25), CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be("/api/v1/resources/links");
        handler.LastUri.Query.Should().Contain("page=2").And.Contain("pageSize=25");
        handler.LastUri.Query.Should().NotContain("search=");
        handler.LastUri.Query.Should().NotContain("categoryId=");
        handler.LastUri.Query.Should().NotContain("includeArchived=");
    }

    [Fact]
    public async Task Query_EscapesFreeFormSearchText()
    {
        (ResourceApiClient client, RecordingHandler handler) = Create(Page());

        await client.QueryLinksAsync(new ResourceQuery(Search: "vm & host"), CancellationToken.None);

        handler.LastUri!.Query.Should().Contain("search=vm%20%26%20host");
    }

    [Fact]
    public async Task Query_CarriesEveryDocumentedFilter()
    {
        var categoryId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        (ResourceApiClient client, RecordingHandler handler) = Create(Page());

        await client.QueryLinksAsync(
            new ResourceQuery("dash", categoryId, "Test", "Lab", "sample", true, 3, 100),
            CancellationToken.None);

        string query = handler.LastUri!.Query;
        query.Should().Contain("search=dash");
        query.Should().Contain($"categoryId={categoryId}");
        query.Should().Contain("environment=Test");
        query.Should().Contain("location=Lab");
        query.Should().Contain("tag=sample");
        query.Should().Contain("includeArchived=true");
    }

    [Fact]
    public async Task Favourite_SendsTheIntentAndTheAggregateVersion()
    {
        var linkId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        (ResourceApiClient client, RecordingHandler handler) = Create(Preferences());

        await client.SetFavouriteAsync(linkId, true, 7, CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be($"/api/v1/resources/me/favourites/{linkId}");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("favourite").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("expectedVersion").GetInt64().Should().Be(7);
    }

    [Fact]
    public async Task DeleteSet_CarriesTheVersionAsAQueryValue()
    {
        var setId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        (ResourceApiClient client, RecordingHandler handler) = Create(Preferences());

        await client.DeleteSetAsync(setId, 4, CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be($"/api/v1/resources/me/sets/{setId}");
        handler.LastUri.Query.Should().Be("?expectedVersion=4");
        handler.LastMethod.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task SaveSet_PreservesTheSubmittedOpeningOrder()
    {
        var first = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("40000000-0000-0000-0000-000000000002");
        (ResourceApiClient client, RecordingHandler handler) = Create(Preferences());

        await client.SaveSetAsync(
            Guid.NewGuid(),
            new SaveShiftSetRequest("Sabah", [second, first], false, 2),
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body!);
        Guid[] sent = body.RootElement.GetProperty("linkIds")
            .EnumerateArray().Select(element => element.GetGuid()).ToArray();

        sent.Should().Equal(second, first);
    }

    [Fact]
    public async Task Resolve_UsesTheDocumentedRoute()
    {
        var setId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        (ResourceApiClient client, RecordingHandler handler) = Create(
            new ShiftSetResponse(setId, "Sabah", [], false));

        await client.ResolveSetAsync(setId, CancellationToken.None);

        handler.LastUri!.AbsolutePath.Should().Be($"/api/v1/resources/me/sets/{setId}/resolve");
    }

    [Fact]
    public async Task StaleVersion_IsSurfacedAsARefreshableConflict()
    {
        // The operator must re-read before trying again; the UI must never resubmit over the edit
        // that won.
        (ResourceApiClient client, _) = Create(
            Preferences(), HttpStatusCode.Conflict, ResourceErrors.Conflict);

        Func<Task> act = () => client.SetFavouriteAsync(Guid.NewGuid(), true, 1, CancellationToken.None);

        SecureOpsApiException exception = (await act.Should().ThrowAsync<SecureOpsApiException>()).Which;
        exception.Problem.Kind.Should().Be(UiProblemKind.Conflict);
        exception.Problem.RequiresRefresh.Should().BeTrue();
    }

    [Fact]
    public async Task NotFound_DoesNotSpeculateAboutVisibility()
    {
        // Missing and not-visible are deliberately indistinguishable; saying "no permission" would
        // reveal that a restricted entry exists.
        (ResourceApiClient client, _) = Create(
            Page(), HttpStatusCode.NotFound, ResourceErrors.NotFound);

        Func<Task> act = () => client.GetLinkAsync(Guid.NewGuid(), false, CancellationToken.None);

        SecureOpsApiException exception = (await act.Should().ThrowAsync<SecureOpsApiException>()).Which;
        exception.Problem.Kind.Should().Be(UiProblemKind.NotFound);
        exception.Problem.Explanation.Should().NotContain("yetki");
    }

    [Fact]
    public async Task ValidationFailure_IsReportedWithoutEchoingSubmittedContent()
    {
        (ResourceApiClient client, _) = Create(
            Page(), HttpStatusCode.BadRequest, ResourceErrors.Invalid);

        Func<Task> act = () => client.CreateLinkAsync(
            new SaveResourceLinkRequest(Guid.NewGuid(), "n", "https://example.invalid/", "p"),
            CancellationToken.None);

        SecureOpsApiException exception = (await act.Should().ThrowAsync<SecureOpsApiException>()).Which;
        exception.Problem.Kind.Should().Be(UiProblemKind.Validation);
        exception.Problem.Retryable.Should().BeFalse();
    }

    private static ResourcePage Page() => new([], 1, 50, 0);

    private static ResourcePreferencesResponse Preferences() => new(0, [], [], null);

    private static (ResourceApiClient Client, RecordingHandler Handler) Create(
        object body,
        HttpStatusCode status = HttpStatusCode.OK,
        string? errorCode = null)
    {
        RecordingHandler handler = new(body, status, errorCode);
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://localhost:5001/") };
        return (new ResourceApiClient(httpClient, new FakeApiSessionContext()), handler);
    }

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

        public HttpMethod? LastMethod { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastMethod = request.Method;

            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            object payload = _errorCode is null
                ? _body
                : new { code = _errorCode, correlationId = "corr-1", retryable = _status == HttpStatusCode.Conflict };

            return new HttpResponseMessage(_status)
            {
                Content = System.Net.Http.Json.JsonContent.Create(payload, options: ApiResponseReader.JsonOptions)
            };
        }
    }
}
