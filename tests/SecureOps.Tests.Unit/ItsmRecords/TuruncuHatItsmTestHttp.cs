using System.Net;
using Microsoft.Extensions.Logging;

namespace SecureOps.Tests.Unit.ItsmRecords;

/// <summary>Scripted HTTP doubles shared by the ITSM adapter tests. No real endpoint is ever contacted.</summary>
internal static class TuruncuHatItsmTestHttp
{
    public const string BaseUrl = "https://itsm.invalid/api/";

    public static HttpResponseMessage Ok(string body) => Response(HttpStatusCode.OK, body);

    public static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    public static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri(BaseUrl), Timeout = Timeout.InfiniteTimeSpan };

    public sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body));
            return _responses.Dequeue();
        }
    }

    public sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    public sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    public sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception) + " " + exception);
    }
}
