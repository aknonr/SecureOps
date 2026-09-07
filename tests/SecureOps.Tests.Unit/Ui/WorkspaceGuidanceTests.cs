using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

public sealed class WorkspaceGuidanceTests
{
    [Theory]
    [InlineData("links", 4)]
    [InlineData("groups", 4)]
    [InlineData("personalize", 3)]
    [InlineData("review", 4)]
    public void Guides_AreBoundedReadOnlyStepsWithoutManagementTargets(string surface, int count)
    {
        IReadOnlyList<ResourceGuideStep> steps = ResourceGuideSteps.For(surface, false);
        steps.Should().HaveCount(count);
        steps.Select(s => s.Target).Should().OnlyHaveUniqueItems();
        steps.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.Title) && !string.IsNullOrWhiteSpace(s.Description));
        steps.Should().NotContain(s => s.Target == "management-link");
        if (surface == "review")
        {
            steps.Should().Contain(s => s.Description.Contains("emekliliği", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task BrowseClient_EncodesLiteralQueryAndNeverUsesRefreshOrPublicationRoutes()
    {
        using var handler = new ReadHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
        var client = new OperationalRecordApiClient(http, new FakeApiSessionContext());
        OperationalRecordPageResponse page = await client.BrowseAsync(new() { Search = "A&B %", Page = 2, PageSize = 10 }, default);
        page.Total.Should().Be(12);
        handler.Calls.Should().Be(1);
        handler.Uri!.AbsolutePath.Should().Be("/api/v1/operational-records/stored");
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(handler.Uri.Query)["search"].ToString().Should().Be("A&B %");
    }

    private sealed class ReadHandler : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        internal Uri? Uri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            request.Method.Should().Be(HttpMethod.Get);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new OperationalRecordPageResponse([], 12, 2, 10)) });
        }
    }
}
