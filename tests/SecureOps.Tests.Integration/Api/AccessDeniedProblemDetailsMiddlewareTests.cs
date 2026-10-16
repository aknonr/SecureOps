using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using SecureOps.Api.Middleware;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Integration.Api;

public sealed class AccessDeniedProblemDetailsMiddlewareTests
{
    [Theory]
    [InlineData(StatusCodes.Status401Unauthorized, true)]
    [InlineData(StatusCodes.Status401Unauthorized, false)]
    [InlineData(StatusCodes.Status403Forbidden, true)]
    [InlineData(StatusCodes.Status403Forbidden, false)]
    public async Task InvokeAsync_WhenAccessIsDenied_PreservesHeadersAndWritesProblemDetails(int status, bool challenge)
    {
        DefaultHttpContext context = new();
        using MemoryStream body = new();
        context.Response.Body = body;
        string[] authenticate = ["Negotiate", "Bearer"];
        AccessDeniedProblemDetailsMiddleware middleware = new(nextContext =>
        {
            nextContext.Response.StatusCode = status;
            nextContext.Response.Headers[CorrelationIdMiddleware.HeaderName] = "denial-test-123";
            if (challenge)
            {
                nextContext.Response.Headers.WWWAuthenticate = new StringValues(authenticate);
            }
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(status);
        context.Response.Headers[CorrelationIdMiddleware.HeaderName].Should().ContainSingle().Which.Should().Be("denial-test-123");
        if (challenge)
        {
            context.Response.Headers.WWWAuthenticate.Should().Equal(authenticate);
        }
        else
        {
            context.Response.Headers.ContainsKey("WWW-Authenticate").Should().BeFalse();
        }
        context.Response.ContentType.Should().Be("application/problem+json");
        body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(body);
        document.RootElement.GetProperty("status").GetInt32().Should().Be(status);
        document.RootElement.GetProperty("title").GetString().Should().Be("Access is denied.");
        document.RootElement.GetProperty("code").GetString().Should().Be(OperationalErrorCodes.AccessDenied);
        document.RootElement.GetProperty("stage").GetString().Should().Be("authorization");
        document.RootElement.GetProperty("retryable").GetBoolean().Should().BeFalse();
    }
}
