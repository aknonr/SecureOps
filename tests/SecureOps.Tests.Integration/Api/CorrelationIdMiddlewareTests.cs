using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SecureOps.Api.Middleware;

namespace SecureOps.Tests.Integration.Api;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenHeaderIsValid_UsesHeaderAsTraceIdentifier()
    {
        DefaultHttpContext context = new();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "lookup-test-123";
        CorrelationIdMiddleware middleware = new(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.TraceIdentifier.Should().Be("lookup-test-123");
        context.Response.Headers[CorrelationIdMiddleware.HeaderName].Should().Contain("lookup-test-123");
    }
}
