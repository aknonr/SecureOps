using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SecureOps.Domain.Announcements;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

public sealed class AnnouncementUiTests
{
    private static AnnouncementContent Content() => new("OCO-SYNTHETIC", "Kapsam", "Şule & Işık", "2026-09-12",
        "2026-09-13T01:00+03:00", "2026-09-13T02:00+03:00", "&lt;b&gt; <script>test</script>", "Etki", "Kontrol", "",
        ["reader@example.invalid"], [], "synthetic-v1");
    [Fact]
    public void Form_RoundTripAndConflictComparisonPreserveLiteralTextAndRecipients()
    {
        var form = AnnouncementForm.From(Content());
        form.Content().Should().BeEquivalentTo(Content());
        form.Values["Subject"] = "Yeni & konu";
        form.Recipients.Add(new("Cc", "copy@example.invalid"));
        form.Differences(Content()).Select(d => d.Label).Should().Equal("Konu", "Bilgi");
        form.Content().Description.Should().Be(Content().Description);
        AnnouncementContent final = Content() with { TemplateRevision = "oco-table-v2", AffectedServices = ["Servis & <b>", "İkinci servis"] };
        AnnouncementForm.From(final).Content().Should().BeEquivalentTo(final);
        form.Differences(final).Select(d => d.Label).Should().Contain(["Duyuru biçimi", "Etkilenen servisler"]);
        ResourceGuideSteps.For("announcements", false).Should().HaveCount(5);
        ResourceGuideSteps.For("announcements", false).Should().OnlyContain(s => s.Href == "");
    }
    [Fact]
    public async Task Client_SaveCarriesVersionAndStrictEtag_ConflictNeverRetries()
    {
        var id = Guid.NewGuid();
        using var handler = new Handler(r =>
        {
            r.Method.Should().Be(HttpMethod.Put);
            r.RequestUri!.Query.Should().Be("?version=2");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Content()) };
            response.Headers.ETag = new("\"3\"");
            return response;
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var client = new AnnouncementApiClient(http, new FakeApiSessionContext());
        (await client.DraftAsync(id, 2, Content(), default)).Version.Should().Be(3);
        handler.Response = _ => new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { code = "AnnouncementConflict" }) };
        FluentAssertions.Specialized.ExceptionAssertions<SecureOpsApiException> failure = await FluentActions.Awaiting(() => client.DraftAsync(id, 2, Content(), default)).Should().ThrowAsync<SecureOpsApiException>();
        failure.Which.Problem.RequiresRefresh.Should().BeTrue();
        handler.Calls.Should().Be(2);
        handler.Response = _ => new(HttpStatusCode.OK) { Content = JsonContent.Create(Content()) };
        await FluentActions.Awaiting(() => client.DraftAsync(id, 0, null, default)).Should().ThrowAsync<SecureOpsApiException>();
    }
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(503)]
    public async Task Client_PropagatesAuthorizationAndUnavailableResponses(int status)
    {
        using var handler = new Handler(_ => new((HttpStatusCode)status) { Content = JsonContent.Create(new { status }) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var client = new AnnouncementApiClient(http, new FakeApiSessionContext());
        FluentAssertions.Specialized.ExceptionAssertions<SecureOpsApiException> error = await FluentActions.Awaiting(() => client.ListAsync(1, default)).Should().ThrowAsync<SecureOpsApiException>();
        error.Which.Problem.StatusCode.Should().Be(status);
    }
    [Fact]
    public async Task Client_RejectsUntrustedDownloadNameAndRetainsValidationFieldCodes()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("not an email") });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var client = new AnnouncementApiClient(http, new FakeApiSessionContext());
        await FluentActions.Awaiting(() => client.DownloadAsync(Guid.NewGuid(), 1, default)).Should().ThrowAsync<SecureOpsApiException>();
        UiProblem problem = UiProblemFactory.FromResponse(400, new() { Code = "AnnouncementIncomplete", Fields = ["Subject", "To"] });
        problem.Fields.Should().Equal("Subject", "To");
        problem.Retryable.Should().BeFalse();
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Response { get; set; } = response;
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(Response(request)); }
    }
}
