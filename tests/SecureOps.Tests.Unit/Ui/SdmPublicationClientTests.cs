using System.Net;
using FluentAssertions;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

public sealed class SdmPublicationClientTests
{
    [Fact]
    public async Task Create_KnownPreDispatchOutage_RemainsATransientReadFailure()
    {
        using var http = new HttpClient(new FailureHandler("source-outage")) { BaseAddress = new Uri("https://example.invalid/") };
        var client = new OperationalRecordApiClient(http, new FakeApiSessionContext());
        Func<Task> create = () => client.CreateJiraAsync(Guid.NewGuid(), CancellationToken.None);
        FluentAssertions.Specialized.ExceptionAssertions<SecureOpsApiException> failure = await create.Should().ThrowAsync<SecureOpsApiException>();
        failure.Which.Problem.Stage.Should().Be("source-validation");
        failure.Which.Problem.Retryable.Should().BeTrue();
    }

    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("invalid-success")]
    [InlineData("server-error")]
    [InlineData("body-loss")]
    public async Task Create_UnacknowledgedResult_IsUnknownAndNeverResent(string scenario)
    {
        using var handler = new FailureHandler(scenario);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
        var client = new OperationalRecordApiClient(http, new FakeApiSessionContext());
        Func<Task> create = () => client.CreateJiraAsync(Guid.NewGuid(), CancellationToken.None);
        FluentAssertions.Specialized.ExceptionAssertions<SecureOpsApiException> failure = await create.Should().ThrowAsync<SecureOpsApiException>();
        failure.Which.Problem.Retryable.Should().BeFalse();
        failure.Which.Problem.Stage.Should().Be("jira-reconciliation");
        handler.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(400, UiProblemKind.Validation)]
    [InlineData(403, UiProblemKind.Forbidden)]
    [InlineData(409, UiProblemKind.Conflict)]
    public async Task Create_KnownRejection_PreservesDistinctMeaning(int status, UiProblemKind kind)
    {
        using var http = new HttpClient(new FailureHandler(status.ToString(System.Globalization.CultureInfo.InvariantCulture))) { BaseAddress = new Uri("https://example.invalid/") };
        var client = new OperationalRecordApiClient(http, new FakeApiSessionContext());
        Func<Task> create = () => client.CreateJiraAsync(Guid.NewGuid(), CancellationToken.None);
        FluentAssertions.Specialized.ExceptionAssertions<SecureOpsApiException> failure = await create.Should().ThrowAsync<SecureOpsApiException>();
        failure.Which.Problem.Kind.Should().Be(kind);
        failure.Which.Problem.Stage.Should().NotBe("jira-reconciliation");
    }

    private sealed class FailureHandler(string scenario) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ++Calls;
            return scenario switch
            {
                "network" => throw new HttpRequestException("Synthetic transport loss"),
                "timeout" => throw new TaskCanceledException("Synthetic timeout"),
                "body-loss" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) }),
                "invalid-success" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{invalid") }),
                "source-outage" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"code\":\"OperationalSourceUnavailable\",\"stage\":\"source-validation\",\"retryable\":true}") }),
                _ => Task.FromResult(new HttpResponseMessage(scenario == "server-error" ? HttpStatusCode.InternalServerError : (HttpStatusCode)int.Parse(scenario, System.Globalization.CultureInfo.InvariantCulture)) { Content = new StringContent("{}") })
            };
        }
    }

    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("Synthetic response loss");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new IOException("Synthetic response loss");
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => throw new IOException("Synthetic response loss");
    }
}
