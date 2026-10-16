using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Unit.OperationalRecords;

public sealed class ResponseBodyTimeoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_WhenBodyStalls_CancelsWithinReadTimeout(bool withLength)
    {
        using StreamContent content = new(new StalledBodyStream());
        var readTimeout = TimeSpan.FromMilliseconds(150);
        Func<Task> read = async () =>
        {
            if (withLength)
            {
                BoundedJsonReadResult result = await BoundedJsonHttpContent.ReadWithLengthAsync(
                    content, 1024, CancellationToken.None, readTimeout);
                result.Document.Dispose();
            }
            else
            {
                using JsonDocument document = await BoundedJsonHttpContent.ReadAsync(
                    content, 1024, CancellationToken.None, readTimeout);
            }
        };

        Func<Task> act = () => read().WaitAsync(TimeSpan.FromSeconds(10));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadAsync_WhenCallerIsCanceled_PropagatesCancellation()
    {
        using StreamContent content = new(new StalledBodyStream());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> act = () => BoundedJsonHttpContent.ReadAsync(content, 1024, cancellation.Token, TimeSpan.FromSeconds(10));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetSessionAsync_WhenLoginBodyStalls_ReturnsRetryableSourceUnavailable()
    {
        using HttpClient client = new(new StalledBodyHandler())
        {
            BaseAddress = new Uri("https://source.invalid/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        TuruncuHatSessionManager manager = new(client,
            Options.Create(new TuruncuHatOptions { RequestTimeoutSeconds = 1 }), TimeProvider.System,
            new EnterpriseIntegrationHealthState(), new EnterpriseIntegrationTelemetry(),
            NullLogger<TuruncuHatSessionManager>.Instance);

        Func<Task> act = () => manager.GetSessionAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        await act.Should().ThrowAsync<ExternalIntegrationException>()
            .Where(exception => exception.ErrorCode == OperationalErrorCodes.OperationalSourceUnavailable
                && exception.Retryable && !exception.OutcomeUnknown);
    }

    private sealed class StalledBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledBodyStream()) });
    }

    private sealed class StalledBodyStream : MemoryStream
    {
        private bool _started;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_started)
            {
                _started = true;
                buffer.Span[0] = (byte)'{';
                return 1;
            }
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
